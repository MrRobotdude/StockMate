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
        var universeResult = await universe.EnsureCurrentAsync(
            force: !universe.HasFullUniverse,
            ct: ct,
            progress: progress);
        if (!universe.HasFullUniverse)
        {
            progress?.Report(new()
            {
                Stage = "UNIVERSE_REQUEST",
                Message = $"Master universe belum lengkap ({data.State.MarketUniverse.Count} tersimpan); sinkronisasi IDX",
                Total = data.State.MarketUniverse.Count,
                Source = "IDX Listed Company",
                TechnicalDetail = "Universe di bawah 500 kode tidak akan dipakai sebagai full universe"
            });
            throw new InvalidOperationException(Loc.T(
                $"Master universe belum lengkap ({data.State.MarketUniverse.Count} saham tersimpan). " +
                "Pembaruan online IDX gagal. StockMate tidak menjalankan scan parsial dan akan mencoba lagi otomatis.",
                $"The master universe is incomplete ({data.State.MarketUniverse.Count} saved stocks). " +
                "The online IDX refresh failed. StockMate will not run a partial scan and will retry automatically."));
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
        progress?.Report(new()
        {
            Stage = "UNIVERSE_READY",
            Message = $"{universeResult.Message} Universe aktif: {universeResult.Count} saham",
            Total = universeResult.Count,
            Source = universe.SourceLabel,
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
            cached.RequestedCount == knownTotal && !force)
        {
            var changed = false;
            if (cached.MarketIndex is null)
                changed = await PopulateMarketIndexAsync(
                    cached, session.Intraday, session.TradingDate,
                    progress, ct);
            if (cached.Session == "EVENING" &&
                EvaluateHistory(cached.Symbols))
                changed = true;
            if (data.ApplyMarketPrices(cached))
                changed = true;
            if (changed)
                await data.SaveAsync();
            return cached;
        }

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
                        data.ApplyMarketPrice(
                            symbol, candles[^1].Close,
                            candles[^1].Time, notify: false);
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
            // One silent checkpoint per 100-stock batch retains resume support
            // without repeatedly rebuilding every visible page.
            await data.SaveCheckpointAsync();
        }

        await PopulateMarketIndexAsync(
            snapshot, session.Intraday, session.TradingDate,
            progress, ct);
        snapshot.IsComplete = snapshot.CompletedCount >= snapshot.RequestedCount;
        snapshot.Status = snapshot.IsComplete
            ? (snapshot.FailedSymbols.Count == 0 ? "COMPLETE" : "COMPLETE_WITH_ERRORS")
            : "PARTIAL";
        if (snapshot.IsComplete &&
            snapshot.Session == "EVENING")
            EvaluateHistory(snapshot.Symbols);
        // A finalized snapshot already carries the rolling price history used
        // by evaluation and analysis. Older snapshots duplicate almost all of
        // those candles, so keeping them inflated app-state JSON and slowed
        // every later save. ScanHistory retains the durable prediction audit.
        data.State.MarketSnapshots = [snapshot];
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

    async Task<bool> PopulateMarketIndexAsync(
        MarketSnapshot snapshot, bool intraday, DateTime tradingDate,
        IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        try
        {
            progress?.Report(new()
            {
                Stage = "MARKET_REGIME",
                Message = Loc.T(
                    "Mengambil IHSG untuk konteks rezim pasar",
                    "Fetching the JCI for market-regime context"),
                Source = "Yahoo Finance ^JKSE",
                Completed = snapshot.CompletedCount,
                Total = snapshot.RequestedCount
            });
            var candles = await GetWithRetryAsync(
                "^JKSE", intraday, progress, ct);
            candles = candles
                .Where(x => x.Time.Date <= tradingDate.Date)
                .OrderBy(x => x.Time)
                .ToList();
            if (candles.Count == 0) return false;
            snapshot.MarketIndex = new SymbolMarketData
            {
                Symbol = "^JKSE",
                Candles = candles
            };
            return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // A missing index makes the regime UNKNOWN; it must not discard an
            // otherwise valid full-market snapshot.
            progress?.Report(new()
            {
                Stage = "MARKET_REGIME_FALLBACK",
                Message = Loc.T(
                    "IHSG belum tersedia; rezim pasar ditandai UNKNOWN",
                    "JCI is unavailable; market regime is marked UNKNOWN"),
                Source = "Yahoo Finance ^JKSE",
                TechnicalDetail = $"{ex.GetType().Name}: {ex.Message}"
            });
            return false;
        }
    }

    readonly record struct MarketContext(
        decimal Return20Percent,
        decimal Breadth20Percent,
        bool IndexAboveMa20,
        string Regime);

    static MarketContext BuildMarketContext(MarketSnapshot snapshot)
    {
        var breadthEligible = snapshot.Symbols
            .Select(x => ToDailyCandles(x.Candles))
            .Where(x => x.Count >= 20)
            .ToList();
        var breadth = breadthEligible.Count == 0
            ? 0m
            : breadthEligible.Count(x =>
                x[^1].Close > x.TakeLast(20).Average(y => y.Close)) *
              100m / breadthEligible.Count;

        var indexBars = snapshot.MarketIndex is null
            ? new List<Candle>()
            : ToDailyCandles(snapshot.MarketIndex.Candles);
        if (indexBars.Count < 21)
            return new MarketContext(0, breadth, false, "UNKNOWN");
        var indexClose = indexBars[^1].Close;
        var indexReturn20 = indexBars[^21].Close <= 0
            ? 0
            : (indexClose / indexBars[^21].Close - 1m) * 100m;
        var indexMa20 = indexBars.TakeLast(20).Average(x => x.Close);
        var indexAboveMa20 = indexClose >= indexMa20;
        var regime = indexReturn20 <= -4m ||
                     (!indexAboveMa20 && breadth < 45m)
            ? "RISK_OFF"
            : indexReturn20 > 0m &&
              indexAboveMa20 && breadth >= 55m
                ? "RISK_ON"
                : "NEUTRAL";
        return new MarketContext(
            indexReturn20, breadth, indexAboveMa20, regime);
    }

    public async Task<List<ScanResult>> AnalyzeAsync(
        bool intraday, MarketSnapshot snapshot, bool usedCache,
        IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        if (!snapshot.IsComplete)
            throw new InvalidOperationException(Loc.T(
                $"Snapshot masih parsial ({snapshot.CompletedCount}/{snapshot.RequestedCount}). Lanjutkan pengambilan data sebelum analisis.",
                $"The snapshot is still partial ({snapshot.CompletedCount}/{snapshot.RequestedCount}). Continue fetching data before analysis."));
        var output = new List<ScanResult>();
        var marketContext = BuildMarketContext(snapshot);
        var completed = 0;
        foreach (var item in snapshot.Symbols)
        {
            ct.ThrowIfCancellationRequested();
            var result = Evaluate(
                item.Symbol, item.Candles, intraday,
                item.IsSpeculative, marketContext);
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
        if (snapshot.Session == "EVENING")
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
            {
                EnrichPrediction(prior, x, snapshot.SessionKey);
                return prior;
            }
            return new PredictionRecord
            {
                SessionKey = snapshot.SessionKey,
                Symbol = x.Symbol, Verdict = x.Verdict,
                Score = x.CombinedScore,
                TechnicalScore = x.Score,
                EventAdjustment = x.EventAdjustment,
                SuggestedLots = x.SuggestedLots,
                StartPrice = x.EntryHigh,
                StopLoss = x.StopLoss,
                Target1 = x.Target1,
                RiskReward = x.RiskReward,
                SignalOpen = x.SignalOpen,
                SignalHigh = x.SignalHigh,
                SignalLow = x.SignalLow,
                SignalClose = x.SignalClose,
                Reasons = x.Reasons,
                ReasonsEn = x.ReasonsEn,
                Risks = x.Risks,
                RisksEn = x.RisksEn,
                EventSummary = x.EventSummary,
                EventSummaryEn = x.EventSummaryEn,
                PrimarySetup = x.PrimarySetup,
                PrimarySetupEn = x.PrimarySetupEn,
                ResearchStatus = x.ResearchStatus,
                MarketReturn20Percent = x.MarketReturn20Percent,
                MarketBreadth20Percent = x.MarketBreadth20Percent,
                MarketRegime = x.MarketRegime,
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
                var current = ranked.FirstOrDefault(x =>
                    x.Symbol.Equals(
                        prior.Symbol,
                        StringComparison.OrdinalIgnoreCase));
                if (current is not null)
                    EnrichPrediction(
                        prior,
                        current,
                        snapshot.SessionKey,
                        preserveExecutionPlan: true);
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

    static void EnrichPrediction(
        PredictionRecord prediction, ScanResult result, string sessionKey,
        bool preserveExecutionPlan = false)
    {
        prediction.SessionKey = string.IsNullOrWhiteSpace(
            prediction.SessionKey) ? sessionKey : prediction.SessionKey;
        prediction.TechnicalScore = result.Score;
        prediction.EventAdjustment = result.EventAdjustment;
        prediction.Score = result.CombinedScore;
        if (!preserveExecutionPlan)
        {
            prediction.SuggestedLots = result.SuggestedLots;
            prediction.RiskReward = result.RiskReward;
        }
        prediction.SignalOpen = result.SignalOpen;
        prediction.SignalHigh = result.SignalHigh;
        prediction.SignalLow = result.SignalLow;
        prediction.SignalClose = result.SignalClose;
        prediction.Reasons = result.Reasons;
        prediction.ReasonsEn = result.ReasonsEn;
        prediction.Risks = result.Risks;
        prediction.RisksEn = result.RisksEn;
        prediction.EventSummary = result.EventSummary;
        prediction.EventSummaryEn = result.EventSummaryEn;
        prediction.PrimarySetup = result.PrimarySetup;
        prediction.PrimarySetupEn = result.PrimarySetupEn;
        prediction.ResearchStatus = result.ResearchStatus;
        prediction.MarketReturn20Percent = result.MarketReturn20Percent;
        prediction.MarketBreadth20Percent = result.MarketBreadth20Percent;
        prediction.MarketRegime = result.MarketRegime;
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
            var eventView = events.SummarizeDetails(item.Symbol);
            item.EventAdjustment = eventView.Adjustment;
            item.EventSummary = eventView.SummaryId;
            item.EventSummaryEn = eventView.SummaryEn;
            item.CombinedScore = Math.Clamp(
                item.Score + eventView.Adjustment, 0, 100);
            if (item.Verdict != "BUY AREA" ||
                (item.CombinedScore >= threshold &&
                 item.EventAdjustment > -20))
                continue;

            // Event data may veto a technical entry, but it never upgrades a
            // weak technical setup into a buy.
            item.Verdict = "WATCH";
            item.SuggestedLots = 0;
            item.ExecutionNote =
                item.EventAdjustment <= -20
                    ? "Beli dibatalkan: corporate action/peristiwa material berisiko tinggi perlu ditelaah sebelum entry."
                    : $"Beli dibatalkan oleh veto isu: skor gabungan {item.CombinedScore}/100 di bawah batas {threshold}.";
            item.ExecutionNoteEn =
                item.EventAdjustment <= -20
                    ? "Buy cancelled: a material corporate action/high-risk event must be reviewed before entry."
                    : $"Buy cancelled by the event veto: combined score {item.CombinedScore}/100 is below the {threshold} threshold.";
            item.Risks +=
                $"\n• Penyesuaian isu {item.EventAdjustment:+#;-#;0} membatalkan sinyal beli.";
            item.RisksEn +=
                $"\n• An event adjustment of {item.EventAdjustment:+#;-#;0} cancelled the buy signal.";
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

    public async Task UpdatePredictionHistoryAsync()
    {
        var snapshot = data.State.MarketSnapshots
            .Where(x => x.IsComplete &&
                        x.Session == "EVENING" &&
                        x.Symbols.Count > 0)
            .OrderByDescending(x => x.TradingDate)
            .ThenByDescending(x => x.Session == "EVENING")
            .ThenByDescending(x => x.CapturedAt)
            .FirstOrDefault();
        if (snapshot is null) return;
        if (EvaluateHistory(snapshot.Symbols))
            await data.SaveAsync();
    }

    bool EvaluateHistory(IReadOnlyCollection<SymbolMarketData> marketData)
    {
        var candlesBySymbol = marketData
            .GroupBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => ToDailyCandles(x.Last().Candles),
                StringComparer.OrdinalIgnoreCase);
        var now = DateTime.Now;
        var changed = false;
        foreach (var run in data.State.ScanHistory)
        {
            foreach (var prediction in run.Predictions)
            {
                if (string.IsNullOrWhiteSpace(prediction.SessionKey))
                {
                    prediction.SessionKey = run.SessionKey;
                    changed = true;
                }
                if (prediction.Id == Guid.Empty)
                {
                    prediction.Id = Guid.NewGuid();
                    changed = true;
                }
                if (prediction.EvaluationVersion < 2)
                {
                    ResetLegacyEvaluation(prediction);
                    changed = true;
                }
                if (!candlesBySymbol.TryGetValue(
                        prediction.Symbol, out var allCandles) ||
                    allCandles.Count == 0) continue;

                var signalDate = prediction.SignalDate == default
                    ? prediction.PredictedAt.Date
                    : prediction.SignalDate.Date;
                if (prediction.SignalDate == default)
                {
                    prediction.SignalDate = signalDate;
                    changed = true;
                }
                var signalCandle = allCandles
                    .Where(x => x.Time.Date <= signalDate)
                    .OrderBy(x => x.Time)
                    .LastOrDefault();
                if (signalCandle is not null && prediction.SignalClose <= 0)
                {
                    prediction.SignalOpen = signalCandle.Open;
                    prediction.SignalHigh = signalCandle.High;
                    prediction.SignalLow = signalCandle.Low;
                    prediction.SignalClose = signalCandle.Close;
                    changed = true;
                }

                var future = allCandles
                    .Where(x => x.Time.Date > signalDate)
                    .OrderBy(x => x.Time)
                    .ToList();
                if (future.Count == 0) continue;

                var next = future[0];
                if (!prediction.NextTradingDate.HasValue)
                {
                    CaptureNextTradingDay(prediction, next);
                    changed = true;
                }

                if (prediction.Outcome == "CANCELLED")
                {
                    if (prediction.NextDayStatus != "CANCELLED")
                    {
                        prediction.NextDayStatus = "CANCELLED";
                        prediction.NextDayNoteCode = "CANCELLED_BEFORE_OPEN";
                        changed = true;
                    }
                    continue;
                }

                if (prediction.Outcome == "NOT_FILLED")
                    continue;

                if (!prediction.EntryFilledAt.HasValue)
                {
                    // The instruction shown by StockMate is one exact GFD limit
                    // and "cancel if opening is above it". The old evaluator
                    // incorrectly counted a later intraday pullback as filled.
                    if (next.Open > 0 &&
                        next.Open > prediction.StartPrice)
                    {
                        MarkNotFilled(
                            prediction, next, now, "OPEN_ABOVE_LIMIT");
                        changed = true;
                        continue;
                    }
                    if (next.Open > 0 &&
                        next.Open <= prediction.StopLoss)
                    {
                        MarkNotFilled(
                            prediction, next, now, "OPEN_BELOW_STOP");
                        changed = true;
                        continue;
                    }
                    if (next.Open <= 0 &&
                        next.Low > prediction.StartPrice)
                    {
                        MarkNotFilled(
                            prediction, next, now, "LIMIT_NOT_TOUCHED");
                        changed = true;
                        continue;
                    }

                    prediction.EntryFilledAt = next.Time;
                    prediction.FilledPrice = next.Open > 0
                        ? Math.Min(next.Open, prediction.StartPrice)
                        : prediction.StartPrice;
                    prediction.NextDayNoteCode = next.Open > 0 &&
                                                 next.Open < prediction.StartPrice
                        ? "FILLED_BELOW_LIMIT"
                        : "FILLED_AT_LIMIT";
                    changed = true;
                }

                var filled = prediction.FilledPrice ??
                             prediction.StartPrice;
                if (prediction.NextDayStatus == "WAITING")
                {
                    var (dayOutcome, dayExit) =
                        ResolveExit(prediction, next);
                    prediction.NextDayStatus = dayOutcome ?? "OPEN";
                    prediction.NextDayReturnPercent = NetReturnPercent(
                        filled, dayExit ?? next.Close);
                    prediction.NextDayMaximumGainPercent =
                        NetReturnPercent(filled, next.High);
                    prediction.NextDayMaximumLossPercent =
                        NetReturnPercent(filled, next.Low);
                    changed = true;

                    if (dayOutcome is not null && dayExit.HasValue)
                    {
                        CompletePrediction(
                            prediction, dayOutcome, dayExit.Value, now);
                        changed = true;
                    }
                }

                var entryDate = prediction.EntryFilledAt.Value.Date;
                var holdingCandles = allCandles
                    .Where(x => x.Time.Date >= entryDate)
                    .OrderBy(x => x.Time)
                    .Take(Math.Max(1, prediction.MaximumHoldingDays))
                    .ToList();
                if (holdingCandles.Count == 0) continue;

                if (prediction.Outcome == "PENDING")
                {
                    decimal? exitPrice = null;
                    string? outcome = null;
                    foreach (var candle in holdingCandles)
                    {
                        (outcome, exitPrice) =
                            ResolveExit(prediction, candle);
                        if (outcome is not null) break;
                    }
                    if (outcome is null &&
                        holdingCandles.Count >=
                        Math.Max(1, prediction.MaximumHoldingDays))
                    {
                        exitPrice = holdingCandles[^1].Close;
                        outcome = "TIME_EXIT";
                    }
                    if (outcome is not null && exitPrice.HasValue)
                    {
                        CompletePrediction(
                            prediction, outcome, exitPrice.Value, now);
                        changed = true;
                    }
                }

                var latest = holdingCandles[^1];
                var latestReturn = prediction.Outcome == "PENDING"
                    ? NetReturnPercent(filled, latest.Close)
                    : prediction.ReturnPercent;
                if (prediction.LatestObservedDate?.Date != latest.Time.Date ||
                    prediction.LatestObservedClose != latest.Close ||
                    prediction.LatestOpenReturnPercent != latestReturn)
                {
                    prediction.LatestObservedDate = latest.Time;
                    prediction.LatestObservedClose = latest.Close;
                    prediction.LatestOpenReturnPercent = latestReturn;
                    changed = true;
                }
            }
        }
        return changed;
    }

    static void ResetLegacyEvaluation(PredictionRecord prediction)
    {
        var cancelled = prediction.Outcome == "CANCELLED";
        prediction.EntryFilledAt = null;
        prediction.FilledPrice = null;
        prediction.NextTradingDate = null;
        prediction.NextOpen = null;
        prediction.NextHigh = null;
        prediction.NextLow = null;
        prediction.NextClose = null;
        prediction.NextDayStatus = "WAITING";
        prediction.NextDayNoteCode = "";
        prediction.NextDayReturnPercent = null;
        prediction.NextDayMarketReturnPercent = null;
        prediction.NextDayMaximumGainPercent = null;
        prediction.NextDayMaximumLossPercent = null;
        prediction.LatestObservedDate = null;
        prediction.LatestObservedClose = null;
        prediction.LatestOpenReturnPercent = null;
        prediction.EvaluatedAt = cancelled
            ? prediction.EvaluatedAt
            : null;
        prediction.EvaluationPrice = null;
        prediction.ReturnPercent = cancelled ? 0 : null;
        prediction.Outcome = cancelled ? "CANCELLED" : "PENDING";
        prediction.EvaluationVersion = 2;
    }

    static void CaptureNextTradingDay(
        PredictionRecord prediction, Candle candle)
    {
        prediction.NextTradingDate = candle.Time;
        prediction.NextOpen = candle.Open;
        prediction.NextHigh = candle.High;
        prediction.NextLow = candle.Low;
        prediction.NextClose = candle.Close;
        prediction.NextDayMarketReturnPercent =
            prediction.SignalClose <= 0
                ? null
                : (candle.Close / prediction.SignalClose - 1m) * 100m;
    }

    static void MarkNotFilled(
        PredictionRecord prediction, Candle candle, DateTime evaluatedAt,
        string noteCode)
    {
        prediction.NextDayStatus = "NOT_FILLED";
        prediction.NextDayNoteCode = noteCode;
        prediction.EvaluatedAt = evaluatedAt;
        prediction.EvaluationPrice = candle.Close;
        prediction.ReturnPercent = null;
        prediction.Outcome = "NOT_FILLED";
    }

    static (string? Outcome, decimal? ExitPrice) ResolveExit(
        PredictionRecord prediction, Candle candle)
    {
        // Daily bars do not reveal which level was touched first. Assuming the
        // stop first prevents an optimistic evaluation when both are inside
        // the same candle.
        if (candle.Low <= prediction.StopLoss)
            return ("STOP", candle.Open > 0
                ? Math.Min(candle.Open, prediction.StopLoss)
                : prediction.StopLoss);
        if (candle.High >= prediction.Target1)
            return ("TARGET", prediction.Target1);
        return (null, null);
    }

    void CompletePrediction(
        PredictionRecord prediction, string outcome, decimal exitPrice,
        DateTime evaluatedAt)
    {
        var filled = prediction.FilledPrice ??
                     prediction.StartPrice;
        prediction.EvaluatedAt = evaluatedAt;
        prediction.EvaluationPrice = exitPrice;
        prediction.ReturnPercent = NetReturnPercent(filled, exitPrice);
        prediction.Outcome = outcome;
    }

    decimal NetReturnPercent(decimal entryPrice, decimal exitPrice)
    {
        var entryCost = entryPrice * (1m + data.State.BuyFeeRate);
        var exitProceeds = exitPrice * (1m - data.State.SellFeeRate);
        return entryCost <= 0
            ? 0
            : (exitProceeds / entryCost - 1m) * 100m;
    }

    ScanResult? Evaluate(
        string symbol, List<Candle> c, bool intraday,
        bool speculative, MarketContext marketContext)
    {
        var bars = c
            .Where(x => x.Close > 0 && x.High > 0 && x.Low > 0)
            .OrderBy(x => x.Time)
            .ToList();
        const int minimumBars = 55;
        if (bars.Count < minimumBars) return null;
        var last = bars[^1];
        var closes = bars.Select(x => x.Close).ToArray();
        var sma20 = closes.TakeLast(20).Average();
        var sma50 = closes.TakeLast(50).Average();
        var earlierSma20 = closes
            .Take(closes.Length - 5)
            .TakeLast(20)
            .Average();
        var sma20SlopePercent = earlierSma20 <= 0
            ? 0
            : (sma20 / earlierSma20 - 1m) * 100m;
        var medianVolume = Median(
            bars.TakeLast(20).Select(x => (decimal)x.Volume));
        var rsi = Rsi(closes, 14);
        var atr = Atr(bars, 14);
        if (atr <= 0 || last.Close <= 0) return null;
        var daily = ToDailyCandles(bars);
        var signalCandle = daily.LastOrDefault() ?? last;
        var liquidWindow = daily.TakeLast(Math.Min(20, daily.Count)).ToList();
        var medianDailyValue = liquidWindow.Count == 0
            ? 0
            : Median(liquidWindow.Select(x => x.Close * x.Volume));
        var liquidEnough = medianDailyValue >= 2_000_000_000m;
        var priceEligible = last.Close >= 100m;
        var distanceFromSma20 = (last.Close / sma20 - 1m) * 100m;
        var maSpread = (sma20 / sma50 - 1m) * 100m;
        var return5 = (last.Close / closes[^6] - 1m) * 100m;
        var return20 = (last.Close / closes[^21] - 1m) * 100m;
        var volumeRatio = medianVolume <= 0
            ? 0
            : last.Volume / medianVolume;
        var priorHigh20 = bars
            .SkipLast(1)
            .TakeLast(20)
            .Max(x => x.High);
        var distanceFromHigh20 = priorHigh20 <= 0
            ? 0
            : (last.Close / priorHigh20 - 1m) * 100m;
        var candleRange = last.High - last.Low;
        var closeStrength = candleRange <= 0
            ? .5m
            : Math.Clamp(
                (last.Close - last.Low) / candleRange, 0m, 1m);
        var atrPercent = atr / last.Close * 100m;

        // A recommendation now needs independent evidence. Merely being above
        // MA20 and MA50 can no longer reach the BUY threshold on its own.
        var scoreValue = 20m;
        var evidence =
            new List<(decimal Strength, string Id, string En)>();
        var risks = new List<string>();
        var risksEn = new List<string>();
        var timeframeId = intraday
            ? "candle 15-menit"
            : "hari";
        var timeframeEn = intraday
            ? "15-minute bars"
            : "days";
        var fivePeriodsEn = intraday
            ? "five 15-minute bars"
            : "five days";
        var twentyPeriodEn = intraday
            ? "20-bar"
            : "20-day";

        if (marketContext.Regime == "RISK_ON")
            evidence.Add((
                36m,
                $"Rezim pasar RISK-ON: IHSG 20 hari {marketContext.Return20Percent:+0.0;-0.0;0.0}% dan {marketContext.Breadth20Percent:0}% saham berada di atas MA20.",
                $"The market is RISK-ON: the JCI is {marketContext.Return20Percent:+0.0;-0.0;0.0}% over 20 days and {marketContext.Breadth20Percent:0}% of stocks are above MA20."));
        else if (marketContext.Regime == "RISK_OFF")
        {
            risks.Add(
                $"rezim pasar RISK-OFF: IHSG 20 hari {marketContext.Return20Percent:+0.0;-0.0;0.0}% dan breadth MA20 {marketContext.Breadth20Percent:0}%; rekomendasi BUY baru diblokir");
            risksEn.Add(
                $"the market is RISK-OFF: the JCI is {marketContext.Return20Percent:+0.0;-0.0;0.0}% over 20 days with {marketContext.Breadth20Percent:0}% MA20 breadth; new BUY recommendations are blocked");
        }

        var aboveSma20 = distanceFromSma20 > 0;
        var notExtended = distanceFromSma20 <= 7m;
        if (aboveSma20 && notExtended)
        {
            scoreValue += 12m + Math.Min(4m, distanceFromSma20 * .8m);
            evidence.Add((
                12m + distanceFromSma20,
                $"Harga Rp{last.Close:N0} berada {distanceFromSma20:0.0}% di atas MA20 {timeframeId} Rp{sma20:N0}; tren pendek positif tanpa jarak yang berlebihan.",
                $"Rp{last.Close:N0} is {distanceFromSma20:0.0}% above the {timeframeEn} MA20 of Rp{sma20:N0}; the short trend is positive without excessive extension."));
        }
        else if (aboveSma20)
        {
            scoreValue -= Math.Min(18m, 8m + distanceFromSma20 - 7m);
            risks.Add(
                $"harga sudah {distanceFromSma20:0.0}% di atas MA20 {timeframeId}; entry terlambat dan rawan pullback");
            risksEn.Add(
                $"price is already {distanceFromSma20:0.0}% above the {timeframeEn} MA20; the entry is late and vulnerable to a pullback");
        }
        else
        {
            risks.Add(
                $"harga {Math.Abs(distanceFromSma20):0.0}% di bawah MA20 {timeframeId} Rp{sma20:N0}; tren pendek belum mendukung buy");
            risksEn.Add(
                $"price is {Math.Abs(distanceFromSma20):0.0}% below the {timeframeEn} MA20 of Rp{sma20:N0}; the short trend does not support a buy");
        }

        var averagesAligned = maSpread > 0;
        if (averagesAligned)
        {
            scoreValue += 10m + Math.Min(4m, maSpread);
            evidence.Add((
                18m + maSpread,
                $"MA20 Rp{sma20:N0} berada {maSpread:0.0}% di atas MA50 Rp{sma50:N0}; tren pendek dan menengah searah.",
                $"MA20 at Rp{sma20:N0} is {maSpread:0.0}% above MA50 at Rp{sma50:N0}; short- and medium-term trends are aligned."));
        }
        else
        {
            risks.Add(
                $"MA20 masih {Math.Abs(maSpread):0.0}% di bawah MA50; tren menengah belum berbalik naik");
            risksEn.Add(
                $"MA20 remains {Math.Abs(maSpread):0.0}% below MA50; the medium-term trend has not turned up");
        }

        var averageRising = sma20SlopePercent > .05m;
        if (averageRising)
        {
            scoreValue += 8m + Math.Min(4m, sma20SlopePercent * 2m);
            evidence.Add((
                20m + sma20SlopePercent,
                $"MA20 naik {sma20SlopePercent:0.0}% dibanding 5 {timeframeId} lalu; arah rata-rata benar-benar menanjak.",
                $"MA20 rose {sma20SlopePercent:0.0}% versus {fivePeriodsEn} ago; the average is actually sloping upward."));
        }
        else
        {
            risks.Add(
                $"kemiringan MA20 {sma20SlopePercent:+0.0;-0.0;0.0}% dalam 5 {timeframeId}; tren belum menguat");
            risksEn.Add(
                $"MA20 slope is {sma20SlopePercent:+0.0;-0.0;0.0}% over {fivePeriodsEn}; trend strength is not improving");
        }

        var momentumConfirmed = return5 > .25m &&
                                return5 <= 8m &&
                                rsi is >= 48m and <= 68m;
        if (momentumConfirmed)
        {
            scoreValue += 8m + Math.Min(4m, return5);
            evidence.Add((
                24m + return5,
                $"Momentum 5 {timeframeId} {return5:+0.0;-0.0;0.0}% dan RSI14 {rsi:0}; naiknya terukur tetapi belum overbought.",
                $"Momentum over {fivePeriodsEn} is {return5:+0.0;-0.0;0.0}% with RSI14 at {rsi:0}; the move is measurable but not overbought."));
        }
        else if (return5 <= 0)
        {
            risks.Add(
                $"momentum 5 {timeframeId} masih {return5:+0.0;-0.0;0.0}%; harga belum menunjukkan dorongan naik");
            risksEn.Add(
                $"momentum over {fivePeriodsEn} is still {return5:+0.0;-0.0;0.0}%; price has not shown upward drive");
        }
        else if (return5 > 8m)
        {
            scoreValue -= Math.Min(10m, return5 - 8m);
            risks.Add(
                $"harga sudah naik {return5:0.0}% dalam 5 {timeframeId}; risiko mengejar momentum meningkat");
            risksEn.Add(
                $"price has already risen {return5:0.0}% over {fivePeriodsEn}; chase risk is elevated");
        }

        if (return20 > 0 && return20 <= 20m)
            scoreValue += Math.Min(4m, return20 / 4m);

        var volumeConfirmed =
            volumeRatio >= data.State.Strategy.VolumeConfirmation;
        if (volumeConfirmed)
        {
            scoreValue += 10m + Math.Min(
                5m,
                (volumeRatio - data.State.Strategy.VolumeConfirmation) * 6m);
            evidence.Add((
                32m + volumeRatio,
                $"Volume {volumeRatio:0.00}× median 20 {timeframeId}; pergerakan didukung partisipasi yang lebih besar dari normal.",
                $"Volume is {volumeRatio:0.00}× the {twentyPeriodEn} median; the move has stronger-than-normal participation."));
        }
        else if (volumeRatio < .8m)
        {
            scoreValue -= 8m;
            risks.Add(
                $"volume hanya {volumeRatio:0.00}× median; kenaikan belum mendapat konfirmasi partisipasi");
            risksEn.Add(
                $"volume is only {volumeRatio:0.00}× the median; the rise lacks participation confirmation");
        }
        else
        {
            risks.Add(
                $"volume {volumeRatio:0.00}× median, masih di bawah syarat konfirmasi {data.State.Strategy.VolumeConfirmation:0.00}×");
            risksEn.Add(
                $"volume is {volumeRatio:0.00}× the median, below the {data.State.Strategy.VolumeConfirmation:0.00}× confirmation requirement");
        }

        if (rsi is >= 48m and <= 68m)
            scoreValue += 7m + Math.Max(
                0m, 3m - Math.Abs(rsi - 58m) / 4m);
        else if (rsi < 42m)
        {
            scoreValue -= 7m;
            risks.Add($"RSI14 {rsi:0} menunjukkan momentum masih lemah");
            risksEn.Add($"RSI14 at {rsi:0} shows momentum is still weak");
        }
        if (rsi > 72)
        {
            scoreValue -= 15m + Math.Min(8m, rsi - 72m);
            risks.Add($"RSI14 {rsi:0} sudah overbought; ruang naik tidak seimbang dengan risiko pullback");
            risksEn.Add($"RSI14 at {rsi:0} is overbought; upside is not balanced against pullback risk");
        }

        var nearHigh = distanceFromHigh20 >= -1.5m;
        var strongClose = closeStrength >= .70m;
        var breakoutConfirmed = nearHigh && strongClose &&
                                volumeRatio >= 1m;
        if (breakoutConfirmed)
        {
            scoreValue += 8m;
            evidence.Add((
                30m + closeStrength,
                $"Close hanya {Math.Abs(Math.Min(0m, distanceFromHigh20)):0.0}% dari high 20 {timeframeId} dan berada di {closeStrength:P0} rentang candle; tekanan beli bertahan sampai penutupan.",
                $"The close is only {Math.Abs(Math.Min(0m, distanceFromHigh20)):0.0}% from the {twentyPeriodEn} high and at {closeStrength:P0} of the candle range; buying pressure held into the close."));
        }
        else if (nearHigh && volumeRatio < 1m)
        {
            risks.Add(
                $"harga dekat high 20 {timeframeId}, tetapi volume hanya {volumeRatio:0.00}× median; breakout belum terkonfirmasi");
            risksEn.Add(
                $"price is near the {twentyPeriodEn} high, but volume is only {volumeRatio:0.00}× the median; the breakout is unconfirmed");
        }
        else if (strongClose)
        {
            scoreValue += 4m;
            evidence.Add((
                16m + closeStrength,
                $"Close berada di {closeStrength:P0} rentang candle terakhir; pembeli mempertahankan harga dekat area atas.",
                $"The close is at {closeStrength:P0} of the latest candle range; buyers held price near the upper area."));
        }

        if (!liquidEnough)
        {
            scoreValue -= 20m;
            risks.Add(
                $"median nilai transaksi hanya Rp{medianDailyValue / 1_000_000_000m:0.0} miliar/hari, di bawah minimum Rp2 miliar");
            risksEn.Add(
                $"median traded value is only Rp{medianDailyValue / 1_000_000_000m:0.0} billion/day, below the Rp2 billion minimum");
        }
        else
        {
            scoreValue += 5m;
            evidence.Add((
                8m,
                $"Median nilai transaksi Rp{medianDailyValue / 1_000_000_000m:0.0} miliar/hari; likuiditas melewati batas minimum eksekusi.",
                $"Median traded value is Rp{medianDailyValue / 1_000_000_000m:0.0} billion/day; liquidity clears the execution minimum."));
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
        evidence.Add((
            14m,
            $"Risk/reward net fee {actualRiskReward:0.00}×: limit Rp{entry:N0}, stop Rp{stop:N0}, target Rp{target1:N0}.",
            $"Net-fee risk/reward is {actualRiskReward:0.00}×: Rp{entry:N0} limit, Rp{stop:N0} stop and Rp{target1:N0} target."));
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
        var priceActionConfirmed = breakoutConfirmed || strongClose;
        var independentConfirmations =
            (momentumConfirmed ? 1 : 0) +
            (volumeConfirmed ? 1 : 0) +
            (priceActionConfirmed ? 1 : 0);
        if (independentConfirmations < 3)
        {
            risks.Add(
                $"baru {independentConfirmations}/3 konfirmasi independen yang lolos (momentum, volume, price action); tren rata-rata saja belum cukup untuk BUY");
            risksEn.Add(
                $"only {independentConfirmations}/3 independent confirmations passed (momentum, volume and price action); moving-average trend alone is not enough for a BUY");
        }
        var trendConfirmed =
            aboveSma20 && averagesAligned && averageRising;
        var primarySetup = breakoutConfirmed && volumeConfirmed
            ? "BREAKOUT 20-HARI + VOLUME"
            : momentumConfirmed && volumeConfirmed && strongClose
                ? "TREND–MOMENTUM + VOLUME"
                : "BELUM ADA SETUP UTAMA";
        var primarySetupEn = breakoutConfirmed && volumeConfirmed
            ? "20-DAY BREAKOUT + VOLUME"
            : momentumConfirmed && volumeConfirmed && strongClose
                ? "TREND–MOMENTUM + VOLUME"
                : "NO PRIMARY SETUP YET";
        var eligible = trendConfirmed && notExtended &&
                       independentConfirmations == 3 &&
                       marketContext.Regime != "RISK_OFF" &&
                       rsi <= 70m && liquidEnough &&
                       priceEligible && freshEnough;
        var reasons = evidence
            .OrderByDescending(x => x.Strength)
            .Take(5)
            .Select(x => "• " + x.Id)
            .ToList();
        var reasonsEn = evidence
            .OrderByDescending(x => x.Strength)
            .Take(5)
            .Select(x => "• " + x.En)
            .ToList();
        reasons.Insert(0, $"• SETUP UTAMA: {primarySetup}.");
        reasonsEn.Insert(0, $"• PRIMARY SETUP: {primarySetupEn}.");
        var riskText = risks.Count == 0
            ? $"• Invalidasi utama: opening di atas Rp{entry:N0} atau harga menyentuh stop Rp{stop:N0}."
            : string.Join("\n", risks.Select(x => "• " + x));
        var riskTextEn = risksEn.Count == 0
            ? $"• Primary invalidation: an open above Rp{entry:N0} or price touching the Rp{stop:N0} stop."
            : string.Join("\n", risksEn.Select(x => "• " + x));
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
            SignalOpen = signalCandle.Open,
            SignalHigh = signalCandle.High,
            SignalLow = signalCandle.Low,
            SignalClose = signalCandle.Close,
            MovingAverage20 = sma20,
            MovingAverage50 = sma50,
            MovingAverage20SlopePercent = sma20SlopePercent,
            Return5PeriodsPercent = return5,
            Return20PeriodsPercent = return20,
            VolumeRatio = volumeRatio,
            Rsi14 = rsi,
            MedianDailyValue = medianDailyValue,
            AtrPercent = atrPercent,
            PrimarySetup = primarySetup,
            PrimarySetupEn = primarySetupEn,
            ResearchStatus = data.State.Strategy.Training is
                { QualityGatePassed: true, Status: "READY_FOR_FORWARD_TEST" }
                    ? "RULE_BASED_TUNED_PARAMETERS"
                    : "RULE_BASED_UNVALIDATED",
            MarketReturn20Percent = marketContext.Return20Percent,
            MarketBreadth20Percent = marketContext.Breadth20Percent,
            MarketRegime = marketContext.Regime,
            Reasons = reasons.Count == 0
                ? "• Belum ada konfirmasi independen yang cukup."
                : string.Join("\n", reasons),
            ReasonsEn = reasonsEn.Count == 0
                ? "• There is not enough independent confirmation yet."
                : string.Join("\n", reasonsEn),
            Risks = riskText,
            RisksEn = riskTextEn,
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
