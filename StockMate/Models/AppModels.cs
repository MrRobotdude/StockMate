using System.Text.Json.Serialization;

namespace StockMate.Models;

public sealed class AppState
{
    public string LanguageCode { get; set; } = "id";
    public int DataSchemaVersion { get; set; }
    public decimal Cash { get; set; }
    public decimal? CashOpeningBalance { get; set; }
    public bool CashReconciled { get; set; }
    public DateTime? CashReconciledAt { get; set; }
    public decimal? OfficialRealizedProfit { get; set; }
    public DateTime? RealizedReconciledAt { get; set; }
    public decimal BuyFeeRate { get; set; } = 0.0015m;
    public decimal SellFeeRate { get; set; } = 0.0025m;
    public decimal RiskPerTrade { get; set; } = 25_000m;
    public decimal MaxRiskPerTrade { get; set; } = 35_000m;
    public decimal MonthlyWarning { get; set; } = 200_000m;
    public decimal MonthlyLimit { get; set; } = 250_000m;
    public decimal MaxOpenRisk { get; set; } = 120_000m;
    public decimal MinRiskReward { get; set; } = 2m;
    public bool IncludeSpeculative { get; set; }
    public string StrategyVersion { get; set; } = "1.0.0";
    public StrategyConfig Strategy { get; set; } = new();
    public List<Position> Positions { get; set; } = [];
    public List<TradeTransaction> Transactions { get; set; } = [];
    public List<TransactionImportBatch> TransactionImports { get; set; } = [];
    public List<JournalEntry> Journal { get; set; } = []; // compatibility with v1 data
    public List<ScanResult> LastScan { get; set; } = [];
    public List<ScanRun> ScanHistory { get; set; } = [];
    public List<string> MarketUniverse { get; set; } = [];
    public List<MarketSnapshot> MarketSnapshots { get; set; } = [];
    public List<PortfolioDecision> PortfolioDecisions { get; set; } = [];
    public List<EventInsight> EventInsights { get; set; } = [];
    public bool AutoEventIntelligence { get; set; } = true;
    public DateTime? EventIntelligenceUpdatedAt { get; set; }
    // Scheduled by Android at 12:15 and 16:30 WIB. This is deliberately
    // independent from opening the Scanner page.
    public bool AutoScanAfterClose { get; set; } = true;
    public int RequestDelayMilliseconds { get; set; } = 750;
    public DateTime? UniverseUpdatedAt { get; set; }
    public string UniverseSource { get; set; } = "";
}

