using StockMate.Models;

namespace StockMate.Services;

public sealed class ScanEngine(MarketDataService market, AppDataService data, UniverseService universe)
{
    public int UniverseCount => universe.Symbols.Count;

    public string GetSessionKey(bool intraday, DateTime? now = null)
    {
        var session = ResolveSession(intraday, now ?? DateTime.Now);
        return $"{session.TradingDate:yyyy-MM-dd}-{(session.Intraday ? "S1" : "S2")}";
    }

    public MarketSnapshot? GetSnapshot(bool intraday) =>
        data.State.MarketSnapshots.LastOrDefault(x => x.SessionKey == GetSessionKey(intraday));

    public bool UseLatestClosing(DateTime? value = null)
    {
        var now = value ?? DateTime.Now;
        if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        var lunchClose = new TimeSpan(
            now.DayOfWeek == DayOfWeek.Friday ? 11 : 12,
            now.DayOfWeek == DayOfWeek.Friday ? 30 : 0, 0);
        return now.TimeOfDay >= lunchClose && now.TimeOfDay < new TimeSpan(16, 0, 0);
    }

    public MarketSnapshot? GetLatestSnapshot() => GetSnapshot(UseLatestClosing());

    public async Task<MarketSnapshot> RefreshMarketDataAsync(
        bool intraday, bool force, IProgress<ScanProgress>? progress, CancellationToken ct,
        bool requireVerifiedClosing = false)
    {
        var preparationStarted = DateTime.UtcNow;
        if (!universe.HasFullUniverse)
        {
            progress?.Report(new()
            {
                Stage = "UNIVERSE_REQUEST",
                Message = $"Master universe belum lengkap ({data.State.MarketUniverse.Count} tersimpan); sinkronisasi IDX",
                Total = data.State.MarketUniverse.Count,
                Source = "IDX Listed Company",
                TechnicalDetail = "Fallback 99 tidak akan dipakai sebagai full universe"
            });
            var refreshed = await universe.EnsureCurrentAsync(true, ct, progress);
            if (!universe.HasFullUniverse)
                throw new InvalidOperationException(
                    $"Master universe belum lengkap ({data.State.MarketUniverse.Count} saham tersimpan). " +
                    "Pembaruan IDX gagal. Buka Atur > Perbarui master IDX atau impor file universe; scanner tidak menjalankan fallback 99.");
        }
        var session = ResolveSession(intraday, DateTime.Now);
        var knownTotal = universe.Symbols
            .Where(x => data.State.IncludeSpeculative || !universe.IsSpeculative(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        progress?.Report(new()
        {
            Stage = "UNIVERSE",
            Message = $"Cache universe ditemukan: {knownTotal} saham",
            Total = knownTotal,
            Source = "cache lokal",
            TechnicalDetail = "Menghitung filter spekulatif dan kode unik; belum melakukan request harga"
        });
        // A cached universe is sufficient to scan. Do not block batch formation
        // on a network refresh; users can force-refresh it from Settings.
        var universeResult = (Count: knownTotal, Updated: false,
            Message: "Memakai universe aktif agar scan dapat langsung dimulai.");
        progress?.Report(new()
        {
            Stage = "UNIVERSE_READY",
            Message = $"{universeResult.Message} Universe aktif: {universeResult.Count} saham",
            Total = universeResult.Count,
            Source = "cache lokal",
            ElapsedMilliseconds = (long)(DateTime.UtcNow - preparationStarted).TotalMilliseconds,
            TechnicalDetail = "Universe dibekukan untuk sesi ini; membentuk batch 100 saham"
        });
        progress?.Report(new()
        {
            Stage = "TRADING_DATE",
            Message = session.IsPreviousTradingDay
                ? $"Bursa hari ini tutup/belum closing; memakai closing {session.TradingDate:dddd, dd MMMM yyyy}"
                : $"Memakai sesi {(session.Intraday ? "1" : "2")} {session.TradingDate:dddd, dd MMMM yyyy}",
            Total = knownTotal,
            Source = "kalender sesi",
            TechnicalDetail = $"Tanggal acuan {session.TradingDate:yyyy-MM-dd}; data bukan real-time"
        });
        await WaitForClosingAsync(session, progress, ct, requireVerifiedClosing);

        var key = GetSessionKey(intraday);
        var cached = data.State.MarketSnapshots.LastOrDefault(x => x.SessionKey == key);
        if (cached?.IsComplete == true && cached.FailedSymbols.Count == 0 &&
            cached.RequestedCount == knownTotal && !force) return cached;

        var symbols = universe.Symbols
            .Where(x => data.State.IncludeSpeculative || !universe.IsSpeculative(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var snapshot = cached is not null && !force ? cached : new MarketSnapshot
        {
            SessionKey = key, Session = session.Intraday ? "LUNCH" : "EVENING",
            CapturedAt = DateTime.Now, RequestedCount = symbols.Length,
            Status = "IN_PROGRESS", ClosingVerified = true,
            TradingDate = session.TradingDate
        };
        if (force)
            data.State.MarketSnapshots.RemoveAll(x => x.SessionKey == key);
        if (!data.State.MarketSnapshots.Contains(snapshot))
            data.State.MarketSnapshots.Add(snapshot);
        snapshot.RequestedCount = symbols.Length;
        snapshot.ClosingVerified = true;
        snapshot.IsComplete = false;
        snapshot.Status = "IN_PROGRESS";
        var alreadyDone = snapshot.Symbols.Select(x => x.Symbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var completed = alreadyDone.Count;
        progress?.Report(new()
        {
            Stage = "DOWNLOAD",
            Message = "Mengambil data harga seluruh emiten",
            Completed = completed,
            Total = symbols.Length,
            Succeeded = snapshot.Symbols.Count,
            Failed = snapshot.FailedSymbols.Count
        });
        const int batchSize = 100;
        var totalBatches = (int)Math.Ceiling(symbols.Length / (double)batchSize);
        progress?.Report(new()
        {
            Stage = "BATCH_PLAN",
            Message = $"{symbols.Length} saham dibagi menjadi {totalBatches} batch",
            Total = symbols.Length,
            TotalBatches = totalBatches,
            BatchSize = Math.Min(batchSize, symbols.Length),
            Source = "scheduler lokal",
            TechnicalDetail = "Batch plan selesai; request harga dimulai berurutan"
        });
        for (var batchIndex = 0; batchIndex < totalBatches; batchIndex++)
        {
            var batchNumber = batchIndex + 1;
            var batchSymbols = symbols.Skip(batchIndex * batchSize).Take(batchSize).ToArray();
            var batchDone = batchSymbols.Count(alreadyDone.Contains);
            progress?.Report(new()
            {
                Stage = "BATCH_START",
                Message = $"Memulai batch {batchNumber}/{totalBatches}",
                Completed = completed, Total = symbols.Length,
                Succeeded = snapshot.Symbols.Count, Failed = snapshot.FailedSymbols.Count,
                CurrentBatch = batchNumber, TotalBatches = totalBatches,
                BatchCompleted = batchDone, BatchSize = batchSymbols.Length,
                LastCompletedBatch = Math.Max(0, batchNumber - 1)
            });

            foreach (var symbol in batchSymbols)
            {
                ct.ThrowIfCancellationRequested();
                if (alreadyDone.Contains(symbol)) continue;
                try
                {
                    var requestStarted = DateTime.UtcNow;
                    progress?.Report(new()
                    {
                        Stage = "REQUEST_START",
                        Message = $"Request harga {symbol}",
                        CurrentSymbol = symbol, Completed = completed, Total = symbols.Length,
                        Succeeded = snapshot.Symbols.Count, Failed = snapshot.FailedSymbols.Count,
                        CurrentBatch = batchNumber, TotalBatches = totalBatches,
                        BatchCompleted = batchDone, BatchSize = batchSymbols.Length,
                        LastCompletedBatch = Math.Max(0, batchNumber - 1),
                        Source = "Yahoo Finance",
                        Attempt = 1,
                        TechnicalDetail = "HTTP chart request • timeout 20 detik"
                    });
                    var candles = await GetWithRetryAsync(symbol, session.Intraday, progress, ct);
                    candles = candles.Where(x => x.Time.Date <= session.TradingDate.Date).ToList();
                    if (candles.Count > 0)
                    {
                        snapshot.Symbols.Add(new SymbolMarketData
                        {
                            Symbol = symbol,
                            IsSpeculative = universe.IsSpeculative(symbol),
                            Candles = candles
                        });
                        // Publish a successful symbol immediately. Portfolio
                        // detail no longer waits for the full IDX scan.
                        data.ApplyMarketPrice(symbol, candles[^1].Close);
                        progress?.Report(new()
                        {
                            Stage = "REQUEST_OK",
                            Message = $"{symbol} diterima: {candles.Count} candle",
                            CurrentSymbol = symbol, Completed = completed, Total = symbols.Length,
                            Succeeded = snapshot.Symbols.Count, Failed = snapshot.FailedSymbols.Count,
                            CurrentBatch = batchNumber, TotalBatches = totalBatches,
                            BatchCompleted = batchDone, BatchSize = batchSymbols.Length,
                            Source = "Yahoo Finance",
                            ElapsedMilliseconds = (long)(DateTime.UtcNow - requestStarted).TotalMilliseconds,
                            TechnicalDetail = "Respons berhasil diparse dan masuk checkpoint"
                        });
                        snapshot.FailedSymbols.RemoveAll(x => x.Equals(symbol, StringComparison.OrdinalIgnoreCase));
                    }
                    else snapshot.FailedSymbols.Add(symbol);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    if (!snapshot.FailedSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
                        snapshot.FailedSymbols.Add(symbol);
                    snapshot.Errors.Add($"{symbol}: {ex.Message}");
                    progress?.Report(new()
                    {
                        Stage = "REQUEST_ERROR",
                        Message = $"Request {symbol} gagal; proses lanjut",
                        CurrentSymbol = symbol, Completed = completed, Total = symbols.Length,
                        Succeeded = snapshot.Symbols.Count, Failed = snapshot.FailedSymbols.Count,
                        CurrentBatch = batchNumber, TotalBatches = totalBatches,
                        BatchCompleted = batchDone, BatchSize = batchSymbols.Length,
                        Source = "Yahoo Finance",
                        TechnicalDetail = $"{ex.GetType().Name}: {ex.Message}"
                    });
                    if (snapshot.Errors.Count > 100) snapshot.Errors.RemoveAt(0);
                }
                finally
                {
                    completed++;
                    batchDone++;
                    snapshot.CompletedCount = completed;
                    progress?.Report(new()
                    {
                        Stage = "DOWNLOAD",
                        Message = $"Batch {batchNumber}/{totalBatches} • mengambil data",
                        CurrentSymbol = symbol, Completed = completed, Total = symbols.Length,
                        Succeeded = snapshot.Symbols.Count, Failed = snapshot.FailedSymbols.Count,
                        CurrentBatch = batchNumber, TotalBatches = totalBatches,
                        BatchCompleted = batchDone, BatchSize = batchSymbols.Length,
                        LastCompletedBatch = Math.Max(0, batchNumber - 1)
                    });
                    // Persisting the full candle snapshot after every symbol
                    // repeatedly serializes several megabytes and stalls the UI.
                    // A small checkpoint interval retains resume support without
                    // hundreds of full-file writes.
                    if (batchDone % 10 == 0)
                        await data.SaveAsync();
                }
                if (data.State.RequestDelayMilliseconds > 0)
                    await Task.Delay(data.State.RequestDelayMilliseconds, ct);
            }

            progress?.Report(new()
            {
                Stage = "BATCH_COMPLETE",
                Message = $"Batch {batchNumber}/{totalBatches} selesai",
                Completed = completed, Total = symbols.Length,
                Succeeded = snapshot.Symbols.Count, Failed = snapshot.FailedSymbols.Count,
                CurrentBatch = batchNumber, TotalBatches = totalBatches,
                BatchCompleted = batchSymbols.Length, BatchSize = batchSymbols.Length,
                LastCompletedBatch = batchNumber
            });
            await data.SaveAsync();
        }

        snapshot.IsComplete = snapshot.CompletedCount >= snapshot.RequestedCount;
        snapshot.Status = snapshot.IsComplete
            ? (snapshot.FailedSymbols.Count == 0 ? "COMPLETE" : "COMPLETE_WITH_ERRORS")
            : "PARTIAL";
        data.State.MarketSnapshots = data.State.MarketSnapshots
            .OrderByDescending(x => x.CapturedAt).Take(6).OrderBy(x => x.CapturedAt).ToList();
        await data.SaveAsync();
        return snapshot;
    }

    async Task<List<Candle>> GetWithRetryAsync(
        string symbol, bool intraday, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var waits = new[] { TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5) };
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (attempt > 0)
                    progress?.Report(new()
                    {
                        Stage = "RETRY",
                        Message = $"Mencoba ulang {symbol}",
                        CurrentSymbol = symbol,
                        Attempt = attempt + 1,
                        Source = "Yahoo Finance",
                        TechnicalDetail = $"Percobaan ke-{attempt + 1}"
                    });
                return await market.GetCandlesAsync(symbol, intraday, ct);
            }
            catch (MarketRateLimitException ex) when (attempt < waits.Length)
            {
                var wait = ex.RetryAfter is { } server && server > waits[attempt]
                    ? server : waits[attempt];
                progress?.Report(new()
                {
                    Stage = "RATE_LIMIT",
                    Message = $"Rate limit {symbol}; jeda {wait.TotalSeconds:0} detik",
                    CurrentSymbol = symbol,
                    Attempt = attempt + 1,
                    Source = "Yahoo Finance",
                    TechnicalDetail = $"HTTP 429 • retry otomatis setelah {wait.TotalSeconds:0} detik"
                });
                await Task.Delay(wait, ct);
            }
            catch (MarketAccessForbiddenException) when (attempt < 3)
            {
                progress?.Report(new()
                {
                    Stage = "FORBIDDEN_RETRY",
                    Message = $"HTTP 403 {symbol}; ganti endpoint dan ulang",
                    CurrentSymbol = symbol,
                    Attempt = attempt + 1,
                    Source = "Yahoo Finance",
                    TechnicalDetail = "Query1 dan Query2 ditolak sementara; membuat request baru"
                });
                await Task.Delay(TimeSpan.FromSeconds(3 + attempt * 4), ct);
            }
            catch (HttpRequestException) when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt == 0 ? 2 : 5), ct);
            }
        }
    }

