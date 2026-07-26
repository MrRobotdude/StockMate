using System.Text.Json;
using StockMate.Models;
using StockMate.Ui;

namespace StockMate.Services;

public sealed class AppDataService
{
    readonly string _path = Path.Combine(FileSystem.AppDataDirectory, "stockmate.json");
    readonly JsonSerializerOptions _json = new() { WriteIndented = false };
    readonly SemaphoreSlim _ioGate = new(1, 1);
    readonly object _loadLock = new();
    Task? _loadTask;
    long _revision;
    public AppState State { get; private set; } = new();
    public long Revision => Interlocked.Read(ref _revision);
    public event Action? Changed;

    public Task LoadAsync()
    {
        lock (_loadLock)
            return _loadTask ??= LoadCoreAsync();
    }

    async Task LoadCoreAsync()
    {
        try
        {
            if (File.Exists(_path))
                State = await Task.Run(async () =>
                    JsonSerializer.Deserialize<AppState>(
                        await File.ReadAllTextAsync(_path).ConfigureAwait(false), _json) ?? Empty())
                    .ConfigureAwait(false);
            else State = Empty();
        }
        catch { State = Empty(); }
        var schemaBefore = State.DataSchemaVersion;
        Migrate();
        RebuildPositions();
        Interlocked.Increment(ref _revision);
        if (schemaBefore != State.DataSchemaVersion)
            await SaveCoreAsync(notify: false).ConfigureAwait(false);
    }

    public async Task SaveAsync()
    {
        Interlocked.Increment(ref _revision);
        await SaveCoreAsync(notify: true).ConfigureAwait(false);
    }