public sealed class ScanProgress
{
    public string Stage { get; set; } = "PREPARING";
    public string Message { get; set; } = "Menyiapkan scanner";
    public string CurrentSymbol { get; set; } = "";
    public int Completed { get; set; }
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int CurrentBatch { get; set; }
    public int TotalBatches { get; set; }
    public int BatchCompleted { get; set; }
    public int BatchSize { get; set; }
    public int LastCompletedBatch { get; set; }
    public string TechnicalDetail { get; set; } = "";
    public string Source { get; set; } = "";
    public int Attempt { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.Now;
    public int Percent => Total <= 0 ? 0 : Math.Clamp((int)Math.Round(Completed * 100d / Total), 0, 100);
    public bool IsIndeterminate => Total <= 0;
    public string DisplayText => Total <= 0
        ? Message
        : $"{Message} • {Completed}/{Total} ({Percent}%) • berhasil {Succeeded} • gagal {Failed}";
}

public sealed class PortfolioDecision
{
    public string Symbol { get; set; } = "";
    public string Action { get; set; } = "HOLD";
    public string Confidence { get; set; } = "RENDAH";
    public int Score { get; set; }
    public int SuggestedLots { get; set; }
    public decimal ReferencePrice { get; set; }
    public decimal EntryLow { get; set; }
    public decimal EntryHigh { get; set; }
    public decimal MaxBuyPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal Target { get; set; }
    public string RiskAction { get; set; } = "";
    public decimal TrailingStopPercent { get; set; }
    public string TakeProfitAction { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Invalidation { get; set; } = "";
    public int TechnicalScore { get; set; }
    public int EventAdjustment { get; set; }
    public string EventSummary { get; set; } = "Data isu belum tersedia.";
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
}

public sealed class EventInsight
{
    public string Symbol { get; set; } = "MARKET";
    public string Title { get; set; } = "";
    public string Source { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTime PublishedAt { get; set; }
    public int Impact { get; set; }
    public string Direction { get; set; } = "NETRAL";
    public string Reason { get; set; } = "";
    public DateTime RetrievedAt { get; set; } = DateTime.Now;
}

public sealed class MarketSnapshot
{
    public string SessionKey { get; set; } = "";
    public string Session { get; set; } = "EVENING";
    public DateTime CapturedAt { get; set; } = DateTime.Now;
    public int RequestedCount { get; set; }
    public int CompletedCount { get; set; }
    public bool IsComplete { get; set; }
    public bool ClosingVerified { get; set; }
    public string Status { get; set; } = "IN_PROGRESS";
    public DateTime? TradingDate { get; set; }
    public List<string> FailedSymbols { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public List<SymbolMarketData> Symbols { get; set; } = [];
}

public sealed class RealizedProfitDetail
{
    public string Symbol { get; set; } = "";
    public DateTime SellDate { get; set; }
    public int SoldShares { get; set; }
    public decimal SellPrice { get; set; }
    public decimal GrossProceeds { get; set; }
    public decimal AllocatedCost { get; set; }
    public decimal RecordedSellFee { get; set; }
    public decimal ProfitBeforeRecordedFee { get; set; }
    public decimal ProfitAfterRecordedFee { get; set; }
    public string FeeBasis { get; set; } = "";
}

public sealed class RealizedProfitSummary
{
    public decimal Estimated { get; set; }
    public decimal DisplayValue { get; set; }
    public decimal Fees { get; set; }
    public bool IsOfficial { get; set; }
    public decimal? ReconciliationDifference { get; set; }
}

public sealed class MissingCostBasis
{
    public string Symbol { get; set; } = "";
    public int MissingShares { get; set; }
    public DateTime FirstUncoveredSellDate { get; set; }
    [JsonIgnore] public int MinimumLots => (int)Math.Ceiling(MissingShares / 100m);
}

public sealed class SymbolMarketData
{
    public string Symbol { get; set; } = "";
    public bool IsSpeculative { get; set; }
    public List<Candle> Candles { get; set; } = [];
}

public sealed class StrategyConfig
{
    public string Version { get; set; } = "1.0.0";
    public decimal MinimumRiskReward { get; set; } = 2m;
    public decimal VolumeConfirmation { get; set; } = 1.25m;
    public decimal AtrStopMultiplier { get; set; } = 1.35m;
    public int BuyScore { get; set; } = 78;
    public int WatchScore { get; set; } = 65;
    public int MaximumNormalPosition { get; set; } = 2_000_000;
    public int MaximumSpeculativePosition { get; set; } = 500_000;
    public StrategyTrainingMetadata? Training { get; set; }
}

public sealed class StrategyTrainingMetadata
{
    public string Method { get; set; } = "";
    public DateTime TrainedAtUtc { get; set; }
    public string DataStart { get; set; } = "";
    public string DataEnd { get; set; } = "";
    public int OutOfSampleFolds { get; set; }
    public int OutOfSampleTrades { get; set; }
    public decimal OutOfSampleWinRate { get; set; }
    public decimal OutOfSampleAverageReturn { get; set; }
    public decimal OutOfSampleMaxDrawdown { get; set; }
    public string DataFingerprint { get; set; } = "";
}

public sealed class Position
{
    public string Symbol { get; set; } = "";
    public int Lots { get; set; }
    public decimal AveragePrice { get; set; }
    public decimal LastPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    [JsonIgnore] public int Shares => Lots * 100;
    [JsonIgnore] public decimal Cost => Shares * AveragePrice;
    [JsonIgnore] public decimal MarketValue => Shares * LastPrice;
    [JsonIgnore] public decimal ProfitLoss => MarketValue - Cost;
    [JsonIgnore] public decimal ProfitLossPercent => Cost == 0 ? 0 : ProfitLoss / Cost * 100;
}

public sealed class JournalEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Time { get; set; } = DateTime.Now;
    public string Symbol { get; set; } = "";
    public string Side { get; set; } = "BUY";
    public int Lots { get; set; }
    public decimal Price { get; set; }
    public string Note { get; set; } = "";
}

public sealed class TradeTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Time { get; set; } = DateTime.Now;
    public string Symbol { get; set; } = "";
    public string Side { get; set; } = "BUY";
    public int Lots { get; set; }
    public decimal Price { get; set; }
    public decimal Fee { get; set; }
    public bool AffectsCash { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public string Source { get; set; } = "MANUAL";
    public string ExternalId { get; set; } = "";
    public DateTime? DueDate { get; set; }
    public decimal SalesTax { get; set; }
    public Guid? ImportBatchId { get; set; }
    public Guid? SupersededByImportBatchId { get; set; }
    public string Note { get; set; } = "";
    [JsonIgnore] public int Shares => Lots * 100;
    [JsonIgnore] public decimal GrossValue => Shares * Price;
    [JsonIgnore] public decimal NetCashFlow => Side == "BUY"
        ? -(GrossValue + Fee)
        : GrossValue - Fee;
}

public sealed class TransactionImportBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime ImportedAt { get; set; } = DateTime.Now;
    public string FileName { get; set; } = "";
    public DateTime CoverageStart { get; set; }
    public DateTime CoverageEnd { get; set; }
    public int ImportedCount { get; set; }
    public int AddedCount { get; set; }
    public int SkippedDuplicateCount { get; set; }
    public int SupersededManualCount { get; set; }
    public string FileFingerprint { get; set; } = "";
    public List<string> ReconciliationDetails { get; set; } = [];
}

public sealed class Candle
{
    public DateTime Time { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
}

public sealed class ScanResult
{
    public string Symbol { get; set; } = "";
    public string Verdict { get; set; } = "WATCH";
    public int Score { get; set; }
    public decimal LastPrice { get; set; }
    public decimal EntryLow { get; set; }
    public decimal EntryHigh { get; set; }
    public decimal MaxBuyPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal Target1 { get; set; }
    public decimal Target2 { get; set; }
    public int SuggestedLots { get; set; }
    public decimal RiskReward { get; set; }
    public DateTime DataTime { get; set; }
    public string DataSession { get; set; } = "";
    public string Reasons { get; set; } = "";
    public string Risks { get; set; } = "";
    public bool IsSpeculative { get; set; }
    public int AllocationRank { get; set; }
    public decimal AllocatedCash { get; set; }
    public string ExecutionNote { get; set; } = "";
}

public sealed class ScanRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime RunTime { get; set; } = DateTime.Now;
    public string Session { get; set; } = "EVENING";
    public string StrategyVersion { get; set; } = "1.0.0";
    public int UniverseCount { get; set; }
    public int SuccessfulCount { get; set; }
    public string SessionKey { get; set; } = "";
    public DateTime MarketDataCapturedAt { get; set; }
    public bool UsedCachedMarketData { get; set; }
    public bool SnapshotComplete { get; set; }
    public int EvaluatedCount { get; set; }
    public int ShortlistCount { get; set; }
    public List<PredictionRecord> Predictions { get; set; } = [];
}

public sealed class PredictionRecord
{
    public string Symbol { get; set; } = "";
    public string Verdict { get; set; } = "";
    public int Score { get; set; }
    public decimal StartPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal Target1 { get; set; }
    public DateTime PredictedAt { get; set; } = DateTime.Now;
    public string DataSession { get; set; } = "";
    public DateTime? EvaluatedAt { get; set; }
    public decimal? EvaluationPrice { get; set; }
    public decimal? ReturnPercent { get; set; }
    public string Outcome { get; set; } = "PENDING";
}