    async Task WaitForClosingAsync(
        SessionResolution session, IProgress<ScanProgress>? progress,
        CancellationToken ct, bool requireVerifiedClosing)
    {
        var now = DateTime.Now;
        var intraday = session.Intraday;
        var close = session.TradingDate.Date.Add(intraday
            ? (session.TradingDate.DayOfWeek == DayOfWeek.Friday
                ? new TimeSpan(11, 30, 0) : new TimeSpan(12, 0, 0))
            : new TimeSpan(16, 0, 0));

        var references = new[] { "BBCA", "BBRI", "TLKM", "ASII" };
        var started = DateTime.UtcNow;
        const int maximumChecks = 3;
        for (var check = 1; check <= maximumChecks; check++)
        {
            ct.ThrowIfCancellationRequested();
            var verified = 0;
            foreach (var symbol in references)
            {
                try
                {
                    var candles = await market.GetCandlesAsync(symbol, intraday, ct);
                    var latest = candles.LastOrDefault()?.Time;
                    if (latest?.Date == session.TradingDate.Date &&
                        (!intraday || latest >= close.AddMinutes(-20))) verified++;
                }
                catch { }
            }
            if (verified >= 3) return;
            progress?.Report(new()
            {
                Stage = "WAITING_CLOSE",
                Message = $"Verifikasi closing {check}/{maximumChecks}: {verified}/4 acuan siap",
                Source = "Yahoo Finance",
                Attempt = check,
                ElapsedMilliseconds = (long)(DateTime.UtcNow - started).TotalMilliseconds,
                TechnicalDetail = check < maximumChecks
                    ? "Belum cukup acuan; ulang 15 detik lagi"
                    : "Batas verifikasi tercapai; lanjut memakai data tersedia"
            });
            if (check < maximumChecks)
                await Task.Delay(TimeSpan.FromSeconds(15), ct);
        }
        progress?.Report(new()
        {
            Stage = "CLOSING_FALLBACK",
            Message = "Closing belum terverifikasi penuh; scan dilanjutkan",
            Source = "fallback waktu sesi",
            TechnicalDetail = "Mencegah scanner menunggu tanpa batas; harga tetap wajib dicocokkan di Stockbit"
        });
        if (requireVerifiedClosing)
            throw new ClosingDataNotReadyException(
                $"Data closing sesi {(session.Intraday ? "1" : "2")} belum tersedia lengkap.");
    }