    async Task SaveCoreAsync(bool notify)
    {
        await _ioGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var bytes = await Task.Run(() => JsonSerializer.SerializeToUtf8Bytes(State, _json))
                .ConfigureAwait(false);
            var tempPath = _path + ".tmp";
            await File.WriteAllBytesAsync(tempPath, bytes).ConfigureAwait(false);
            File.Move(tempPath, _path, true);
        }
        finally
        {
            _ioGate.Release();
        }
        if (!notify) return;
        NotifyChanged();
    }

    public void Reset() => State = Empty();

    public async Task ResetScanDataAsync()
    {
        State.LastScan.Clear();
        State.ScanHistory.Clear();
        State.MarketSnapshots.Clear();
        State.PortfolioDecisions.Clear();
        foreach (var position in State.Positions)
            position.LastPrice = position.AveragePrice;
        await SaveAsync();
    }

    public async Task ResetTransactionHistoryAsync()
    {
        State.Transactions.Clear();
        State.TransactionImports.Clear();
        State.Positions.Clear();
        State.OfficialRealizedProfit = null;
        State.RealizedReconciledAt = null;
        State.CashOpeningBalance = null;
        State.Cash = 0;
        State.CashReconciled = false;
        State.CashReconciledAt = null;
        await SaveAsync();
    }

    public async Task ResetAllAsync()
    {
        State = Empty();
        await SaveAsync();
    }

    void Migrate()
    {
        State.Positions ??= [];
        State.Transactions ??= [];
        State.TransactionImports ??= [];
        State.LastScan ??= [];
        State.ScanHistory ??= [];
        State.MarketUniverse ??= [];
        State.MarketSnapshots ??= [];
        State.PortfolioDecisions ??= [];
        State.EventInsights ??= [];
        State.Strategy ??= new StrategyConfig();
        foreach (var snapshot in State.MarketSnapshots)
        {
            snapshot.FailedSymbols ??= [];
            snapshot.Errors ??= [];
            snapshot.Symbols ??= [];
            if (snapshot.CompletedCount == 0) snapshot.CompletedCount = snapshot.Symbols.Count;
        }
        foreach (var batch in State.TransactionImports)
            batch.ReconciliationDetails ??= [];
        if (State.DataSchemaVersion < 157)
        {
            foreach (var tx in State.Transactions.Where(x =>
                         x.IsActive && x.Source == "HISTORY" &&
                         x.Note.Contains("e-Statement Stockbit",
                             StringComparison.OrdinalIgnoreCase)))
            {
                var rate = tx.Side == "BUY" ? State.BuyFeeRate : State.SellFeeRate;
                tx.Fee = decimal.Round(tx.GrossValue * rate, 0);
            }
            foreach (var tx in State.Transactions.Where(x =>
                         x.IsActive && x.Source == "IPO_SYNC" && x.Fee <= 0))
                tx.Fee = decimal.Round(tx.GrossValue * State.BuyFeeRate, 0);

            // v1.5.6 did not collect the official realized figure and used
            // sales tax as total brokerage fee. Require a one-time re-sync.
            State.OfficialRealizedProfit = null;
            State.RealizedReconciledAt = null;
            State.CashReconciled = false;
            State.CashReconciledAt = null;
            State.DataSchemaVersion = 157;
        }
        if (State.DataSchemaVersion < 162)
        {
            // Older versions treated every analyzable symbol, including WAIT,
            // as a prediction. Retain only actual recommendations so historical
            // accuracy is not inflated by hundreds of non-signals.
            foreach (var run in State.ScanHistory)
            {
                run.EvaluatedCount = Math.Max(run.EvaluatedCount, run.Predictions.Count);
                run.Predictions = run.Predictions
                    .Where(x => x.Verdict == "BUY AREA")
                    .OrderByDescending(x => x.Score)
                    .Take(30)
                    .ToList();
                run.ShortlistCount = run.Predictions.Count;
            }
            State.DataSchemaVersion = 162;
        }
        if (State.DataSchemaVersion < 168)
        {
            // v1.6.8 imports are idempotent and no longer invalidate official
            // cash/realized reconciliation merely because a statement overlaps.
            foreach (var batch in State.TransactionImports)
            {
                if (batch.AddedCount == 0 && batch.SkippedDuplicateCount == 0)
                    batch.AddedCount = batch.ImportedCount;
            }
            State.DataSchemaVersion = 168;
        }
        if (State.DataSchemaVersion < 172)
        {
            // v1.6.12 replaces the hidden page-open trigger with an explicit
            // Android background schedule requested by the user.
            State.AutoScanAfterClose = true;
            State.DataSchemaVersion = 172;
        }
        if (State.DataSchemaVersion < 1700)
        {
            State.EventInsights ??= [];
            State.AutoEventIntelligence = true;
            State.DataSchemaVersion = 1700;
        }
        if (State.DataSchemaVersion < 1800)
        {
            // v0.7.2 corrects reversed RSI, true-range ATR, entry-based risk,
            // and GFD outcome evaluation. Old scanner output is not comparable
            // and must not remain visible as if it used the corrected engine.
            State.LastScan.Clear();
            State.ScanHistory.Clear();
            State.MarketSnapshots.Clear();
            State.PortfolioDecisions.Clear();
            State.DataSchemaVersion = 1800;
        }
        // Positions are derived only from imported or explicitly entered
        // transactions. Never manufacture opening transactions from UI data.
        if (State.Transactions.Count == 0)
            State.Positions = [];
        if (State.CashOpeningBalance is null)
            State.CashOpeningBalance = State.Cash -
                State.Transactions.Where(x => x.IsActive && x.AffectsCash).Sum(x => x.NetCashFlow);
        RecalculateCash();
    }

    public async Task SetUniverseAsync(IEnumerable<string> symbols)
    {
        State.MarketUniverse = symbols
            .Select(NormalizeSymbol)
            .Where(x => x.Length is >= 4 and <= 6)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
        await SaveAsync();
    }

    static string NormalizeSymbol(string value)
    {
        var symbol = value.Trim().ToUpperInvariant();
        return symbol.EndsWith(".JK", StringComparison.OrdinalIgnoreCase)
            ? symbol[..^3]
            : symbol;
    }

    public async Task<(bool Ok, string Message)> AddTransactionAsync(
        string symbol, string side, int lots, decimal price, string note = "")
    {
        symbol = NormalizeSymbol(symbol);
        side = side.Trim().ToUpperInvariant();
        if (symbol.Length < 4 || symbol.Length > 6 || lots <= 0 || price <= 0)
            return (false, Loc.T(
                "Kode saham, jumlah lot, atau harga tidak valid.",
                "The stock symbol, lot count, or price is invalid."));
        if (side is not ("BUY" or "SELL"))
            return (false, Loc.T(
                "Jenis transaksi harus BUY atau SELL.",
                "The transaction side must be BUY or SELL."));
        if (State.MarketUniverse.Count > 0 &&
            !State.MarketUniverse.Contains(symbol, StringComparer.OrdinalIgnoreCase))
            return (false, Loc.T(
                $"{symbol} tidak ditemukan pada universe IDX tersimpan.",
                $"{symbol} was not found in the saved IDX universe."));

        RebuildPositions();
        var current = State.Positions.FirstOrDefault(x => x.Symbol == symbol);
        if (side == "SELL" && (current is null || current.Lots < lots))
            return (false, Loc.T(
                $"Lot {symbol} tidak cukup. Tersedia {current?.Lots ?? 0} lot.",
                $"There are not enough {symbol} lots. Available: {current?.Lots ?? 0} lots."));

        var gross = lots * 100m * price;
        var fee = decimal.Round(gross * (side == "BUY" ? State.BuyFeeRate : State.SellFeeRate), 0);
        if (side == "BUY" && State.Cash < gross + fee)
            return (false, Loc.T(
                $"Kas tidak cukup. Dibutuhkan Rp {gross + fee:N0}.",
                $"Insufficient cash. Rp {gross + fee:N0} is required."));

        State.Transactions.Add(new TradeTransaction
        {
            Symbol = symbol, Side = side, Lots = lots, Price = price, Fee = fee,
            Source = "MANUAL", IsActive = true, Note = note
        });
        RecalculateCash();
        RebuildPositions();
        await SaveAsync();
        return (true, Loc.T(
            $"{side} {symbol} {lots} lot tercatat. Fee Rp {fee:N0}.",
            $"{side} {symbol} {lots} lots recorded. Fee Rp {fee:N0}."));
    }

    public void RebuildPositions()
    {
        var oldPositions = State.Positions
            .GroupBy(x => x.Symbol)
            .ToDictionary(x => x.Key, x => x.Last());
        var rebuilt = new List<Position>();
        foreach (var group in State.Transactions.Where(x => x.IsActive).OrderBy(x => x.Time).GroupBy(x => x.Symbol))
        {
            var shares = 0;
            decimal carryingCost = 0;
            foreach (var tx in group.OrderBy(x => x.Time))
            {
                if (tx.Side == "BUY")
                {
                    shares += tx.Shares;
                    carryingCost += tx.GrossValue + tx.Fee;
                }
                else if (shares > 0)
                {
                    var sold = Math.Min(shares, tx.Shares);
                    carryingCost -= carryingCost / shares * sold;
                    shares -= sold;
                }
            }
            if (shares <= 0) continue;
            var symbol = group.Key;
            var old = oldPositions.GetValueOrDefault(symbol);
            rebuilt.Add(new Position
            {
                Symbol = symbol,
                Lots = shares / 100,
                AveragePrice = carryingCost / shares,
                LastPrice = old?.LastPrice ?? carryingCost / shares,
                StopLoss = old?.StopLoss ?? 0,
                TakeProfit = old?.TakeProfit ?? 0
            });
        }
        State.Positions = rebuilt;
    }

    public void RecalculateCash()
    {
        State.Cash = (State.CashOpeningBalance ?? 0) +
            State.Transactions.Where(x => x.IsActive && x.AffectsCash).Sum(x => x.NetCashFlow);
    }

    public RealizedProfitSummary GetRealizedSummary()
    {
        var details = GetRealizedDetails();
        var estimated = details.Sum(x => x.ProfitAfterRecordedFee);
        var officialValue = State.OfficialRealizedProfit;
        return new RealizedProfitSummary
        {
            Estimated = estimated,
            DisplayValue = officialValue ?? estimated,
            Fees = State.Transactions.Where(x => x.IsActive).Sum(x => x.Fee),
            IsOfficial = officialValue.HasValue,
            ReconciliationDifference = officialValue.HasValue
                ? officialValue.Value - estimated
                : null
        };
    }

    public List<RealizedProfitDetail> GetRealizedDetails()
    {
        var output = new List<RealizedProfitDetail>();
        foreach (var group in State.Transactions.Where(x => x.IsActive)
                     .OrderBy(x => x.Time).GroupBy(x => x.Symbol))
        {
            var shares = 0;
            decimal carryingCost = 0;
            foreach (var tx in group.OrderBy(x => x.Time))
            {
                if (tx.Side == "BUY")
                {
                    shares += tx.Shares;
                    carryingCost += tx.GrossValue + tx.Fee;
                }
                else if (shares > 0)
                {
                    var sold = Math.Min(shares, tx.Shares);
                    var allocatedCost = carryingCost / shares * sold;
                    var allocatedSellFee = tx.Shares == 0 ? 0 : tx.Fee * sold / tx.Shares;
                    var grossProceeds = sold * tx.Price;
                    output.Add(new RealizedProfitDetail
                    {
                        Symbol = group.Key,
                        SellDate = tx.Time,
                        SoldShares = sold,
                        SellPrice = tx.Price,
                        GrossProceeds = grossProceeds,
                        AllocatedCost = allocatedCost,
                        RecordedSellFee = allocatedSellFee,
                        ProfitBeforeRecordedFee = grossProceeds - allocatedCost,
                        ProfitAfterRecordedFee = grossProceeds - allocatedSellFee - allocatedCost,
                        FeeBasis = tx.Source == "HISTORY"
                            ? "e-Statement PDF: sales tax saja"
                            : "fee transaksi tercatat"
                    });
                    carryingCost -= allocatedCost;
                    shares -= sold;
                }
            }
        }
        return output;
    }

    public List<MissingCostBasis> GetMissingCostBasis()
    {
        var output = new List<MissingCostBasis>();
        foreach (var group in State.Transactions.Where(x => x.IsActive)
                     .OrderBy(x => x.Time).GroupBy(x => x.Symbol))
        {
            var availableShares = 0;
            var missingShares = 0;
            DateTime? firstUncoveredSell = null;
            foreach (var tx in group.OrderBy(x => x.Time))
            {
                if (tx.Side == "BUY")
                {
                    availableShares += tx.Shares;
                    continue;
                }

                if (tx.Side != "SELL") continue;
                var uncovered = Math.Max(0, tx.Shares - availableShares);
                if (uncovered > 0)
                {
                    missingShares += uncovered;
                    firstUncoveredSell ??= tx.Time;
                }
                availableShares = Math.Max(0, availableShares - tx.Shares);
            }

            if (missingShares > 0 && firstUncoveredSell.HasValue)
                output.Add(new MissingCostBasis
                {
                    Symbol = group.Key,
                    MissingShares = missingShares,
                    FirstUncoveredSellDate = firstUncoveredSell.Value
                });
        }
        return output.OrderBy(x => x.FirstUncoveredSellDate).ThenBy(x => x.Symbol).ToList();
    }

    public async Task<(bool Ok, string Message)> UpsertExternalAcquisitionAsync(
        MissingCostBasis missing, int lots, decimal averagePrice, decimal acquisitionFee,
        string acquisitionType)
    {
        if (lots < missing.MinimumLots || averagePrice <= 0 || acquisitionFee < 0)
            return (false, Loc.T(
                $"{missing.Symbol}: minimal {missing.MinimumLots} lot, harga harus di atas 0, dan biaya tidak boleh negatif.",
                $"{missing.Symbol}: enter at least {missing.MinimumLots} lots, a price above 0, and a non-negative fee."));

        foreach (var existing in State.Transactions.Where(x =>
                     x.IsActive && x.Source == "IPO_SYNC" &&
                     x.Symbol.Equals(missing.Symbol, StringComparison.OrdinalIgnoreCase)))
            existing.IsActive = false;

        State.Transactions.Add(new TradeTransaction
        {
            Time = missing.FirstUncoveredSellDate.AddTicks(-1),
            Symbol = missing.Symbol,
            Side = "BUY",
            Lots = lots,
            Price = averagePrice,
            Fee = acquisitionFee > 0
                ? acquisitionFee
                : decimal.Round(lots * 100m * averagePrice * State.BuyFeeRate, 0),
            AffectsCash = true,
            IsActive = true,
            Source = "IPO_SYNC",
            ExternalId = $"SYNC-{missing.Symbol}-{missing.FirstUncoveredSellDate:yyyyMMdd}",
            Note = $"{acquisitionType}; cost basis dilengkapi saat Sync Up"
        });
        RebuildPositions();
        RecalculateCash();
        await SaveAsync();
        return (true, Loc.T(
            $"{missing.Symbol}: cost basis {lots} lot @ Rp {averagePrice:N0} disimpan.",
            $"{missing.Symbol}: cost basis for {lots} lots @ Rp {averagePrice:N0} was saved."));
    }

    public void ApplyMarketPrices(IEnumerable<ScanResult> results)
    {
        var prices = results.GroupBy(x => x.Symbol)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.DataTime).First().LastPrice);
        foreach (var position in State.Positions)
            if (prices.TryGetValue(position.Symbol, out var price))
                position.LastPrice = price;
    }

    public void ApplyMarketPrices(MarketSnapshot snapshot)
    {
        var prices = snapshot.Symbols
            .Where(x => x.Candles.Count > 0)
            .ToDictionary(x => x.Symbol, x => x.Candles[^1].Close,
                StringComparer.OrdinalIgnoreCase);
        foreach (var position in State.Positions)
            if (prices.TryGetValue(position.Symbol, out var price) && price > 0)
                position.LastPrice = price;
    }

    public bool ApplyMarketPrice(string symbol, decimal price)
    {
        if (price <= 0) return false;
        var position = State.Positions.FirstOrDefault(x =>
            x.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
        if (position is null) return false;
        position.LastPrice = price;
        Interlocked.Increment(ref _revision);
        NotifyChanged();
        return true;
    }

    void NotifyChanged()
    {
        var handlers = Changed?.GetInvocationList();
        if (handlers is null) return;
        foreach (var handler in handlers)
            try
            {
                ((Action)handler)();
            }
            catch
            {
                // A page may have been replaced while a save was completing.
                // Persisted data remains valid; ignore only the stale UI listener.
            }
    }

    static AppState Empty() => new();
}
