using StockMate.Models;
using StockMate.Services;
using StockMate.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace StockMate.Pages;

public sealed class PositionDetailPage : ContentPage
{
    readonly AppDataService _data;
    readonly PortfolioDecisionService _decisions;
    readonly string _symbol;
    readonly VerticalStackLayout _root = UiKit.PageStack();

    public PositionDetailPage(
        AppDataService data, PortfolioDecisionService decisions, string symbol)
    {
        _data = data;
        _decisions = decisions;
        _symbol = symbol;
        Title = symbol;
        BackgroundColor = UiKit.Navy;
        Content = new ScrollView { Content = _root };
        Appearing += async (_, _) =>
        {
            await _decisions.RebuildAsync();
            Render();
        };
    }

    void Render()
    {
        _data.RebuildPositions();
        _root.Children.Clear();
        var position = _data.State.Positions.FirstOrDefault(x =>
            x.Symbol.Equals(_symbol, StringComparison.OrdinalIgnoreCase));
        if (position is null)
        {
            RenderScannerDetail();
            return;
        }

        var decision = _data.State.PortfolioDecisions.FirstOrDefault(x =>
            x.Symbol.Equals(_symbol, StringComparison.OrdinalIgnoreCase));
        _root.Children.Add(UiKit.Heading(this, position.Symbol, position.Symbol,
            "Halaman ini memisahkan ringkasan posisi, keputusan, rencana risiko, dan kondisi pembatalan rekomendasi.",
            "This page separates position summary, decision, risk plan, and recommendation invalidation."));

        _root.Children.Add(UiKit.ExpandableCard(
            Loc.T("Ringkasan posisi", "Position summary"),
            $"{position.Lots} lot · Rp {position.MarketValue:N0}",
            new VerticalStackLayout
        {
            Spacing = 7,
            Children =
            {
                MetricLine("Harga rata-rata", $"Rp {position.AveragePrice:N2}"),
                MetricLine("Harga pasar", $"Rp {position.LastPrice:N0}"),
                MetricLine("Nilai posisi", $"Rp {position.MarketValue:N0}"),
                MetricLine("Modal posisi", $"Rp {position.Cost:N0}"),
                MetricLine("Unrealized P/L",
                    $"{(position.ProfitLoss >= 0 ? "+" : "")}Rp {position.ProfitLoss:N0} " +
                    $"({position.ProfitLossPercent:+0.00;-0.00;0.00}%)",
                    position.ProfitLoss >= 0 ? UiKit.Green : UiKit.Red)
            }
        }, $"{position.ProfitLossPercent:+0.00;-0.00;0.00}%",
            position.ProfitLoss >= 0 ? UiKit.Green : UiKit.Red,
            initiallyExpanded: true));

        _root.Children.Add(UiKit.SectionHeading(this, "Keputusan", "Decision",
            "Keputusan memakai posisi, harga snapshot, kas, risk/reward, dan strategi aktif.",
            "The decision uses the position, snapshot price, cash, risk/reward and active strategy."));
        if (decision is null)
        {
            _root.Children.Add(UiKit.EmptyState("⌁", "Keputusan belum tersedia",
                "Harga dan untung/rugi tetap sudah diperbarui saat simbol berhasil. Rekomendasi lengkap dibuat setelah analisis selesai."));
        }
        else
        {
            var actionColor = decision.Action.Contains("SELL") ||
                              decision.Action.Contains("REDUCE")
                ? UiKit.Red
                : decision.Action.Contains("ADD") ||
                  decision.Action.Contains("AVERAGE")
                    ? UiKit.Green : UiKit.Blue;
            _root.Children.Add(UiKit.ExpandableCard(
                decision.Action,
                $"{Loc.T("Keyakinan", "Confidence")} {decision.Confidence}",
                new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    UiKit.Sub(decision.Reason),
                    UiKit.Sub(decision.EventSummary),
                    MetricLine("Skor teknikal", $"{decision.TechnicalScore}/100"),
                    MetricLine("Penyesuaian isu", $"{decision.EventAdjustment:+#;-#;0}"),
                    MetricLine("Skor gabungan", $"{decision.Score}/100"),
                    MetricLine("Area tambah", $"Rp {decision.EntryLow:N0}–{decision.EntryHigh:N0}"),
                    MetricLine("Batas harga beli", $"Rp {decision.MaxBuyPrice:N0}"),
                    MetricLine("Jumlah tambah", decision.SuggestedLots > 0
                        ? $"{decision.SuggestedLots} lot"
                        : "0 lot — jangan eksekusi"),
                    MetricLine("Stop loss", $"Rp {decision.StopLoss:N0}"),
                    MetricLine("Target utama", $"Rp {decision.Target:N0}")
                }
            }, decision.Confidence, actionColor, initiallyExpanded: true));