    static SessionResolution ResolveSession(bool requestedIntraday, DateTime now)
    {
        var date = now.Date;
        var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var requestedClose = requestedIntraday
            ? new TimeSpan(date.DayOfWeek == DayOfWeek.Friday ? 11 : 12,
                date.DayOfWeek == DayOfWeek.Friday ? 30 : 0, 0)
            : new TimeSpan(16, 0, 0);
        var usePrevious = isWeekend || now.TimeOfDay < requestedClose;
        if (usePrevious)
        {
            do date = date.AddDays(-1);
            while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
            return new SessionResolution(date, false, true);
        }
        return new SessionResolution(date, requestedIntraday, false);
    }

    readonly record struct SessionResolution(
        DateTime TradingDate, bool Intraday, bool IsPreviousTradingDay);

    public async Task<List<ScanResult>> AnalyzeAsync(
        bool intraday, MarketSnapshot snapshot, bool usedCache,
        IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        if (!snapshot.IsComplete)
            throw new InvalidOperationException(
                $"Snapshot masih parsial ({snapshot.CompletedCount}/{snapshot.RequestedCount}). Lanjutkan pengambilan data sebelum analisis.");
        var output = new List<ScanResult>();
        var completed = 0;
        foreach (var item in snapshot.Symbols)
        {
            ct.ThrowIfCancellationRequested();
            var result = Evaluate(item.Symbol, item.Candles, intraday, item.IsSpeculative);
            if (result is not null)
            {
                // The source candle timestamp can be UTC or session-open time.
                // For a closing scanner, show the resolved IDX trading date and
                // explicit session instead of presenting 09:00 as the scan time.
                result.DataTime = snapshot.TradingDate ?? result.DataTime.Date;
                result.DataSession = snapshot.Session == "LUNCH"
                    ? "Closing Sesi 1"
                    : "Closing Sesi 2";
                output.Add(result);
            }
            completed++;
            progress?.Report(new()
            {
                Stage = "ANALYZE", Message = "Menganalisis kandidat",
                CurrentSymbol = item.Symbol,
                Completed = completed, Total = snapshot.Symbols.Count,
                Succeeded = output.Count
            });
        }
        AllocateAvailableCash(output);
        var allResults = output
            .OrderByDescending(x => x.AllocationRank > 0)
            .ThenBy(x => x.AllocationRank == 0 ? int.MaxValue : x.AllocationRank)
            .ThenByDescending(x => x.Score)
            .ThenByDescending(x => x.RiskReward)
            .ThenByDescending(x => x.LastPrice)
            .ToList();
        EvaluateHistory(allResults, intraday);
        data.ApplyMarketPrices(snapshot);
        // Keep all evaluated shares for audit, but only actionable BUY AREA
        // results are recommendations and prediction-performance observations.
        var ranked = allResults;
        data.State.LastScan = ranked;
        var existingRun = data.State.ScanHistory.LastOrDefault(x =>
            x.SessionKey == snapshot.SessionKey &&
            x.StrategyVersion == data.State.Strategy.Version);
        var preservedOutcomes = existingRun?.Predictions
            .ToDictionary(x => x.Symbol, StringComparer.OrdinalIgnoreCase);
        var run = existingRun ?? new ScanRun();
        run.RunTime = DateTime.Now;
        run.Session = intraday ? "LUNCH" : "EVENING";
        run.StrategyVersion = data.State.Strategy.Version;
        run.SessionKey = snapshot.SessionKey;
        run.MarketDataCapturedAt = snapshot.CapturedAt;
        run.UsedCachedMarketData = usedCache;
        run.UniverseCount = snapshot.RequestedCount;
        run.SuccessfulCount = snapshot.Symbols.Count;
        run.SnapshotComplete = snapshot.IsComplete;
        run.EvaluatedCount = ranked.Count;
        var recommendations = ranked
            .Where(x => x.Verdict == "BUY AREA" && x.SuggestedLots > 0)
            .Take(30)
            .ToList();
        run.ShortlistCount = recommendations.Count;
        run.Predictions = recommendations.Select(x =>
        {
            if (preservedOutcomes?.TryGetValue(x.Symbol, out var prior) == true &&
                prior.Outcome != "PENDING") return prior;
            return new PredictionRecord
            {
                Symbol = x.Symbol, Verdict = x.Verdict, Score = x.Score,
                StartPrice = x.LastPrice, StopLoss = x.StopLoss, Target1 = x.Target1,
                PredictedAt = DateTime.Now, DataSession = x.DataSession
            };
        }).ToList();
        progress?.Report(new()
        {
            Stage = "SAVING",
            Message = "Menyimpan hasil scan dan keputusan portofolio",
            Completed = snapshot.Symbols.Count,
            Total = snapshot.Symbols.Count,
            Succeeded = output.Count,
            Failed = snapshot.FailedSymbols.Count
        });
        if (existingRun is null) data.State.ScanHistory.Add(run);
        if (data.State.ScanHistory.Count > 60)
            data.State.ScanHistory = data.State.ScanHistory.OrderByDescending(x => x.RunTime).Take(60).OrderBy(x => x.RunTime).ToList();
        await data.SaveAsync();
        return ranked;
    }

