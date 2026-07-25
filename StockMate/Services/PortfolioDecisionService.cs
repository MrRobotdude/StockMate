using StockMate.Models;

namespace StockMate.Services;

public sealed class PortfolioDecisionService(AppDataService data)
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
        var scans = data.State.LastScan.ToDictionary(x => x.Symbol, StringComparer.OrdinalIgnoreCase);
        var totalEquity = data.State.Cash + data.State.Positions.Sum(x => x.MarketValue);
        var decisions = new List<PortfolioDecision>();

        foreach (var p in data.State.Positions.OrderByDescending(x => x.MarketValue))
        {
            scans.TryGetValue(p.Symbol, out var scan);
            var weight = totalEquity <= 0 ? 0 : p.MarketValue / totalEquity;
            var score = scan?.Score ?? 0;
            var action = "HOLD - JANGAN TAMBAH";
            var lots = 0;
            var reason = scan is null
                ? "Belum ada skor terbaru untuk posisi ini; keputusan agresif ditahan."
                : $"Skor teknikal {score}/100, P/L {p.ProfitLossPercent:+0.0;-0.0;0.0}%, bobot {weight:P0}.";

            if (scan is not null && p.LastPrice <= scan.StopLoss)
                action = "SELL ALL / CUT LOSS";
            else if (scan is not null && score < 55)
                action = p.ProfitLossPercent < -5 ? "SELL ALL / CUT LOSS" : "REDUCE 30-50%";
            else if (scan is not null && p.LastPrice >= scan.Target1)
                action = "TAKE PROFIT 30-50%";
            else if (scan is not null && score >= data.State.Strategy.BuyScore && weight < 0.20m &&
                     p.LastPrice <= scan.MaxBuyPrice)
            {
                action = p.ProfitLossPercent < 0 ? "AVERAGE DOWN BERTAHAP" : "ADD BERTAHAP";
                lots = Math.Max(0, Math.Min(scan.SuggestedLots,
                    (int)Math.Floor(Math.Max(0, data.State.Cash * 0.25m) / Math.Max(1, p.LastPrice * 100))));
                if (lots == 0)
                    action = "PANTAU - JANGAN TAMBAH";
            }
            else if (weight > 0.30m)
                action = "REDUCE / JANGAN TAMBAH";

            decisions.Add(new()
            {
                Symbol = p.Symbol, Action = action, Score = score, SuggestedLots = lots,
                ReferencePrice = p.LastPrice, StopLoss = scan?.StopLoss ?? p.StopLoss,
                EntryLow = scan?.EntryLow ?? p.LastPrice,
                EntryHigh = scan?.EntryHigh ?? p.LastPrice,
                MaxBuyPrice = scan?.MaxBuyPrice ?? p.LastPrice,
                Target = scan?.Target1 ?? p.TakeProfit,
                Confidence = RecommendationConfidence(action, scan, score, weight),
                RiskAction = RiskAction(action, p, scan),
                TrailingStopPercent = TrailingPercent(action, p, scan),
                TakeProfitAction = TakeProfitAction(action, p, scan),
                Reason = reason,
                Invalidation = scan is null
                    ? "Jalankan scan lengkap sebelum bertindak."
                    : $"Batal tambah jika harga > {scan.MaxBuyPrice:N0} atau turun <= {scan.StopLoss:N0}."
            });
        }

        data.State.PortfolioDecisions = decisions;
        _builtForRevision = data.Revision;
        return decisions;
        }
        finally
        {
            _gate.Release();
        }
    }

    static string RecommendationConfidence(
        string action, ScanResult? scan, decimal score, decimal weight)
    {
        if (scan is null) return "RENDAH";
        if (action.Contains("SELL ALL") && (scan.LastPrice <= scan.StopLoss || score < 45))
            return "TINGGI";
        if (action.Contains("REDUCE") && (score < 55 || weight > 0.30m))
            return "TINGGI";
        if (action.Contains("TAKE PROFIT") && scan.LastPrice >= scan.Target1)
            return "TINGGI";
        if ((action.Contains("ADD") || action.Contains("AVERAGE")) &&
            score >= 78 && scan.LastPrice <= scan.MaxBuyPrice)
            return "TINGGI";
        return score >= 65 ? "SEDANG" : "RENDAH";
    }

    static string RiskAction(string action, Position position, ScanResult? scan)
    {
        if (action.Contains("SELL ALL"))
            return "Eksekusi cut loss; jangan menunggu trailing stop.";
        if (action.Contains("TAKE PROFIT"))
            return "Jual sebagian di target, lalu lindungi sisa dengan trailing stop.";
        if (action.Contains("REDUCE"))
            return "Kurangi posisi sekarang; pasang stop loss tetap untuk sisa posisi.";
        if (scan is null || scan.StopLoss <= 0)
            return "Belum ada level valid; jangan menambah sebelum scan lengkap.";
        if (position.ProfitLossPercent >= 5)
            return "Gunakan trailing stop untuk mengunci profit; stop tidak boleh diturunkan.";
        return $"Gunakan stop loss tetap di {scan.StopLoss:N0}; trailing stop belum perlu.";
    }

    static decimal TrailingPercent(string action, Position position, ScanResult? scan)
    {
        if (scan is null || scan.LastPrice <= 0) return 0;
        if (!(action.Contains("TAKE PROFIT") || position.ProfitLossPercent >= 5)) return 0;
        var volatilityDistance = (scan.LastPrice - scan.StopLoss) / scan.LastPrice * 100;
        return Math.Clamp(decimal.Round(volatilityDistance, 1), 1.5m, 8m);
    }

    static string TakeProfitAction(string action, Position position, ScanResult? scan)
    {
        if (scan is null || scan.Target1 <= 0) return "Target belum valid.";
        if (action.Contains("TAKE PROFIT"))
            return $"Take profit 30–50% sekarang/dekat {scan.Target1:N0}; sisanya arahkan ke {scan.Target2:N0}.";
        if (action.Contains("SELL ALL"))
            return "Target profit dibatalkan karena skenario cut loss aktif.";
        return $"Rencana take profit 30–50% di {scan.Target1:N0}; sisa posisi di {scan.Target2:N0}.";
    }
}
