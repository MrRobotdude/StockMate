using StockMate.Models;
using StockMate.Ui;

namespace StockMate.Services;

public sealed class PortfolioDecisionService(
    AppDataService data, EventIntelligenceService events)
{
    long _builtForRevision = -1;
    readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<List<PortfolioDecision>> RebuildAsync()
    {
        if (_builtForRevision == data.Revision)
            return data.State.PortfolioDecisions;

        await _gate.WaitAsync();
        try
        {
            if (_builtForRevision == data.Revision)
                return data.State.PortfolioDecisions;

            data.RebuildPositions();
            var scans = data.State.LastScan.ToDictionary(
                x => x.Symbol, StringComparer.OrdinalIgnoreCase);
            var totalEquity = data.State.Cash +
                data.State.Positions.Sum(x => x.MarketValue);
            var decisions = new List<PortfolioDecision>();

            foreach (var position in data.State.Positions
                         .OrderByDescending(x => x.MarketValue))
            {
                scans.TryGetValue(position.Symbol, out var scan);
                var weight = totalEquity <= 0
                    ? 0
                    : position.MarketValue / totalEquity;
                var technicalScore = scan?.Score ?? 0;
                var eventView = data.State.AutoEventIntelligence
                    ? events.Summarize(position.Symbol)
                    : (Adjustment: 0, Summary: Loc.T(
                        "Analisis isu dinonaktifkan.",
                        "Event analysis is disabled."));
                var score = Math.Clamp(
                    technicalScore + eventView.Adjustment, 0, 100);
                var effectiveStop = EffectiveStop(position, scan);
                var effectiveTarget = position.TakeProfit > 0
                    ? position.TakeProfit
                    : scan?.Target1 ?? 0;

                var actionCode = "HOLD_NO_ADD";
                var suggestedLots = 0;
                var actionLots = 0;
                var executionPrice = position.LastPrice;

                if (effectiveStop > 0 &&
                    position.LastPrice <= effectiveStop)
                {
                    actionCode = "SELL_ALL";
                    actionLots = position.Lots;
                    executionPrice = position.LastPrice;
                }
                else if (scan is not null && score < 55)
                {
                    if (position.ProfitLossPercent < -5 ||
                        position.Lots <= 1)
                    {
                        actionCode = "SELL_ALL";
                        actionLots = position.Lots;
                    }
                    else
                    {
                        actionCode = "REDUCE";
                        actionLots = HalfLots(position.Lots);
                    }
                    executionPrice = position.LastPrice;
                }
                else if (effectiveTarget > 0 &&
                         position.LastPrice >= effectiveTarget)
                {
                    actionCode = "TAKE_PROFIT";
                    actionLots = HalfLots(position.Lots);
                    executionPrice = effectiveTarget;
                }
                else if (scan is not null &&
                         score >= data.State.Strategy.BuyScore &&
                         weight < 0.20m &&
                         position.LastPrice <= scan.EntryHigh)
                {
                    suggestedLots = CalculateAddLots(
                        position, scan, totalEquity, effectiveStop);
                    actionCode = suggestedLots <= 0
                        ? "WATCH_NO_ADD"
                        : position.ProfitLossPercent < 0
                            ? "AVERAGE_DOWN"
                            : "ADD";
                    executionPrice = scan.EntryHigh;
                }
                else if (weight > 0.30m && position.Lots > 1)
                {
                    actionLots = LotsToConcentrationTarget(
                        position, totalEquity, 0.25m);
                    if (actionLots > 0)
                    {
                        actionCode = "REDUCE";
                        executionPrice = position.LastPrice;
                    }
                }

                var reason = BuildReason(
                    scan, technicalScore, eventView.Adjustment, score,
                    position.ProfitLossPercent, weight);
                var confidenceScore = ConfidenceScore(
                    actionCode, scan, score, weight, effectiveStop);
                var confidence = confidenceScore >= 80
                    ? "HIGH"
                    : confidenceScore >= 60 ? "MEDIUM" : "LOW";
                var trailing = TrailingPercent(
                    actionCode, position, scan, effectiveStop);

                decisions.Add(new PortfolioDecision
                {
                    Symbol = position.Symbol,
                    ActionCode = actionCode,
                    // Keep a stable legacy value for old saved-state readers.
                    Action = actionCode,
                    Score = score,
                    SuggestedLots = suggestedLots,
                    ActionLots = actionLots,
                    ExecutionPrice = executionPrice,
                    TechnicalScore = technicalScore,
                    EventAdjustment = eventView.Adjustment,
                    EventSummary = eventView.Summary,
                    ReferencePrice = position.LastPrice,
                    StopLoss = effectiveStop,
                    EntryLow = scan?.EntryHigh ?? position.LastPrice,
                    EntryHigh = scan?.EntryHigh ?? position.LastPrice,
                    MaxBuyPrice = scan?.EntryHigh ?? position.LastPrice,
                    Target = effectiveTarget,
                    Confidence = confidence,
                    ConfidenceScore = confidenceScore,
                    RiskAction = RiskAction(
                        actionCode, actionLots, suggestedLots,
                        position, scan, trailing, effectiveStop),
                    TrailingStopPercent = trailing,
                    TakeProfitAction = TakeProfitAction(
                        actionCode, actionLots, position, scan,
                        effectiveTarget),
                    Reason = reason,
                    Invalidation = Invalidation(
                        actionCode, scan, effectiveStop)
                });
            }

            data.State.PortfolioDecisions = decisions;
            // Decisions are derived state but must survive an app restart. Save
            // once and then cache against the new revision produced by SaveAsync.
            await data.SaveAsync();
            _builtForRevision = data.Revision;
            return decisions;
        }
        finally
        {
            _gate.Release();
        }
    }

    int CalculateAddLots(
        Position position, ScanResult scan, decimal totalEquity,
        decimal effectiveStop)
    {
        var entry = scan.EntryHigh;
        var entryCostPerShare =
            entry * (1m + data.State.BuyFeeRate);
        var stopProceedsPerShare =
            effectiveStop * (1m - data.State.SellFeeRate);
        var riskPerLot = Math.Max(
            0, entryCostPerShare - stopProceedsPerShare) * 100m;
        if (entry <= 0 || riskPerLot <= 0 || data.State.Cash <= 0)
            return 0;

        var oneLotCash = entry * 100m * (1m + data.State.BuyFeeRate);
        var byRisk = (int)Math.Floor(
            data.State.RiskPerTrade / riskPerLot);
        var byCash = (int)Math.Floor(data.State.Cash / oneLotCash);
        var concentrationRoom = Math.Max(
            0, totalEquity * 0.20m - position.MarketValue);
        var byConcentration = (int)Math.Floor(
            concentrationRoom / oneLotCash);
        var maximumValue = scan.IsSpeculative
            ? data.State.Strategy.MaximumSpeculativePosition
            : data.State.Strategy.MaximumNormalPosition;
        var byStrategyCap = (int)Math.Floor(
            Math.Max(0, maximumValue - position.MarketValue) /
            oneLotCash);

        return Math.Max(0, new[]
        {
            byRisk, byCash, byConcentration, byStrategyCap
        }.Min());
    }

    static int HalfLots(int lots) =>
        lots <= 0 ? 0 : (int)Math.Ceiling(lots / 2m);

    static decimal EffectiveStop(
        Position position, ScanResult? scan)
    {
        if (position.StopLoss <= 0)
            return scan?.StopLoss ?? 0;
        if (scan is null || scan.StopLoss <= 0)
            return position.StopLoss;
        // For a long position, the higher stop is tighter. A new scan must
        // never silently widen a stop the user already placed.
        return Math.Max(position.StopLoss, scan.StopLoss);
    }

    static int LotsToConcentrationTarget(
        Position position, decimal totalEquity, decimal targetWeight)
    {
        if (position.Lots <= 1 || position.LastPrice <= 0 ||
            totalEquity <= 0) return 0;
        var excess = position.MarketValue -
            totalEquity * targetWeight;
        if (excess <= 0) return 0;
        var lots = (int)Math.Ceiling(
            excess / (position.LastPrice * 100m));
        // REDUCE must leave at least one lot. Selling the final lot is a
        // different decision and should be labelled SELL_ALL.
        return Math.Clamp(lots, 1, position.Lots - 1);
    }

    static string BuildReason(
        ScanResult? scan, int technicalScore, int eventAdjustment,
        int combinedScore, decimal profitLossPercent, decimal weight)
    {
        if (scan is null)
            return Loc.T(
                "Belum ada skor terbaru untuk posisi ini; keputusan agresif ditahan.",
                "There is no recent score for this position, so aggressive action is withheld.");
        return Loc.T(
            $"Teknikal {technicalScore}/100, penyesuaian isu {eventAdjustment:+#;-#;0}, " +
            $"skor gabungan {combinedScore}/100, P/L {profitLossPercent:+0.0;-0.0;0.0}%, " +
            $"bobot {weight:P0}.",
            $"Technical {technicalScore}/100, event adjustment {eventAdjustment:+#;-#;0}, " +
            $"combined score {combinedScore}/100, P/L {profitLossPercent:+0.0;-0.0;0.0}%, " +
            $"weight {weight:P0}.");
    }

    static int ConfidenceScore(
        string actionCode, ScanResult? scan, int score, decimal weight,
        decimal effectiveStop)
    {
        if (scan is null) return 35;
        return actionCode switch
        {
            "SELL_ALL" when effectiveStop > 0 &&
                            scan.LastPrice <= effectiveStop => 92,
            "SELL_ALL" => Math.Clamp(100 - score, 70, 90),
            "REDUCE" when weight > 0.30m => 85,
            "REDUCE" => Math.Clamp(100 - score, 65, 85),
            "TAKE_PROFIT" => 88,
            "ADD" or "AVERAGE_DOWN" => Math.Clamp(score, 55, 90),
            "WATCH_NO_ADD" => 55,
            _ => Math.Clamp(score, 35, 75)
        };
    }

    static string RiskAction(
        string actionCode, int actionLots, int suggestedLots,
        Position position, ScanResult? scan, decimal trailing,
        decimal effectiveStop)
    {
        var remaining = Math.Max(0, position.Lots - actionLots);
        return actionCode switch
        {
            "SELL_ALL" => Loc.T(
                $"Jual seluruh {actionLots} lot; jangan menunggu trailing stop.",
                $"Sell all {actionLots} lots; do not wait for a trailing stop."),
            "TAKE_PROFIT" when remaining == 0 => Loc.T(
                $"Jual seluruh {actionLots} lot pada target.",
                $"Sell all {actionLots} lots at the target."),
            "TAKE_PROFIT" when trailing > 0 => Loc.T(
                $"Jual {actionLots} lot. Sisa {remaining} lot dilindungi trailing stop {trailing:N1}%.",
                $"Sell {actionLots} lots. Protect the remaining {remaining} lots with a {trailing:N1}% trailing stop."),
            "TAKE_PROFIT" => Loc.T(
                $"Jual {actionLots} lot. Atur stop tetap untuk sisa {remaining} lot sebelum melanjutkan.",
                $"Sell {actionLots} lots. Set a fixed stop for the remaining {remaining} lots before continuing."),
            "REDUCE" => Loc.T(
                $"Kurangi {actionLots} lot. Sisa {remaining} lot memakai stop loss tetap.",
                $"Reduce by {actionLots} lots. Keep a fixed stop loss on the remaining {remaining} lots."),
            "ADD" or "AVERAGE_DOWN" => Loc.T(
                $"Tambahkan tepat {suggestedLots} lot hanya pada limit yang ditampilkan.",
                $"Add exactly {suggestedLots} lots only at the displayed limit."),
            _ when effectiveStop <= 0 => Loc.T(
                "Belum ada level valid; jangan menambah sebelum scan lengkap.",
                "There is no valid level yet; do not add before a complete scan."),
            _ when position.ProfitLossPercent >= 5 && trailing > 0 => Loc.T(
                $"Gunakan trailing stop {trailing:N1}% untuk mengunci profit; stop tidak boleh diturunkan.",
                $"Use a {trailing:N1}% trailing stop to protect profit; never lower the stop."),
            _ => Loc.T(
                $"Gunakan stop loss tetap di Rp {effectiveStop:N0}; trailing stop belum perlu.",
                $"Use a fixed stop loss at Rp {effectiveStop:N0}; a trailing stop is not needed yet.")
        };
    }

    static decimal TrailingPercent(
        string actionCode, Position position, ScanResult? scan,
        decimal effectiveStop)
    {
        if (scan is null || scan.LastPrice <= 0 ||
            effectiveStop <= 0) return 0;
        if (actionCode != "TAKE_PROFIT" &&
            position.ProfitLossPercent < 5) return 0;
        var volatilityDistance =
            (scan.LastPrice - effectiveStop) /
            scan.LastPrice * 100;
        return Math.Clamp(
            decimal.Round(volatilityDistance, 1), 1.5m, 8m);
    }

    static string TakeProfitAction(
        string actionCode, int actionLots,
        Position position, ScanResult? scan,
        decimal primaryTarget)
    {
        if (primaryTarget <= 0)
            return Loc.T("Target belum valid.", "The target is not valid yet.");
        var remaining = Math.Max(0, position.Lots - actionLots);
        var secondaryTarget = scan is not null &&
                              scan.Target2 > primaryTarget
            ? scan.Target2
            : 0;
        return actionCode switch
        {
            "TAKE_PROFIT" when remaining > 0 &&
                               secondaryTarget > 0 => Loc.T(
                $"Jual {actionLots} lot tepat di Rp {primaryTarget:N0}; target {remaining} lot sisanya Rp {secondaryTarget:N0}.",
                $"Sell {actionLots} lots at exactly Rp {primaryTarget:N0}; target Rp {secondaryTarget:N0} for the remaining {remaining} lots."),
            "TAKE_PROFIT" => Loc.T(
                $"Jual {actionLots} lot tepat di Rp {primaryTarget:N0}.",
                $"Sell {actionLots} lots at exactly Rp {primaryTarget:N0}."),
            "SELL_ALL" => Loc.T(
                "Target profit dibatalkan karena skenario cut loss aktif.",
                "The profit target is cancelled because the cut-loss scenario is active."),
            _ when secondaryTarget > 0 => Loc.T(
                $"Rencana target: jual {HalfLots(position.Lots)} lot di Rp {primaryTarget:N0}; target sisa Rp {secondaryTarget:N0}.",
                $"Target plan: sell {HalfLots(position.Lots)} lots at Rp {primaryTarget:N0}; target Rp {secondaryTarget:N0} for the rest."),
            _ => Loc.T(
                $"Rencana target: jual {HalfLots(position.Lots)} lot di Rp {primaryTarget:N0}.",
                $"Target plan: sell {HalfLots(position.Lots)} lots at Rp {primaryTarget:N0}.")
        };
    }

    static string Invalidation(
        string actionCode, ScanResult? scan, decimal effectiveStop)
    {
        if (scan is null)
            return Loc.T(
                "Jalankan scan lengkap sebelum bertindak.",
                "Run a complete scan before taking action.");
        return Loc.IsBuyAction(actionCode)
            ? Loc.T(
                $"Batalkan order jika harga opening di atas Rp {scan.EntryHigh:N0} atau harga menyentuh stop Rp {effectiveStop:N0}. Jangan menaikkan limit.",
                $"Cancel the order if the opening price is above Rp {scan.EntryHigh:N0} or price reaches the Rp {effectiveStop:N0} stop. Do not raise the limit.")
            : Loc.T(
                $"Keputusan dievaluasi ulang setelah snapshot baru; stop tetap Rp {effectiveStop:N0} dan tidak boleh diperlebar.",
                $"Re-evaluate after a new snapshot; the stop remains Rp {effectiveStop:N0} and must not be widened.");
    }
}
