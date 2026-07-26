using StockMate.Models;
using StockMate.Services;
using StockMate.Ui;

namespace StockMate.Pages;

public sealed class DashboardPage : ContentPage
{
    readonly AppDataService _data;
    readonly PortfolioDecisionService _decisions;
    readonly EventIntelligenceService _events;
    readonly VerticalStackLayout _root = UiKit.PageStack();
    bool _isPreparing;
    bool _subscribed;

    public DashboardPage(
        AppDataService data, PortfolioDecisionService decisions,
        EventIntelligenceService events)
    {
        _data = data;
        _decisions = decisions;
        _events = events;
        Title = Loc.T("Ringkasan", "Summary");
        BackgroundColor = UiKit.Navy;
        Content = new ScrollView { Content = _root };
        Appearing += OnAppearing;
        Disappearing += OnDisappearing;
    }

    async void OnAppearing(object? sender, EventArgs e)
    {
        if (_isPreparing)
            return;

        if (!_subscribed)
        {
            _data.Changed += Render;
            _subscribed = true;
        }
        _isPreparing = true;
        try
        {
            // Rebuilding the local summary is part of tab rendering. Do not open a
            // modal here: a modal changes Page.Appearing and can race with Shell
            // when the user switches tabs quickly.
            await _decisions.RebuildAsync();
            Render();
        }
        catch (Exception ex)
        {
            await AppDialog.ShowAsync(this, Loc.T("Gagal", "Failed"),
                Loc.T(
                    $"Ringkasan tidak dapat diperbarui: {ex.Message}",
                    $"The summary could not be refreshed: {ex.Message}"),
                danger: true);
        }
        finally
        {
            _isPreparing = false;
        }
    }

    void OnDisappearing(object? sender, EventArgs e)
    {
        if (!_subscribed) return;
        _data.Changed -= Render;
        _subscribed = false;
    }

    void Render()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _root.Children.Clear();
            var invested = _data.State.Positions.Sum(x => x.Cost);
            var market = _data.State.Positions.Sum(x => x.MarketValue);
            var unrealized = market - invested;
            var equity = market + _data.State.Cash;
            var realized = _data.GetRealizedSummary();

            _root.Children.Add(UiKit.Heading(this,
                "Ringkasan", "Summary",
                "Dashboard hanya menampilkan kondisi dan tindakan terpenting. Ketuk kartu untuk membuka rincian perhitungan.",
                "The dashboard shows only the most important status and actions. Tap a card to open calculation details."));

            _root.Children.Add(HeroCard(equity, invested, unrealized));

