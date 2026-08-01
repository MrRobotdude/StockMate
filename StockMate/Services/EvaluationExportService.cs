using System.Globalization;
using System.Text;
using StockMate.Models;

namespace StockMate.Services;

public sealed class EvaluationExportService(AppDataService data)
{
    const string SchemaVersion = "stockmate-evaluation-v1";

    public async Task<(string Path, int Rows)> ExportAsync()
    {
        var rows = data.State.ScanHistory
            .SelectMany(run => (run.Predictions ?? [])
                .Select(prediction => (Run: run, Prediction: prediction)))
            .OrderBy(x => x.Prediction.SignalDate)
            .ThenBy(x => x.Prediction.PredictedAt)
            .ThenBy(x => x.Prediction.Symbol)
            .ToList();
        var path = Path.Combine(
            FileSystem.CacheDirectory,
            $"stockmate-evaluation-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", Headers.Select(Csv)));
        foreach (var row in rows)
            builder.AppendLine(string.Join(",", Values(row.Run, row.Prediction)
                .Select(Csv)));
        await File.WriteAllTextAsync(
            path, builder.ToString(), new UTF8Encoding(true));
        return (path, rows.Count);
    }

    static readonly string[] Headers =
    [
        "export_schema", "exported_at", "app_version", "strategy_version",
        "training_status", "session_key", "predicted_at", "signal_date",
        "data_session", "symbol", "verdict", "primary_setup",
        "research_status", "technical_score", "event_adjustment",
        "combined_score", "market_regime", "market_return_20_pct",
        "market_breadth_20_pct", "signal_open", "signal_high", "signal_low",
        "signal_close", "limit_price", "lots", "stop", "target",
        "risk_reward", "reasons", "risks", "event_summary",
        "next_trading_date", "next_open", "next_high", "next_low",
        "next_close", "next_day_status", "next_day_note",
        "entry_filled_at", "filled_price", "next_return_net_pct",
        "close_vs_signal_pct", "maximum_gain_net_pct",
        "maximum_loss_net_pct", "swing_outcome", "evaluated_at",
        "evaluation_price", "swing_return_net_pct", "maximum_holding_days"
    ];

    IEnumerable<object?> Values(ScanRun run, PredictionRecord p)
    {
        yield return SchemaVersion;
        yield return DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
        yield return $"{AppInfo.Current.VersionString}+{AppInfo.Current.BuildString}";
        yield return run.StrategyVersion;
        yield return data.State.Strategy.Training?.Status ?? "RULE_BASED_UNVALIDATED";
        yield return p.SessionKey;
        yield return Iso(p.PredictedAt);
        yield return IsoDate(p.SignalDate);
        yield return p.DataSession;
        yield return p.Symbol;
        yield return p.Verdict;
        yield return p.PrimarySetup;
        yield return p.ResearchStatus;
        yield return p.TechnicalScore;
        yield return p.EventAdjustment;
        yield return p.Score;
        yield return p.MarketRegime;
        yield return p.MarketReturn20Percent;
        yield return p.MarketBreadth20Percent;
        yield return p.SignalOpen;
        yield return p.SignalHigh;
        yield return p.SignalLow;
        yield return p.SignalClose;
        yield return p.StartPrice;
        yield return p.SuggestedLots;
        yield return p.StopLoss;
        yield return p.Target1;
        yield return p.RiskReward;
        yield return p.Reasons;
        yield return p.Risks;
        yield return p.EventSummary;
        yield return IsoDate(p.NextTradingDate);
        yield return p.NextOpen;
        yield return p.NextHigh;
        yield return p.NextLow;
        yield return p.NextClose;
        yield return p.NextDayStatus;
        yield return p.NextDayNoteCode;
        yield return Iso(p.EntryFilledAt);
        yield return p.FilledPrice;
        yield return p.NextDayReturnPercent;
        yield return p.NextDayMarketReturnPercent;
        yield return p.NextDayMaximumGainPercent;
        yield return p.NextDayMaximumLossPercent;
        yield return p.Outcome;
        yield return Iso(p.EvaluatedAt);
        yield return p.EvaluationPrice;
        yield return p.ReturnPercent;
        yield return p.MaximumHoldingDays;
    }

    static string? Iso(DateTime? value) =>
        value?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    static string? IsoDate(DateTime? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Csv(object? value)
    {
        var text = value switch
        {
            null => "",
            decimal number => number.ToString("0.########", CultureInfo.InvariantCulture),
            double number => number.ToString("0.########", CultureInfo.InvariantCulture),
            float number => number.ToString("0.########", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}