            var riskStack = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    UiKit.Sub(decision.RiskAction),
                    UiKit.Sub(decision.TakeProfitAction)
                }
            };
            if (decision.TrailingStopPercent > 0)
                riskStack.Children.Add(new Label
                {
                    Text = $"Trailing stop yang disarankan: {decision.TrailingStopPercent:N1}%",
                    TextColor = UiKit.Green,
                    FontAttributes = FontAttributes.Bold
                });
            else
                riskStack.Children.Add(UiKit.Sub(
                    "Trailing stop belum disarankan pada kondisi harga saat ini."));
            _root.Children.Add(UiKit.ExpandableCard(
                Loc.T("Rencana risiko & eksekusi", "Risk & execution plan"),
                decision.TrailingStopPercent > 0
                    ? $"Trailing {decision.TrailingStopPercent:N1}%"
                    : Loc.T("Buka untuk melihat rencana", "Open to view the plan"),
                riskStack));
            _root.Children.Add(UiKit.ExpandableCard(
                Loc.T("Kapan keputusan berubah", "When the decision changes"),
                Loc.T("Kondisi pembatalan rekomendasi", "Recommendation invalidation"),
                UiKit.Sub(decision.Invalidation)));
        }

        var buy = UiKit.Primary("Buy / Tambah posisi");
        var sell = UiKit.Primary("Sell / Kurangi posisi");
        sell.BackgroundColor = UiKit.Card;
        var risk = UiKit.Tertiary(Loc.T("Atur stop loss & take profit", "Set stop loss & take profit"));
        buy.Clicked += async (_, _) => await RecordAsync("BUY");
        sell.Clicked += async (_, _) => await RecordAsync("SELL");
        risk.Clicked += async (_, _) => await SetRiskAsync(position);
        var buttons = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)],
            ColumnSpacing = 10
        };
        buttons.Add(buy, 0);
        buttons.Add(sell, 1);
        _root.Children.Add(buttons);
        _root.Children.Add(risk);
    }

    void RenderScannerDetail()
    {
        var scan = _data.State.LastScan.FirstOrDefault(x =>
            x.Symbol.Equals(_symbol, StringComparison.OrdinalIgnoreCase));
        _root.Children.Add(UiKit.Heading(this, _symbol, _symbol,
            "Detail peluang dari snapshot terakhir. Saham ini belum menjadi posisi aktif.",
            "Opportunity detail from the latest snapshot. This stock is not an active position."));
        if (scan is null)
        {
            _root.Children.Add(UiKit.EmptyState("◇", "Detail belum tersedia",
                "Jalankan analisis snapshot terbaru untuk membuat keputusan saham ini."));
            return;
        }

        var actionColor = scan.Verdict.Contains("BUY") ? UiKit.Green : UiKit.Blue;
        _root.Children.Add(UiKit.ExpandableCard(
            scan.Verdict,
            scan.SuggestedLots > 0
                ? $"Beli ideal Rp {scan.EntryLow:N0}–{scan.EntryHigh:N0} · {scan.SuggestedLots} lot"
                : $"Pantau Rp {scan.EntryLow:N0}–{scan.EntryHigh:N0}",
            new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    MetricLine("Harga snapshot", $"Rp {scan.LastPrice:N0}"),
                    MetricLine("Batas maksimal beli", $"Rp {scan.MaxBuyPrice:N0}"),
                    MetricLine("Dana dialokasikan", $"Rp {scan.AllocatedCash:N0}"),
                    MetricLine("Stop loss", $"Rp {scan.StopLoss:N0}"),
                    MetricLine("Target 1", $"Rp {scan.Target1:N0}"),
                    MetricLine("Target 2", $"Rp {scan.Target2:N0}"),
                    MetricLine("Risk/reward", $"{scan.RiskReward:N2}x"),
                    UiKit.Caption($"{scan.DataSession} · data {scan.DataTime:dd MMM yyyy HH:mm}")
                }
            }, scan.Verdict, actionColor, initiallyExpanded: true));
        _root.Children.Add(UiKit.ExpandableCard(
            "Alasan keputusan",
            "Fakta yang mendukung setup",
            UiKit.Sub(scan.Reasons)));
        _root.Children.Add(UiKit.ExpandableCard(
            "Risiko & pembatalan",
            $"Jangan beli di atas Rp {scan.MaxBuyPrice:N0}",
            UiKit.Sub($"{scan.Risks}\n\nRekomendasi batal jika harga melewati batas beli atau menembus stop loss.")));
        var eventView = App.Services.GetRequiredService<EventIntelligenceService>()
            .Summarize(_symbol);
        _root.Children.Add(UiKit.ExpandableCard(
            "Isu & peristiwa terbaru",
            $"Penyesuaian skor {eventView.Adjustment:+#;-#;0}",
            UiKit.Sub(eventView.Summary)));

        var buy = UiKit.Primary("Catat BUY");
        buy.IsEnabled = scan.SuggestedLots > 0 && scan.Verdict.Contains("BUY");
        buy.Clicked += async (_, _) => await RecordAsync("BUY");
        _root.Children.Add(buy);
    }

    async Task RecordAsync(string side)
    {
        var lotsText = await AppDialog.PromptAsync(this,
            $"{side} {_symbol}", "Jumlah lot yang benar-benar dieksekusi",
            keyboard: Keyboard.Numeric);
        if (string.IsNullOrWhiteSpace(lotsText)) return;
        var priceText = await AppDialog.PromptAsync(this,
            "Harga transaksi", "Harga per lembar", keyboard: Keyboard.Numeric);
        if (string.IsNullOrWhiteSpace(priceText)) return;
        var note = await AppDialog.PromptAsync(this, "Catatan", "Alasan transaksi (opsional)") ?? "";
        if (!int.TryParse(lotsText, out var lots) ||
            !decimal.TryParse(priceText, out var price))
        {
            await AppDialog.ShowAsync(this, "Data tidak valid", "Lot dan harga harus berupa angka.");
            return;
        }
        var result = await _data.AddTransactionAsync(_symbol, side, lots, price, note);
        await AppDialog.ShowAsync(this,
            result.Ok ? "Transaksi tersimpan" : "Tidak dapat disimpan",
            result.Message, danger: !result.Ok);
        if (result.Ok)
        {
            await _decisions.RebuildAsync();
            Render();
        }
    }

    async Task SetRiskAsync(Position position)
    {
        var stopText = await AppDialog.PromptAsync(this,
            "Stop loss", "Isi 0 untuk menghapus",
            position.StopLoss.ToString("0.##"), Keyboard.Numeric);
        if (stopText is null) return;
        var targetText = await AppDialog.PromptAsync(this,
            "Take profit", "Isi 0 untuk menghapus",
            position.TakeProfit.ToString("0.##"), Keyboard.Numeric);
        if (targetText is null) return;
        if (!decimal.TryParse(stopText, out var stop) ||
            !decimal.TryParse(targetText, out var target))
        {
            await AppDialog.ShowAsync(this, "Data tidak valid",
                "Stop loss dan take profit harus berupa angka.");
            return;
        }
        position.StopLoss = stop;
        position.TakeProfit = target;
        await _data.SaveAsync();
        Render();
    }

    static Grid MetricLine(string label, string value, Color? valueColor = null)
    {
        var grid = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)]
        };
        grid.Add(UiKit.Sub(label), 0);
        grid.Add(new Label
        {
            Text = value,
            TextColor = valueColor ?? Colors.White,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.End
        }, 1);
        return grid;
    }

}