            var two = new Grid
            {
                ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)],
                ColumnSpacing = 10
            };
            two.Add(UiKit.Metric(Loc.T("Kas tersedia", "Available cash"),
                $"Rp {_data.State.Cash:N0}", _data.State.Cash < 0 ? UiKit.Red : Colors.White), 0);
            two.Add(UiKit.Metric(Loc.T("Realized", "Realized"),
                SignedRupiah(realized.DisplayValue),
                realized.DisplayValue >= 0 ? UiKit.Green : UiKit.Red), 1);
            _root.Children.Add(two);

            if (_data.State.Cash < 0)
                _root.Children.Add(UiKit.ExpandableCard(
                    Loc.T("Kas perlu direkonsiliasi", "Cash needs reconciliation"),
                    Loc.T("Kas negatif tidak dipakai untuk rekomendasi beli.", "Negative cash is excluded from buy recommendations."),
                    UiKit.Sub(Loc.T(
                        "Nilai beli pada history lebih besar daripada saldo awal yang tersimpan. Jalankan Sync Up dengan saldo broker terbaru.",
                        "History purchases exceed the stored opening balance. Run Sync Up with the latest broker cash.")),
                    Loc.T("PERLU AKSI", "ACTION"), UiKit.Red));

            AddPriorityRecommendations();
            AddRiskControl();
            AddLargestPositions();
        });
    }

    Border HeroCard(decimal equity, decimal invested, decimal unrealized)
    {
        var pct = invested == 0 ? 0 : unrealized / invested * 100;
        var detail = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                MetricLine(Loc.T("Modal saham", "Invested cost"), $"Rp {invested:N0}"),
                MetricLine(Loc.T("Nilai pasar saham", "Stock market value"),
                    $"Rp {invested + unrealized:N0}"),
                MetricLine(Loc.T("Kas", "Cash"), $"Rp {_data.State.Cash:N0}"),
                UiKit.Caption(Loc.T(
                    "Total equity = nilai pasar saham + kas.",
                    "Total equity = stock market value + cash."))
            }
        };
        return UiKit.ExpandableCard(
            Loc.T("Total equity", "Total equity"),
            $"Rp {equity:N0}",
            detail,
            $"{pct:+0.00;-0.00;0.00}%",
            unrealized >= 0 ? UiKit.Green : UiKit.Red);
    }

    void AddPriorityRecommendations()
    {
        _root.Children.Add(UiKit.SectionHeading(this,
            "Tindakan prioritas", "Priority actions",
            "Maksimal lima tindakan terpenting berdasarkan urgensi dan keyakinan. Detail alasan, risiko, stop, dan target ada di dalam tiap kartu.",
            "Up to five important actions ranked by urgency and confidence. Reasoning, risk, stop and target are inside each card."));

        var items = BuildRecommendations()
            .OrderByDescending(x => x.Priority)
            .Take(5)
            .ToList();
        if (items.Count == 0)
        {
            _root.Children.Add(UiKit.EmptyState("⌁",
                Loc.T("Belum ada tindakan", "No action yet"),
                Loc.T("Jalankan scanner untuk memperbarui rekomendasi.", "Run the scanner to refresh recommendations.")));
            return;
        }

        foreach (var item in items)
        {
            var color = ActionColor(item.ActionCode);
            var detail = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    UiKit.Body(item.Detail),
                    UiKit.Sub(item.RiskDetail),
                    MetricLine(Loc.T("Stop loss", "Stop loss"), $"Rp {item.Stop:N0}"),
                    MetricLine(Loc.T("Target", "Target"), $"Rp {item.Target:N0}")
                }
            };
            var open = UiKit.Tertiary(Loc.T("Buka detail saham", "Open stock details"));
            open.Clicked += async (_, _) => await Navigation.PushAsync(
                new PositionDetailPage(_data, _decisions, item.Symbol));
            detail.Children.Add(open);
            _root.Children.Add(UiKit.ExpandableCard(
                $"{item.Symbol} · {item.Action}",
                RecommendationSummary(item),
                detail,
                $"{item.Confidence} · {item.ConfidenceScore}/100",
                color));
        }
    }

    IEnumerable<RecommendationItem> BuildRecommendations()
    {
        foreach (var d in _data.State.PortfolioDecisions)
            yield return new RecommendationItem
            {
                Symbol = d.Symbol,
                ActionCode = d.ActionCode,
                Action = Loc.Action(d),
                Confidence = Loc.Confidence(d.Confidence),
                ConfidenceScore = d.ConfidenceScore,
                SuggestedLots = d.SuggestedLots,
                ActionLots = d.ActionLots,
                ExecutionPrice = d.ExecutionPrice,
                EntryLow = d.EntryLow,
                EntryHigh = d.EntryHigh,
                ReferencePrice = d.ReferencePrice,
                Detail = Loc.IsBuyAction(d.ActionCode)
                    ? Loc.T(
                        $"{d.Reason} Tambah {d.SuggestedLots} lot dengan limit Rp {d.EntryHigh:N0}.",
                        $"{d.Reason} Add {d.SuggestedLots} lots with a Rp {d.EntryHigh:N0} limit.")
                    : Loc.IsSellAction(d.ActionCode)
                        ? Loc.T(
                            $"{d.Reason} Jual {d.ActionLots} lot pada Rp {d.ExecutionPrice:N0}.",
                            $"{d.Reason} Sell {d.ActionLots} lots at Rp {d.ExecutionPrice:N0}.")
                        : d.Reason,
                RiskDetail = $"{d.RiskAction} {d.TakeProfitAction}",
                Stop = d.StopLoss, Target = d.Target,
                Priority = ActionPriority(d.ActionCode) +
                           ConfidencePriority(d.Confidence)
            };

        var held = _data.State.Positions.Select(x => x.Symbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var scan in _data.State.LastScan.Where(x =>
                     !held.Contains(x.Symbol) &&
                     x.Verdict == "BUY AREA" &&
                     x.AllocationRank > 0 &&
                     x.SuggestedLots > 0 &&
                     x.Score + EventView(x.Symbol).Adjustment >=
                         _data.State.Strategy.BuyScore &&
                     x.LastPrice <= x.MaxBuyPrice).Take(5))
        {
            var eventView = EventView(scan.Symbol);
            var combinedScore = Math.Clamp(scan.Score + eventView.Adjustment, 0, 100);
            // ScanEngine has already allocated the exact fee-inclusive lot
            // count at the exact limit price. Recalculating it here with the
            // previous close would understate cash and execution risk.
            var lots = scan.SuggestedLots;
            yield return new RecommendationItem
            {
                Symbol = scan.Symbol,
                Action = lots > 0 ? Loc.T("BUKA POSISI", "OPEN POSITION") : Loc.T("PANTAU", "WATCH"),
                ActionCode = lots > 0 ? "OPEN_POSITION" : "WATCH",
                Confidence = combinedScore >= 82 ? Loc.T("TINGGI", "HIGH") : Loc.T("SEDANG", "MEDIUM"),
                ConfidenceScore = combinedScore,
                SuggestedLots = lots,
                EntryLow = scan.EntryLow,
                EntryHigh = scan.EntryHigh,
                ReferencePrice = scan.LastPrice,
                Detail = lots > 0
                    ? Loc.T(
                        $"{lots} lot dengan limit Rp {scan.EntryHigh:N0}; batal jika opening di atas Rp {scan.MaxBuyPrice:N0}. " +
                        $"Teknikal {scan.Score}/100, isu {eventView.Adjustment:+#;-#;0}.",
                        $"{lots} lots with a Rp {scan.EntryHigh:N0} limit; cancel if the opening is above Rp {scan.MaxBuyPrice:N0}. " +
                        $"Technical {scan.Score}/100, events {eventView.Adjustment:+#;-#;0}.")
                    : Loc.T("Setup lolos, tetapi kas belum cukup.", "Setup passed, but cash is insufficient."),
                RiskDetail = $"Risk/reward {scan.RiskReward:N2}. {eventView.Summary}",
                Stop = scan.StopLoss, Target = scan.Target1,
                Priority = (lots > 0 ? 70 : 20) + combinedScore
            };
        }
    }

    static string RecommendationSummary(RecommendationItem item)
    {
        if (item.SuggestedLots > 0)
            return Loc.T(
                $"Limit Rp {item.EntryHigh:N0} · {item.SuggestedLots} lot",
                $"Limit Rp {item.EntryHigh:N0} · {item.SuggestedLots} lots");

        if (Loc.IsSellAction(item.ActionCode))
            return Loc.T(
                $"Jual {item.ActionLots} lot · harga Rp {item.ExecutionPrice:N0}",
                $"Sell {item.ActionLots} lots · price Rp {item.ExecutionPrice:N0}");

        return Loc.T(
            $"Pertahankan di harga acuan Rp {item.ReferencePrice:N0}",
            $"Maintain at reference price Rp {item.ReferencePrice:N0}");
    }

    void AddRiskControl()
    {
        var risk = _data.State.Positions.Where(x => x.StopLoss > 0)
            .Sum(x => Math.Max(0, (x.LastPrice - x.StopLoss) * x.Shares));
        var uncovered = _data.State.Positions.Count(x => x.StopLoss <= 0);
        var detail = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                MetricLine(Loc.T("Batas risiko", "Risk limit"), $"Rp {_data.State.MaxOpenRisk:N0}"),
                MetricLine(Loc.T("Tanpa stop", "Without stop"), uncovered.ToString()),
                new ProgressBar
                {
                    Progress = (double)Math.Min(1, risk / Math.Max(1, _data.State.MaxOpenRisk)),
                    ProgressColor = risk > _data.State.MaxOpenRisk ? UiKit.Red : UiKit.Green
                }
            }
        };
        _root.Children.Add(UiKit.ExpandableCard(
            Loc.T("Kontrol risiko", "Risk control"),
            $"Rp {risk:N0} / Rp {_data.State.MaxOpenRisk:N0}",
            detail,
            uncovered == 0 ? Loc.T("TERLINDUNGI", "COVERED") : $"{uncovered} " + Loc.T("TANPA STOP", "NO STOP"),
            uncovered == 0 ? UiKit.Green : UiKit.Red));
    }

    void AddLargestPositions()
    {
        _root.Children.Add(UiKit.SectionHeading(this,
            "Posisi terbesar", "Largest positions",
            "Empat posisi dengan nilai pasar terbesar. Ketuk kartu untuk melihat lot, modal, nilai pasar, dan P/L.",
            "The four largest positions by market value. Tap a card for lots, cost, market value and P/L."));
        foreach (var p in _data.State.Positions.OrderByDescending(x => x.MarketValue).Take(4))
        {
            var detail = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    MetricLine(Loc.T("Jumlah", "Quantity"), Loc.Lots(p.Lots)),
                    MetricLine(Loc.T("Modal", "Cost"), $"Rp {p.Cost:N0}"),
                    MetricLine(Loc.T("Nilai pasar", "Market value"), $"Rp {p.MarketValue:N0}"),
                    MetricLine("P/L", SignedRupiah(p.ProfitLoss),
                        p.ProfitLoss >= 0 ? UiKit.Green : UiKit.Red)
                }
            };
            var open = UiKit.Tertiary(Loc.T("Buka detail posisi", "Open position details"));
            open.Clicked += async (_, _) => await Navigation.PushAsync(
                new PositionDetailPage(_data, _decisions, p.Symbol));
            detail.Children.Add(open);
            _root.Children.Add(UiKit.ExpandableCard(
                p.Symbol, $"Rp {p.MarketValue:N0}", detail,
                $"{p.ProfitLossPercent:+0.00;-0.00;0.00}%",
                p.ProfitLoss >= 0 ? UiKit.Green : UiKit.Red));
        }
    }

    static Grid MetricLine(string label, string value, Color? color = null)
    {
        var grid = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)],
            ColumnSpacing = 8
        };
        grid.Add(UiKit.Sub(label), 0);
        grid.Add(new Label
        {
            Text = value, TextColor = color ?? Colors.White,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.End
        }, 1);
        return grid;
    }

    static Color ActionColor(string actionCode) =>
        Loc.IsSellAction(actionCode)
            ? UiKit.Red
            : Loc.IsBuyAction(actionCode) || actionCode == "OPEN_POSITION"
                ? UiKit.Green
                : UiKit.Blue;

    static int ActionPriority(string actionCode) => actionCode switch
    {
        "SELL_ALL" => 100,
        "REDUCE" => 90,
        "TAKE_PROFIT" => 80,
        "AVERAGE_DOWN" or "ADD" => 70,
        _ => 30
    };
    static int ConfidencePriority(string confidence) =>
        confidence is "TINGGI" or "HIGH" ? 30 : confidence is "SEDANG" or "MEDIUM" ? 15 : 0;
    static string SignedRupiah(decimal value) => $"{(value >= 0 ? "+" : "")}Rp {value:N0}";

    (int Adjustment, string Summary) EventView(string symbol) =>
        _data.State.AutoEventIntelligence
            ? _events.Summarize(symbol)
            : (0, Loc.T(
                "Analisis isu dinonaktifkan.",
                "Event analysis is disabled."));

    sealed class RecommendationItem
    {
        public string Symbol { get; init; } = "";
        public string ActionCode { get; init; } = "";
        public string Action { get; init; } = "";
        public string Confidence { get; init; } = "";
        public int ConfidenceScore { get; init; }
        public string Detail { get; init; } = "";
        public string RiskDetail { get; init; } = "";
        public int SuggestedLots { get; init; }
        public int ActionLots { get; init; }
        public decimal ExecutionPrice { get; init; }
        public decimal EntryLow { get; init; }
        public decimal EntryHigh { get; init; }
        public decimal ReferencePrice { get; init; }
        public decimal Stop { get; init; }
        public decimal Target { get; init; }
        public int Priority { get; init; }
    }
}