    void AllocateAvailableCash(List<ScanResult> results)
    {
        var remaining = Math.Max(0m, data.State.Cash);
        var ownedSymbols = data.State.Positions
            .Where(x => x.Shares > 0)
            .Select(x => x.Symbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rank = 1;
        foreach (var item in results)
        {
            item.AllocationRank = 0;
            item.AllocatedCash = 0;
        }
        foreach (var item in results
                     // Existing positions are governed by PortfolioDecisionService.
                     // They must never compete with new-entry candidates for cash
                     // or show a contradictory scanner buy recommendation.
                     .Where(x => x.Verdict == "BUY AREA" && !ownedSymbols.Contains(x.Symbol))
                     .OrderByDescending(x => x.Score)
                     .ThenByDescending(x => x.RiskReward)
                     .ThenBy(x => x.EntryHigh))
        {
            var oneLotCash = item.EntryHigh * 100m * (1m + data.State.BuyFeeRate);
            var affordableLots = oneLotCash <= 0 ? 0 : (int)Math.Floor(remaining / oneLotCash);
            var allocatedLots = Math.Min(item.SuggestedLots, affordableLots);
            if (allocatedLots <= 0)
            {
                item.SuggestedLots = 0;
                item.Verdict = "WATCH";
                item.ExecutionNote = oneLotCash > data.State.Cash
                    ? $"Minimal 1 lot memerlukan sekitar Rp{oneLotCash:N0}; kas Rp{data.State.Cash:N0}."
                    : "Kas sudah dialokasikan ke kandidat dengan prioritas lebih tinggi.";
                continue;
            }
            item.SuggestedLots = allocatedLots;
            item.AllocationRank = rank++;
            item.AllocatedCash = allocatedLots * oneLotCash;
            item.ExecutionNote = $"Prioritas alokasi #{item.AllocationRank} • estimasi dana Rp{item.AllocatedCash:N0} termasuk fee.";
            remaining -= item.AllocatedCash;
        }
        foreach (var item in results.Where(x => ownedSymbols.Contains(x.Symbol)))
        {
            item.AllocationRank = 0;
            item.AllocatedCash = 0;
            item.SuggestedLots = 0;
            item.ExecutionNote =
                "Saham sudah dimiliki; tindakan mengikuti keputusan Portofolio.";
        }
    }

    public async Task<(List<ScanResult> Results, bool UsedCache, MarketSnapshot Snapshot)> RunAsync(
        bool intraday, bool forceRefresh, IProgress<ScanProgress>? progress, CancellationToken ct,
        bool requireVerifiedClosing = false)
    {
        var existing = GetSnapshot(intraday);
        var snapshot = await RefreshMarketDataAsync(
            intraday, forceRefresh, progress, ct, requireVerifiedClosing);
        var usedCache = existing is not null && !forceRefresh;
        var analyzeIntraday = snapshot.Session == "LUNCH";
        var results = await AnalyzeAsync(analyzeIntraday, snapshot, usedCache, progress, ct);
        return (results, usedCache, snapshot);
    }

    void EvaluateHistory(IReadOnlyCollection<ScanResult> latest, bool intraday)
    {
        var priceBySymbol = latest.ToDictionary(x => x.Symbol, x => x.LastPrice);
        var now = DateTime.Now;
        foreach (var run in data.State.ScanHistory)
        {
            var minimumAge = run.Session == "LUNCH" ? TimeSpan.FromHours(2) : TimeSpan.FromHours(12);
            if (now - run.RunTime < minimumAge) continue;
            foreach (var prediction in run.Predictions.Where(x => x.Outcome == "PENDING"))
            {
                if (!priceBySymbol.TryGetValue(prediction.Symbol, out var price)) continue;
                prediction.EvaluatedAt = now;
                prediction.EvaluationPrice = price;
                prediction.ReturnPercent = prediction.StartPrice == 0 ? 0 : (price / prediction.StartPrice - 1) * 100;
                prediction.Outcome = price >= prediction.Target1 ? "TARGET"
                    : price <= prediction.StopLoss ? "STOP"
                    : prediction.ReturnPercent > 0 ? "POSITIVE" : "NEGATIVE";
            }
        }
    }

    ScanResult? Evaluate(string symbol, List<Candle> c, bool intraday, bool speculative)
    {
        var min = intraday ? 20 : 55;
        if (c.Count < min) return null;
        var last = c[^1];
        var closes = c.Select(x => x.Close).ToArray();
        var sma20 = closes.TakeLast(20).Average();
        var sma50 = closes.TakeLast(Math.Min(50, closes.Length)).Average();
        var avgVolume = c.TakeLast(20).Average(x => (decimal)x.Volume);
        var rsi = Rsi(closes, 14);
        var atr = c.TakeLast(14).Average(x => x.High - x.Low);
        if (atr <= 0 || last.Close <= 0) return null;
        // Continuous components deliberately avoid score piles such as 80/100.
        // The former implementation only used +15/+10 blocks, so many unrelated
        // shares received an identical rating.
        var scoreValue = 35m;
        var reasons = new List<string>();
        var risks = new List<string>();
        if (last.Close > sma20)
        {
            scoreValue += 10m + Math.Min(7m, (last.Close / sma20 - 1m) * 100m);
            reasons.Add("harga di atas rata-rata 20 periode");
        }
        else risks.Add("harga masih di bawah rata-rata 20 periode");
        if (sma20 > sma50)
        {
            scoreValue += 9m + Math.Min(7m, (sma20 / sma50 - 1m) * 100m);
            reasons.Add("tren menengah naik");
        }
        var volumeRatio = avgVolume <= 0 ? 0 : last.Volume / avgVolume;
        if (volumeRatio > data.State.Strategy.VolumeConfirmation)
        {
            scoreValue += 8m + Math.Min(8m, (volumeRatio - data.State.Strategy.VolumeConfirmation) * 8m);
            reasons.Add("volume lebih kuat dari normal");
        }
        if (rsi is >= 48 and <= 68)
        {
            scoreValue += 6m + Math.Max(0m, 5m - Math.Abs(rsi - 58m) / 2m);
            reasons.Add("momentum sehat, belum terlalu panas");
        }
        if (rsi > 72) { scoreValue -= 15m + Math.Min(8m, rsi - 72m); risks.Add("momentum sudah terlalu panas"); }
        if (speculative) { scoreValue -= 8m; risks.Add("kategori spekulatif: ukuran posisi wajib kecil"); }
        var stop = RoundPrice(last.Close - atr * data.State.Strategy.AtrStopMultiplier);
        var risk = last.Close - stop;
        if (risk <= 0) return null;
        var minimumRiskReward = Math.Max(data.State.MinRiskReward, data.State.Strategy.MinimumRiskReward);
        var target1 = RoundPrice(last.Close + risk * minimumRiskReward);
        var target2 = RoundPrice(last.Close + risk * 3m);
        var lotsByRisk = (int)Math.Floor(data.State.RiskPerTrade / (risk * 100m));
        var maxValue = speculative ? data.State.Strategy.MaximumSpeculativePosition : data.State.Strategy.MaximumNormalPosition;
        var lotsByValue = (int)Math.Floor(maxValue / (last.Close * 100m));
        var lots = Math.Max(0, Math.Min(lotsByRisk, lotsByValue));
        var score = Math.Clamp((int)Math.Round(scoreValue), 0, 100);
        var age = DateTime.Now - last.Time;
        if (intraday && age > TimeSpan.FromMinutes(60))
            risks.Add("snapshot intraday sudah lama; jangan entry tanpa cek harga berjalan di Stockbit");
        return new ScanResult
        {
            Symbol = symbol,
            Verdict = score >= data.State.Strategy.BuyScore && lots > 0
                ? "BUY AREA"
                : score >= data.State.Strategy.BuyScore
                    ? "PANTAU — LOT 0"
                    : score >= data.State.Strategy.WatchScore ? "WATCH" : "WAIT",
            Score = score,
            LastPrice = RoundPrice(last.Close),
            // One executable limit price, not a range. Keeping both legacy
            // fields equal preserves existing saved-data compatibility.
            EntryLow = RoundPrice(last.Close + atr * .25m),
            EntryHigh = RoundPrice(last.Close + atr * .25m),
            MaxBuyPrice = RoundPrice(last.Close + atr * .5m),
            StopLoss = stop,
            Target1 = target1,
            Target2 = target2,
            SuggestedLots = lots,
            RiskReward = minimumRiskReward,
            DataTime = last.Time,
            DataSession = intraday ? "Closing Sesi 1" : "Closing Sesi 2",
            Reasons = reasons.Count == 0 ? "belum ada konfirmasi kuat" : string.Join(", ", reasons),
            Risks = risks.Count == 0 ? "tetap konfirmasi harga dan kondisi IHSG di Stockbit" : string.Join(", ", risks),
            IsSpeculative = speculative
        };
    }

    static decimal Rsi(decimal[] values, int period)
    {
        decimal gain = 0, loss = 0;
        foreach (var pair in values.TakeLast(period + 1).Zip(values.TakeLast(period), (a, b) => (a, b)))
        {
            var d = pair.a - pair.b;
            if (d > 0) gain += d; else loss -= d;
        }
        if (loss == 0) return 100;
        var rs = gain / loss;
        return 100 - 100 / (1 + rs);
    }

    static decimal RoundPrice(decimal value)
    {
        var step = value < 200 ? 1 : value < 500 ? 2 : value < 2000 ? 5 : value < 5000 ? 10 : 25;
        return Math.Max(step, Math.Round(value / step) * step);
    }
}

public sealed class ClosingDataNotReadyException(string message) : Exception(message);
