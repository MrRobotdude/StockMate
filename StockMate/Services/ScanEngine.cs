using StockMate.Models;
using StockMate.Ui;

namespace StockMate.Services;

public sealed class ScanEngine(
    MarketDataService market, AppDataService data, UniverseService universe,
    EventIntelligenceService events)
{
    public int UniverseCount => universe.Symbols.Count;

    public string GetSessionKey(bool intraday, DateTime? now = null)
    {
        var session = ResolveSession(intraday, now ?? DateTime.Now);
        return $"{session.TradingDate:yyyy-MM-dd}-{(session.Intraday ? "S1" : "S2")}";
    }

    public MarketSnapshot? GetSnapshot(bool intraday)
    {
        var session = ResolveSession(intraday, DateTime.Now);
        var exactKey =
            $"{session.TradingDate:yyyy-MM-dd}-{(session.Intraday ? "S1" : "S2")}";
        var exact = data.State.MarketSnapshots.LastOrDefault(
            x => x.SessionKey == exactKey);
        if (exact is not null || !session.IsPreviousTradingDay)
            return exact;

        // On an IDX holiday, the previous calendar weekday may have no candle.
        // Fall back to the latest completed S2 snapshot on or before that date.
        return data.State.MarketSnapshots
            .Where(x => x.Session == "EVENING" &&
                        x.TradingDate <= session.TradingDate)
            .OrderByDescending(x => x.TradingDate)
            .ThenByDescending(x => x.CapturedAt)
            .FirstOrDefault();
    }

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
                throw new InvalidOperationException(Loc.T(
                    $"Master universe belum lengkap ({data.State.MarketUniverse.Count} saham tersimpan). " +
                    "Pembaruan IDX gagal. Buka Atur > Perbarui master IDX atau impor file universe; scanner tidak menjalankan fallback 99.",
                    $"The master universe is incomplete ({data.State.MarketUniverse.Count} saved stocks). " +
                    "The IDX update failed. Open Settings > Update IDX master or import a universe file; the scanner will not use the 99-stock fallback."));
        }
        var session = ResolveSession(intraday, DateTime.Now);
        session = await ResolveAvailablePreviousCloseAsync(
            session, ct);
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

        var key =
            $"{session.TradingDate:yyyy-MM-dd}-{(session.Intraday ? "S1" : "S2")}";
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
                    var sessionOneClose = session.TradingDate.Date.Add(
                        session.TradingDate.DayOfWeek == DayOfWeek.Friday
                            ? new TimeSpan(11, 30, 0)
                            : new TimeSpan(12, 0, 0));
                    candles = candles.Where(x =>
                    {
                        if (x.Time.Date > session.TradingDate.Date)
                            return false;
                        if (!session.Intraday ||
                            x.Time.Date < session.TradingDate.Date)
                            return true;
                        return x.Time <= sessionOneClose;
                    }).ToList();
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
                Loc.T(
                    $"Data closing sesi {(session.Intraday ? "1" : "2")} belum tersedia lengkap.",
                    $"Complete Session {(session.Intraday ? "1" : "2")} closing data is not available yet."));
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

    async Task<SessionResolution> ResolveAvailablePreviousCloseAsync(
        SessionResolution session, CancellationToken ct)
    {
        if (!session.IsPreviousTradingDay)
            return session;
        try
        {
            var reference = await market.GetCandlesAsync(
                "BBCA", false, ct);
            var latest = reference
                .Where(x => x.Time.Date <= session.TradingDate.Date)
                .OrderBy(x => x.Time)
                .LastOrDefault();
            if (latest is not null &&
                latest.Time.Date < session.TradingDate.Date)
                return new SessionResolution(
                    latest.Time.Date, false, true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // The normal closing verification below will provide the visible
            // retry behavior when the reference request is unavailable.
        }
        return session;
    }

    readonly record struct SessionResolution(
        DateTime TradingDate, bool Intraday, bool IsPreviousTradingDay);

    public async Task<List<ScanResult>> AnalyzeAsync(
        bool intraday, MarketSnapshot snapshot, bool usedCache,
        IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        if (!snapshot.IsComplete)
            throw new InvalidOperationException(Loc.T(
                $"Snapshot masih parsial ({snapshot.CompletedCount}/{snapshot.RequestedCount}). Lanjutkan pengambilan data sebelum analisis.",
                $"The snapshot is still partial ({snapshot.CompletedCount}/{snapshot.RequestedCount}). Continue fetching data before analysis."));
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
        var existingRun = data.State.ScanHistory.LastOrDefault(x =>
            x.SessionKey == snapshot.SessionKey &&
            x.StrategyVersion == data.State.Strategy.Version);
        var preservedOutcomes = existingRun?.Predictions
            .ToDictionary(x => x.Symbol, StringComparer.OrdinalIgnoreCase);
        ApplyEventVeto(output);
        FreezeCancelledSignals(output, preservedOutcomes);
        AllocateAvailableCash(output);
        var allResults = output
            .OrderByDescending(x => x.AllocationRank > 0)
            .ThenBy(x => x.AllocationRank == 0 ? int.MaxValue : x.AllocationRank)
            .ThenByDescending(x => x.CombinedScore)
            .ThenByDescending(x => x.RiskReward)
            .ThenByDescending(x => x.LastPrice)
            .ToList();
        EvaluateHistory(snapshot.Symbols);
        data.ApplyMarketPrices(snapshot);
        // Keep all evaluated shares for audit, but only actionable BUY AREA
        // results are recommendations and prediction-performance observations.
        var ranked = allResults;
        data.State.LastScan = ranked;
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
        var refreshedPredictions = recommendations.Select(x =>
        {
            // Re-running the same session must not reset the prediction clock
            // or turn an already-filled/finished signal into a new prediction.
            if (preservedOutcomes?.TryGetValue(x.Symbol, out var prior) == true)
                return prior;
            return new PredictionRecord
            {
                Symbol = x.Symbol, Verdict = x.Verdict,
                Score = x.CombinedScore,
                StartPrice = x.EntryHigh,
                StopLoss = x.StopLoss,
                Target1 = x.Target1,
                PredictedAt = DateTime.Now,
                SignalDate = x.DataTime.Date,
                MaximumHoldingDays = x.MaximumHoldingDays,
                DataSession = x.DataSession
            };
        }).ToList();
        if (preservedOutcomes is not null)
        {
            var activeSymbols = refreshedPredictions
                .Select(x => x.Symbol)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var prior in preservedOutcomes.Values
                         .Where(x => !activeSymbols.Contains(x.Symbol)))
            {
                // A pre-opening event refresh can veto a recommendation made
                // at 07:00. Keep the audit record, but make it explicit that
                // the order must be cancelled before it can be filled.
                if (prior.Outcome == "PENDING" &&
                    !prior.EntryFilledAt.HasValue)
                {
                    prior.Outcome = "CANCELLED";
                    prior.EvaluatedAt = DateTime.Now;
                    prior.ReturnPercent = 0;
                }
                refreshedPredictions.Add(prior);
            }
        }
        run.Predictions = refreshedPredictions
            .OrderByDescending(x => x.PredictedAt)
            .ToList();
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

    static void FreezeCancelledSignals(
        IEnumerable<ScanResult> results,
        IReadOnlyDictionary<string, PredictionRecord>? preservedOutcomes)
    {
        if (preservedOutcomes is null) return;
        foreach (var result in results)
        {
            if (!preservedOutcomes.TryGetValue(
                    result.Symbol, out var prediction) ||
                prediction.Outcome != "CANCELLED")
                continue;

            // Once the pre-opening instruction says CANCEL, do not let a
            // later refresh on the same session silently reactivate it.
            result.Verdict = "WATCH";
            result.SuggestedLots = 0;
            result.ExecutionNote =
                "DIBATALKAN untuk sesi ini. Jangan aktifkan kembali atau mengejar harga.";
            result.ExecutionNoteEn =
                "CANCELLED for this session. Do not reactivate or chase the price.";
        }
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
                     .OrderByDescending(x => x.CombinedScore)
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
                item.ExecutionNoteEn = oneLotCash > data.State.Cash
                    ? $"At least 1 lot needs about Rp{oneLotCash:N0}; cash is Rp{data.State.Cash:N0}."
                    : "Cash has been allocated to a higher-priority candidate.";
                continue;
            }
            item.SuggestedLots = allocatedLots;
            item.AllocationRank = rank++;
            item.AllocatedCash = allocatedLots * oneLotCash;
            item.ExecutionNote = $"Prioritas alokasi #{item.AllocationRank} • estimasi dana Rp{item.AllocatedCash:N0} termasuk fee.";
            item.ExecutionNoteEn =
                $"Allocation priority #{item.AllocationRank} • estimated Rp{item.AllocatedCash:N0} including fees.";
            remaining -= item.AllocatedCash;
        }
        foreach (var item in results.Where(x => ownedSymbols.Contains(x.Symbol)))
        {
            item.AllocationRank = 0;
            item.AllocatedCash = 0;
            item.SuggestedLots = 0;
            item.ExecutionNote =
                "Saham sudah dimiliki; tindakan mengikuti keputusan Portofolio.";
            item.ExecutionNoteEn =
                "This stock is already owned; follow the Portfolio decision.";
        }
    }

    void ApplyEventVeto(IEnumerable<ScanResult> results)
    {
        var threshold = data.State.Strategy.BuyScore;
        foreach (var item in results)
        {
            if (!data.State.AutoEventIntelligence)
            {
                item.EventAdjustment = 0;
                item.CombinedScore = item.Score;
                continue;
            }
            var eventView = events.Summarize(item.Symbol);
            item.EventAdjustment = eventView.Adjustment;
            item.CombinedScore = Math.Clamp(
                item.Score + eventView.Adjustment, 0, 100);
            if (item.Verdict != "BUY AREA" ||
                item.CombinedScore >= threshold)
                continue;

            // Event data may veto a technical entry, but it never upgrades a
            // weak technical setup into a buy.
            item.Verdict = "WATCH";
            item.SuggestedLots = 0;
            item.ExecutionNote =
                $"Beli dibatalkan oleh veto isu: skor gabungan {item.CombinedScore}/100 di bawah batas {threshold}.";
            item.ExecutionNoteEn =
                $"Buy cancelled by the event veto: combined score {item.CombinedScore}/100 is below the {threshold} threshold.";
            item.Risks +=
                $", penyesuaian isu {item.EventAdjustment:+#;-#;0} membatalkan sinyal beli";
            item.RisksEn +=
                $", an event adjustment of {item.EventAdjustment:+#;-#;0} cancelled the buy signal";
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

    void EvaluateHistory(IReadOnlyCollection<SymbolMarketData> marketData)
    {
        var candlesBySymbol = marketData.ToDictionary(
            x => x.Symbol,
            x => ToDailyCandles(x.Candles),
            StringComparer.OrdinalIgnoreCase);
        var now = DateTime.Now;
        foreach (var run in data.State.ScanHistory)
        {
            foreach (var prediction in run.Predictions.Where(x => x.Outcome == "PENDING"))
            {
                if (!candlesBySymbol.TryGetValue(
                        prediction.Symbol, out var allCandles) ||
                    allCandles.Count == 0) continue;

                var signalDate = prediction.SignalDate == default
                    ? prediction.PredictedAt.Date
                    : prediction.SignalDate.Date;
                var future = allCandles
                    .Where(x => x.Time.Date > signalDate)
                    .OrderBy(x => x.Time)
                    .ToList();
                if (future.Count == 0) continue;

                if (!prediction.EntryFilledAt.HasValue)
                {
                    var entryCandle = future[0];
                    if (entryCandle.Low > prediction.StartPrice)
                    {
                        // Good-for-Day order expired without being filled.
                        prediction.EvaluatedAt = now;
                        prediction.EvaluationPrice = entryCandle.Close;
                        prediction.ReturnPercent = 0;
                        prediction.Outcome = "NOT_FILLED";
                        continue;
                    }
                    prediction.EntryFilledAt = entryCandle.Time;
                    prediction.FilledPrice = entryCandle.Open > 0
                        ? Math.Min(entryCandle.Open, prediction.StartPrice)
                        : prediction.StartPrice;
                }

                var entryDate = prediction.EntryFilledAt.Value.Date;
                var holdingCandles = allCandles
                    .Where(x => x.Time.Date >= entryDate)
                    .OrderBy(x => x.Time)
                    .Take(Math.Max(1, prediction.MaximumHoldingDays))
                    .ToList();
                if (holdingCandles.Count == 0) continue;

                decimal? exitPrice = null;
                string? outcome = null;
                foreach (var candle in holdingCandles)
                {
                    // When target and stop are both inside one daily candle,
                    // assume the stop was hit first. This avoids optimistic
                    // look-ahead from unknown intraday ordering.
                    if (candle.Low <= prediction.StopLoss)
                    {
                        // A stop cannot fill above a gap-down open. Using the
                        // displayed stop in that case would manufacture a
                        // better exit—and can even turn a bad gap into profit.
                        exitPrice = candle.Open > 0
                            ? Math.Min(candle.Open, prediction.StopLoss)
                            : prediction.StopLoss;
                        outcome = "STOP";
                        break;
                    }
                    if (candle.High >= prediction.Target1)
                    {
                        exitPrice = prediction.Target1;
                        outcome = "TARGET";
                        break;
                    }
                }

                if (outcome is null &&
                    holdingCandles.Count >=
                    Math.Max(1, prediction.MaximumHoldingDays))
                {
                    exitPrice = holdingCandles[^1].Close;
                    outcome = "TIME_EXIT";
                }
                if (outcome is null || !exitPrice.HasValue) continue;

                var filled = prediction.FilledPrice ??
                             prediction.StartPrice;
                prediction.EvaluatedAt = now;
                prediction.EvaluationPrice = exitPrice;
                var entryCost =
                    filled * (1m + data.State.BuyFeeRate);
                var exitProceeds =
                    exitPrice.Value *
                    (1m - data.State.SellFeeRate);
                prediction.ReturnPercent = entryCost <= 0
                    ? 0
                    : (exitProceeds / entryCost - 1) * 100;
                prediction.Outcome = outcome;
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
        var atr = Atr(c, 14);
        if (atr <= 0 || last.Close <= 0) return null;
        var daily = ToDailyCandles(c);
        var liquidWindow = daily.TakeLast(Math.Min(20, daily.Count)).ToList();
        var medianDailyValue = liquidWindow.Count == 0
            ? 0
            : Median(liquidWindow.Select(x => x.Close * x.Volume));
        var liquidEnough = medianDailyValue >= 2_000_000_000m;
        var priceEligible = last.Close >= 100m;
        var trendConfirmed = last.Close > sma20 && sma20 > sma50;
        // Continuous components deliberately avoid score piles such as 80/100.
        // The former implementation only used +15/+10 blocks, so many unrelated
        // shares received an identical rating.
        var scoreValue = 35m;
        var reasons = new List<string>();
        var reasonsEn = new List<string>();
        var risks = new List<string>();
        var risksEn = new List<string>();
        if (last.Close > sma20)
        {
            scoreValue += 10m + Math.Min(7m, (last.Close / sma20 - 1m) * 100m);
            reasons.Add("harga di atas rata-rata 20 periode");
            reasonsEn.Add("price is above the 20-period average");
        }
        else
        {
            risks.Add("harga masih di bawah rata-rata 20 periode");
            risksEn.Add("price remains below the 20-period average");
        }
        if (sma20 > sma50)
        {
            scoreValue += 9m + Math.Min(7m, (sma20 / sma50 - 1m) * 100m);
            reasons.Add("tren menengah naik");
            reasonsEn.Add("the medium-term trend is rising");
        }
        else
        {
            risks.Add("tren menengah belum naik");
            risksEn.Add("the medium-term trend is not rising yet");
        }
        var volumeRatio = avgVolume <= 0 ? 0 : last.Volume / avgVolume;
        if (volumeRatio > data.State.Strategy.VolumeConfirmation)
        {
            scoreValue += 8m + Math.Min(8m, (volumeRatio - data.State.Strategy.VolumeConfirmation) * 8m);
            reasons.Add("volume lebih kuat dari normal");
            reasonsEn.Add("volume is stronger than normal");
        }
        if (rsi is >= 48 and <= 68)
        {
            scoreValue += 6m + Math.Max(0m, 5m - Math.Abs(rsi - 58m) / 2m);
            reasons.Add("momentum sehat, belum terlalu panas");
            reasonsEn.Add("momentum is healthy and not overheated");
        }
        if (rsi > 72)
        {
            scoreValue -= 15m + Math.Min(8m, rsi - 72m);
            risks.Add("momentum sudah terlalu panas");
            risksEn.Add("momentum is already overheated");
        }
        if (!liquidEnough)
        {
            scoreValue -= 20m;
            risks.Add("median nilai transaksi harian belum mencapai Rp2 miliar");
            risksEn.Add("daily traded value is below Rp2 billion");
        }
        if (!priceEligible)
        {
            scoreValue -= 15m;
            risks.Add("harga di bawah Rp100 tidak eligible untuk rekomendasi beli");
            risksEn.Add("stocks below Rp100 are not eligible for a buy recommendation");
        }
        if (speculative)
        {
            scoreValue -= 8m;
            risks.Add("kategori spekulatif: ukuran posisi wajib kecil");
            risksEn.Add("speculative category: position size must stay small");
        }

        // A single GFD limit is produced. The buffer is capped at 1% so the
        // recommendation never turns into a wide range or an invitation to
        // chase price.
        var entryBuffer = Math.Min(atr * .25m, last.Close * .01m);
        var entry = RoundDownPrice(last.Close + entryBuffer);
        var stop = RoundUpPrice(
            entry - atr * data.State.Strategy.AtrStopMultiplier);
        if (stop >= entry)
            stop = PreviousPriceStep(entry);
        var entryCostPerShare =
            entry * (1m + data.State.BuyFeeRate);
        var stopProceedsPerShare =
            stop * (1m - data.State.SellFeeRate);
        var netRiskPerShare =
            entryCostPerShare - stopProceedsPerShare;
        if (netRiskPerShare <= 0) return null;
        var minimumRiskReward = Math.Max(data.State.MinRiskReward, data.State.Strategy.MinimumRiskReward);
        var target1 = RoundUpPrice(
            (entryCostPerShare +
             netRiskPerShare * minimumRiskReward) /
            Math.Max(.0001m, 1m - data.State.SellFeeRate));
        var secondaryRiskReward =
            Math.Max(3m, minimumRiskReward + 1m);
        var target2 = RoundUpPrice(
            (entryCostPerShare +
             netRiskPerShare * secondaryRiskReward) /
            Math.Max(.0001m, 1m - data.State.SellFeeRate));
        var actualRewardPerShare =
            target1 * (1m - data.State.SellFeeRate) -
            entryCostPerShare;
        var actualRiskReward =
            actualRewardPerShare / netRiskPerShare;
        var lotsByRisk = (int)Math.Floor(
            data.State.RiskPerTrade /
            (netRiskPerShare * 100m));
        var maxValue = speculative ? data.State.Strategy.MaximumSpeculativePosition : data.State.Strategy.MaximumNormalPosition;
        var oneLotCash = entry * 100m * (1m + data.State.BuyFeeRate);
        var lotsByValue = oneLotCash <= 0
            ? 0
            : (int)Math.Floor(maxValue / oneLotCash);
        var lots = Math.Max(0, Math.Min(lotsByRisk, lotsByValue));
        var score = Math.Clamp((int)Math.Round(scoreValue), 0, 100);
        var age = DateTime.Now - last.Time;
        var freshEnough = !intraday || age <= TimeSpan.FromMinutes(60);
        if (intraday && age > TimeSpan.FromMinutes(60))
        {
            risks.Add("snapshot intraday sudah lama; jangan entry tanpa cek harga berjalan di Stockbit");
            risksEn.Add("the intraday snapshot is stale; do not enter without checking Stockbit");
        }
        var eligible = trendConfirmed && liquidEnough &&
                       priceEligible && freshEnough;
        return new ScanResult
        {
            Symbol = symbol,
            Verdict = eligible &&
                      score >= data.State.Strategy.BuyScore && lots > 0
                ? "BUY AREA"
                : eligible && score >= data.State.Strategy.BuyScore
                    ? "PANTAU — LOT 0"
                    : score >= data.State.Strategy.WatchScore ? "WATCH" : "WAIT",
            Score = score,
            CombinedScore = score,
            LastPrice = RoundPrice(last.Close),
            // One executable limit price, not a range. Keeping both legacy
            // fields equal preserves existing saved-data compatibility.
            EntryLow = entry,
            EntryHigh = entry,
            MaxBuyPrice = entry,
            StopLoss = stop,
            Target1 = target1,
            Target2 = target2,
            SuggestedLots = lots,
            RiskReward = actualRiskReward,
            DataTime = last.Time,
            DataSession = intraday ? "Closing Sesi 1" : "Closing Sesi 2",
            Reasons = reasons.Count == 0 ? "belum ada konfirmasi kuat" : string.Join(", ", reasons),
            ReasonsEn = reasonsEn.Count == 0
                ? "there is no strong confirmation yet"
                : string.Join(", ", reasonsEn),
            Risks = risks.Count == 0 ? "tetap konfirmasi harga dan kondisi IHSG di Stockbit" : string.Join(", ", risks),
            RisksEn = risksEn.Count == 0
                ? "always confirm price and IHSG conditions in Stockbit"
                : string.Join(", ", risksEn),
            IsSpeculative = speculative,
            MaximumHoldingDays = 20
        };
    }

    static decimal Rsi(decimal[] values, int period)
    {
        if (values.Length < period + 1) return 50;
        var window = values.TakeLast(period + 1).ToArray();
        decimal gain = 0, loss = 0;
        for (var i = 1; i < window.Length; i++)
        {
            // The previous implementation subtracted current from previous,
            // reversing every gain/loss and therefore the RSI signal.
            var d = window[i] - window[i - 1];
            if (d > 0) gain += d; else loss -= d;
        }
        if (gain == 0 && loss == 0) return 50;
        if (loss == 0) return 100;
        var rs = gain / loss;
        return 100 - 100 / (1 + rs);
    }

    static decimal Atr(IReadOnlyList<Candle> candles, int period)
    {
        if (candles.Count < 2) return 0;
        var start = Math.Max(1, candles.Count - period);
        decimal total = 0;
        var count = 0;
        for (var i = start; i < candles.Count; i++)
        {
            var candle = candles[i];
            var previousClose = candles[i - 1].Close;
            var trueRange = new[]
            {
                Math.Abs(candle.High - candle.Low),
                Math.Abs(candle.High - previousClose),
                Math.Abs(candle.Low - previousClose)
            }.Max();
            total += trueRange;
            count++;
        }
        return count == 0 ? 0 : total / count;
    }

    static decimal Median(IEnumerable<decimal> values)
    {
        var ordered = values.OrderBy(x => x).ToArray();
        if (ordered.Length == 0) return 0;
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 1
            ? ordered[middle]
            : (ordered[middle - 1] + ordered[middle]) / 2m;
    }

    static List<Candle> ToDailyCandles(IEnumerable<Candle> candles) =>
        candles
            .Where(x => x.Close > 0)
            .OrderBy(x => x.Time)
            .GroupBy(x => x.Time.Date)
            .Select(group =>
            {
                var ordered = group.OrderBy(x => x.Time).ToList();
                return new Candle
                {
                    Time = group.Key,
                    Open = ordered.First().Open,
                    High = ordered.Max(x => x.High),
                    Low = ordered.Min(x => x.Low),
                    Close = ordered.Last().Close,
                    Volume = ordered.Sum(x => x.Volume)
                };
            })
            .ToList();

    static decimal RoundPrice(decimal value)
    {
        var step = PriceStep(value);
        return Math.Max(step, Math.Round(value / step) * step);
    }

    static decimal RoundDownPrice(decimal value)
    {
        var step = PriceStep(value);
        return Math.Max(step, Math.Floor(value / step) * step);
    }

    static decimal RoundUpPrice(decimal value)
    {
        var step = PriceStep(value);
        return Math.Max(step, Math.Ceiling(value / step) * step);
    }

    static decimal PreviousPriceStep(decimal value)
    {
        // At a band boundary the previous valid price uses the lower band's
        // fraction: 200→199, 500→498, 2,000→1,995, 5,000→4,990.
        var probe = Math.Max(1m, value - .0001m);
        var step = PriceStep(probe);
        return Math.Max(
            step, Math.Floor(probe / step) * step);
    }

    static decimal PriceStep(decimal value) =>
        value < 200 ? 1 :
        value < 500 ? 2 :
        value < 2000 ? 5 :
        value < 5000 ? 10 : 25;
}

public sealed class ClosingDataNotReadyException(string message)
    : Exception(message)
{
}
