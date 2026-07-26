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
            try
            {
                await _decisions.RebuildAsync();
                Render();
            }
            catch (Exception ex)
            {
                await AppDialog.ShowAsync(this, Loc.T("Gagal", "Failed"),
                    Loc.T(
                        $"Detail posisi tidak dapat diperbarui: {ex.Message}",
                        $"Position details could not be refreshed: {ex.Message}"),
                    danger: true);
            }
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
            $"{Loc.Lots(position.Lots)} · Rp {position.MarketValue:N0}",
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
            var actionColor = Loc.IsSellAction(decision.ActionCode)
                ? UiKit.Red
                : Loc.IsBuyAction(decision.ActionCode)
                    ? UiKit.Green : UiKit.Blue;
            var decisionDetails = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    UiKit.Sub(decision.Reason),
                    UiKit.Sub(decision.EventSummary),
                    MetricLine("Skor teknikal", $"{decision.TechnicalScore}/100"),
                    MetricLine("Penyesuaian isu", $"{decision.EventAdjustment:+#;-#;0}"),
                    MetricLine("Skor gabungan", $"{decision.Score}/100")
                }
            };
            if (Loc.IsBuyAction(decision.ActionCode))
            {
                decisionDetails.Children.Add(MetricLine(
                    "Harga limit order", $"Rp {decision.EntryHigh:N0}"));
                decisionDetails.Children.Add(MetricLine(
                    "Batal jika opening di atas", $"Rp {decision.EntryHigh:N0}"));
                decisionDetails.Children.Add(MetricLine(
                    "Jumlah tambah", Loc.Lots(decision.SuggestedLots)));
            }
            else if (Loc.IsSellAction(decision.ActionCode))
            {
                decisionDetails.Children.Add(MetricLine(
                    Loc.T("Harga order jual", "Sell order price"),
                    $"Rp {decision.ExecutionPrice:N0}"));
                decisionDetails.Children.Add(MetricLine(
                    "Jumlah jual", Loc.Lots(decision.ActionLots)));
            }
            else
            {
                decisionDetails.Children.Add(MetricLine(
                    Loc.T("Harga acuan", "Reference price"),
                    $"Rp {decision.ReferencePrice:N0}"));
            }
            decisionDetails.Children.Add(MetricLine(
                "Stop loss", $"Rp {decision.StopLoss:N0}"));
            decisionDetails.Children.Add(MetricLine(
                "Target utama", $"Rp {decision.Target:N0}"));
            _root.Children.Add(UiKit.ExpandableCard(
                Loc.Action(decision),
                Loc.T(
                    $"Keyakinan {Loc.Confidence(decision.Confidence)} · {decision.ConfidenceScore}/100",
                    $"Confidence {Loc.Confidence(decision.Confidence)} · {decision.ConfidenceScore}/100"),
                decisionDetails,
                $"{decision.ConfidenceScore}/100",
                actionColor,
                initiallyExpanded: true));

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
                    Text = Loc.T(
                        $"Trailing stop yang disarankan: {decision.TrailingStopPercent:N1}%",
                        $"Recommended trailing stop: {decision.TrailingStopPercent:N1}%"),
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

        var buy = UiKit.Primary(Loc.T("Buy / Tambah posisi"));
        var sell = UiKit.Primary(Loc.T("Sell / Kurangi posisi"));
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
            Loc.Verdict(scan.Verdict),
            scan.SuggestedLots > 0
                ? Loc.T(
                    $"Limit Rp {scan.EntryHigh:N0} · {scan.SuggestedLots} lot",
                    $"Limit Rp {scan.EntryHigh:N0} · {scan.SuggestedLots} lots")
                : Loc.T(
                    $"Pantau harga Rp {scan.EntryHigh:N0}",
                    $"Watch price Rp {scan.EntryHigh:N0}"),
            new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    MetricLine("Harga snapshot", $"Rp {scan.LastPrice:N0}"),
                    MetricLine("Harga limit order", $"Rp {scan.EntryHigh:N0}"),
                    MetricLine("Batal jika opening di atas", $"Rp {scan.MaxBuyPrice:N0}"),
                    MetricLine("Dana dialokasikan", $"Rp {scan.AllocatedCash:N0}"),
                    MetricLine("Stop loss", $"Rp {scan.StopLoss:N0}"),
                    MetricLine("Target 1", $"Rp {scan.Target1:N0}"),
                    MetricLine("Target 2", $"Rp {scan.Target2:N0}"),
                    MetricLine("Risk/reward", $"{scan.RiskReward:N2}x"),
                    MetricLine(Loc.T("Skor teknikal", "Technical score"),
                        $"{scan.Score}/100"),
                    MetricLine(Loc.T("Penyesuaian isu", "Event adjustment"),
                        $"{scan.EventAdjustment:+#;-#;0}"),
                    MetricLine(Loc.T("Skor gabungan", "Combined score"),
                        $"{scan.CombinedScore}/100"),
                    UiKit.Caption(Loc.T(
                        $"{scan.DataSession} · data {scan.DataTime:dd MMM yyyy HH:mm}",
                        $"{Loc.Session(scan.DataSession)} · data {scan.DataTime:dd MMM yyyy HH:mm}"))
                }
            }, Loc.Verdict(scan.Verdict), actionColor,
            initiallyExpanded: true));
        _root.Children.Add(UiKit.ExpandableCard(
            "Alasan keputusan",
            "Fakta yang mendukung setup",
            UiKit.Sub(Loc.English && !string.IsNullOrWhiteSpace(scan.ReasonsEn)
                ? scan.ReasonsEn : scan.Reasons)));
        _root.Children.Add(UiKit.ExpandableCard(
            "Risiko & pembatalan",
            Loc.T(
                $"Jangan beli di atas Rp {scan.EntryHigh:N0}",
                $"Do not buy above Rp {scan.EntryHigh:N0}"),
            UiKit.Sub(Loc.T(
                $"{scan.Risks}\n\nRekomendasi batal jika harga melewati batas beli atau menembus stop loss.",
                $"{(string.IsNullOrWhiteSpace(scan.RisksEn) ? scan.Risks : scan.RisksEn)}\n\nThe recommendation is invalid if price exceeds the buy limit or breaks the stop loss."))));
        var eventView = _data.State.AutoEventIntelligence
            ? App.Services.GetRequiredService<EventIntelligenceService>()
                .Summarize(_symbol)
            : (Adjustment: 0, Summary: Loc.T(
                "Analisis isu dinonaktifkan.",
                "Event analysis is disabled."));
        _root.Children.Add(UiKit.ExpandableCard(
            "Isu & peristiwa terbaru",
            Loc.T(
                $"Penyesuaian skor {eventView.Adjustment:+#;-#;0}",
                $"Score adjustment {eventView.Adjustment:+#;-#;0}"),
            UiKit.Sub(eventView.Summary)));

        var buy = UiKit.Primary(Loc.T("Catat BUY"));
        buy.IsEnabled = scan.SuggestedLots > 0 && scan.Verdict.Contains("BUY");
        buy.Clicked += async (_, _) => await RecordAsync("BUY");
        _root.Children.Add(buy);
    }

    async Task RecordAsync(string side)
    {
        try
        {
            var lotsText = await AppDialog.PromptAsync(this,
                $"{side} {_symbol}", "Jumlah lot yang benar-benar dieksekusi",
                keyboard: Keyboard.Numeric);
            if (string.IsNullOrWhiteSpace(lotsText)) return;
            var priceText = await AppDialog.PromptAsync(this,
                "Harga transaksi", "Harga per lembar",
                keyboard: Keyboard.Numeric);
            if (string.IsNullOrWhiteSpace(priceText)) return;
            var note = await AppDialog.PromptAsync(
                this, "Catatan", "Alasan transaksi (opsional)") ?? "";
            if (!int.TryParse(lotsText, out var lots) ||
                !decimal.TryParse(priceText, out var price))
            {
                await AppDialog.ShowAsync(
                    this, "Data tidak valid",
                    "Lot dan harga harus berupa angka.");
                return;
            }
            var result = await _data.AddTransactionAsync(
                _symbol, side, lots, price, note);
            await AppDialog.ShowAsync(this,
                result.Ok ? "Transaksi tersimpan" : "Tidak dapat disimpan",
                result.Message, danger: !result.Ok);
            if (result.Ok)
            {
                await _decisions.RebuildAsync();
                Render();
            }
        }
        catch (Exception ex)
        {
            await AppDialog.ShowAsync(
                this, Loc.T("Tidak dapat disimpan", "Could not save"),
                Loc.T(
                    $"Transaksi tidak dapat disimpan: {ex.Message}",
                    $"The transaction could not be saved: {ex.Message}"),
                danger: true);
        }
    }

    async Task SetRiskAsync(Position position)
    {
        try
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
                !decimal.TryParse(targetText, out var target) ||
                stop < 0 || target < 0)
            {
                await AppDialog.ShowAsync(this, "Data tidak valid",
                    Loc.T(
                        "Stop loss dan take profit harus berupa angka 0 atau lebih.",
                        "Stop loss and take profit must be numbers equal to or above 0."));
                return;
            }
            position.StopLoss = stop;
            position.TakeProfit = target;
            await _data.SaveAsync();
            await _decisions.RebuildAsync();
            Render();
        }
        catch (Exception ex)
        {
            await AppDialog.ShowAsync(
                this, Loc.T("Tidak dapat disimpan", "Could not save"),
                Loc.T(
                    $"Rencana risiko tidak dapat disimpan: {ex.Message}",
                    $"The risk plan could not be saved: {ex.Message}"),
                danger: true);
        }
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
