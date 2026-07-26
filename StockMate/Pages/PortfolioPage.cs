using StockMate.Models;
using StockMate.Services;
using StockMate.Ui;

namespace StockMate.Pages;

public sealed class PortfolioPage : ContentPage
{
    readonly AppDataService _data;
    readonly PortfolioDecisionService _decisions;
    readonly VerticalStackLayout _list = new() { Spacing = 10 };
    readonly SearchBar _search = new() { Placeholder = Loc.T("Cari posisi / Search position…") };
    readonly Picker _filter = new() { Title = Loc.T("Semua tindakan") };
    readonly Picker _sort = new() { Title = Loc.T("Urutkan") };
    readonly Label _pageInfo = UiKit.Sub("");
    readonly Button _previous = UiKit.Secondary("← " + Loc.T("Sebelumnya", "Previous"));
    readonly Button _next = UiKit.Secondary(Loc.T("Berikutnya", "Next") + " →");
    const int PageSize = 8;
    int _page = 1;
    bool _isPreparing;

    public PortfolioPage(AppDataService data, PortfolioDecisionService decisions)
    {
        _data = data;
        _decisions = decisions;
        Title = Loc.T("Portofolio", "Portfolio");
        BackgroundColor = UiKit.Navy;
        _filter.ItemsSource = new[]
        {
            Loc.T("Semua", "All"),
            Loc.T("Tambah", "Add"),
            Loc.T("Tahan", "Hold"),
            Loc.T("Kurangi/Jual", "Reduce/Sell"),
            Loc.T("Untung", "Profit"),
            Loc.T("Rugi", "Loss"),
            Loc.T("Belum ada harga", "No price"),
            Loc.T("Stop terancam", "Stop at risk")
        };
        _filter.SelectedIndex = 0;
        _sort.ItemsSource = new[]
        {
            Loc.T("Prioritas tindakan", "Action priority"),
            Loc.T("Nilai terbesar", "Largest value"),
            Loc.T("Modal terbesar", "Largest cost"),
            Loc.T("Untung rupiah terbesar", "Largest profit"),
            Loc.T("Rugi rupiah terbesar", "Largest loss"),
            Loc.T("P/L % terbaik", "Best P/L %"),
            Loc.T("P/L % terburuk", "Worst P/L %"),
            Loc.T("Dekat stop loss", "Closest to stop loss"),
            Loc.T("Kode A–Z", "Symbol A–Z")
        };
        _sort.SelectedIndex = 0;
        _search.TextChanged += (_, _) => { _page = 1; Render(); };
        _filter.SelectedIndexChanged += (_, _) => { _page = 1; Render(); };
        _sort.SelectedIndexChanged += (_, _) => { _page = 1; Render(); };
        _previous.Clicked += (_, _) => { if (_page > 1) { _page--; Render(); } };
        _next.Clicked += (_, _) => { if (_next.IsEnabled) { _page++; Render(); } };

        var buy = UiKit.Primary("+ Buy");
        var sell = UiKit.Primary("− Sell");
        sell.BackgroundColor = UiKit.Card;
        buy.Clicked += async (_, _) => await RecordAsync("BUY");
        sell.Clicked += async (_, _) => await RecordAsync("SELL");
        var actions = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)],
            ColumnSpacing = 10
        };
        actions.Add(buy, 0);
        actions.Add(sell, 1);

        var root = UiKit.PageStack();
        root.Children.Add(UiKit.Heading(this, "Portofolio", "Portfolio",
            "Lot dan average dihitung dari transaksi. Nilai pasar memakai harga snapshot terakhir. Gunakan filter tindakan untuk fokus pada posisi yang perlu keputusan.",
            "Lots and average cost come from transactions. Market value uses the latest snapshot. Use action filters to focus on positions that need a decision."));
        root.Children.Add(actions);
        var filterGrid = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)],
            ColumnSpacing = 8
        };
        filterGrid.Add(_filter, 0);
        filterGrid.Add(_sort, 1);
        root.Children.Add(UiKit.Box(new VerticalStackLayout
        {
            Spacing = 8,
            Children = { _search, filterGrid }
        }));
        root.Children.Add(_list);
        root.Children.Add(UiKit.Pager(_previous, _pageInfo, _next));
        Content = new ScrollView { Content = root };
        Appearing += OnAppearing;
    }

    async void OnAppearing(object? sender, EventArgs e)
    {
        if (_isPreparing)
            return;

        _isPreparing = true;
        try
        {
            // This is a local refresh performed while changing tabs. Keeping it
            // modal-free avoids stacked navigation modals and orphan dim layers.
            await _decisions.RebuildAsync();
            Render();
        }
        catch (Exception ex)
        {
            await AppDialog.ShowAsync(this, Loc.T("Gagal", "Failed"),
                Loc.T(
                    $"Portofolio tidak dapat diperbarui: {ex.Message}",
                    $"The portfolio could not be refreshed: {ex.Message}"),
                danger: true);
        }
        finally
        {
            _isPreparing = false;
        }
    }

    void Render()
    {
        _list.Children.Clear();
        IEnumerable<Position> query = _data.State.Positions;
        if (!string.IsNullOrWhiteSpace(_search.Text))
            query = query.Where(x => x.Symbol.Contains(_search.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        query = _filter.SelectedIndex switch
        {
            1 => query.Where(x => Loc.IsBuyAction(Decision(x)?.ActionCode)),
            2 => query.Where(x => Decision(x)?.ActionCode is
                "HOLD" or "HOLD_NO_ADD" or "WATCH_NO_ADD"),
            3 => query.Where(x => Loc.IsSellAction(Decision(x)?.ActionCode)),
            4 => query.Where(x => x.ProfitLoss >= 0),
            5 => query.Where(x => x.ProfitLoss < 0),
            6 => query.Where(x => x.LastPrice <= 0),
            7 => query.Where(x => x.StopLoss > 0 && x.LastPrice > 0 &&
                                  x.LastPrice <= x.StopLoss * 1.03m),
            _ => query
        };
        query = _sort.SelectedIndex switch
        {
            1 => query.OrderByDescending(x => x.MarketValue),
            2 => query.OrderByDescending(x => x.Cost),
            3 => query.OrderByDescending(x => x.ProfitLoss),
            4 => query.OrderBy(x => x.ProfitLoss),
            5 => query.OrderByDescending(x => x.ProfitLossPercent),
            6 => query.OrderBy(x => x.ProfitLossPercent),
            7 => query.OrderBy(x => x.StopLoss <= 0 || x.LastPrice <= 0
                ? decimal.MaxValue : (x.LastPrice - x.StopLoss) / x.LastPrice),
            8 => query.OrderBy(x => x.Symbol),
            _ => query.OrderByDescending(x => DecisionPriority(Decision(x)))
                      .ThenByDescending(x => Math.Abs(x.ProfitLoss))
        };
        var filtered = query.ToList();
        var totalPages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)PageSize));
        _page = Math.Clamp(_page, 1, totalPages);
        var positions = filtered.Skip((_page - 1) * PageSize).Take(PageSize).ToList();
        _pageInfo.Text = Loc.T(
            $"Halaman {_page}/{totalPages} • {filtered.Count} posisi",
            $"Page {_page}/{totalPages} • {filtered.Count} positions");
        _previous.IsEnabled = _page > 1;
        _next.IsEnabled = _page < totalPages;
        if (positions.Count == 0)
        {
            _list.Children.Add(UiKit.EmptyState("◇", "Portofolio masih kosong",
                "Impor e-Statement atau catat transaksi untuk mulai memantau posisi."));
            return;
        }
        foreach (var p in positions)
        {
            var decision = _data.State.PortfolioDecisions.FirstOrDefault(x => x.Symbol == p.Symbol);
            var details = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    UiKit.Sub(Loc.T(
                        $"{p.Lots} lot • rata-rata Rp {p.AveragePrice:N2} • harga Rp {p.LastPrice:N0}",
                        $"{p.Lots} lots • average Rp {p.AveragePrice:N2} • price Rp {p.LastPrice:N0}")),
                    UiKit.Sub(Loc.T(
                        $"Modal Rp {p.Cost:N0} • nilai pasar Rp {p.MarketValue:N0}",
                        $"Cost Rp {p.Cost:N0} • market value Rp {p.MarketValue:N0}")),
                    UiKit.Sub(p.StopLoss > 0
                        ? $"Stop Rp {p.StopLoss:N0} • target Rp {p.TakeProfit:N0}"
                        : Loc.T("Stop dan target belum diatur.", "Stop and target are not set.")),
                    UiKit.Caption(Loc.T("Ketuk lagi kartu untuk menutup detail.", "Tap the card again to close details."))
                }
            };
            if (decision is not null)
            {
                details.Children.Insert(0, UiKit.Body(PortfolioPlan(decision)));
                details.Children.Add(UiKit.Sub($"{Loc.T("Alasan", "Reason")}: {decision.Reason}"));
                details.Children.Add(UiKit.Sub($"{Loc.T("Risiko", "Risk")}: {decision.RiskAction}"));
            }
            var action = decision is null
                ? Loc.T("TUNGGU DATA", "WAIT FOR DATA")
                : Loc.Action(decision);
            var confidence = decision is null
                ? Loc.T("RENDAH", "LOW")
                : $"{Loc.Confidence(decision.Confidence)} · {decision.ConfidenceScore}/100";
            var box = UiKit.ExpandableCard(
                $"{p.Symbol} · {action}",
                PortfolioSummary(p, decision),
                details,
                confidence,
                DecisionColor(decision?.ActionCode));
            // Detail posisi tetap tersedia melalui kartu setelah ringkasan diperluas.
            var open = UiKit.Tertiary(Loc.T("Buka halaman posisi", "Open position page"));
            open.Clicked += async (_, _) => await Navigation.PushAsync(
                new PositionDetailPage(_data, _decisions, p.Symbol));
            details.Children.Add(open);
            _list.Children.Add(box);
        }
    }

    static string PortfolioSummary(Position position, PortfolioDecision? decision)
    {
        var price = position.LastPrice > 0 ? position.LastPrice : decision?.ReferencePrice ?? 0;
        var lotPlan = decision is null
            ? ""
            : Loc.IsBuyAction(decision.ActionCode)
                ? Loc.T(
                    $" · tambah {decision.SuggestedLots} lot",
                    $" · add {decision.SuggestedLots} lots")
                : Loc.IsSellAction(decision.ActionCode)
                    ? Loc.T(
                        $" · jual {decision.ActionLots} lot",
                        $" · sell {decision.ActionLots} lots")
                    : "";
        return Loc.T(
            $"Harga Rp {price:N0}{lotPlan} · P/L {position.ProfitLossPercent:+0.00;-0.00;0.00}%",
            $"Price Rp {price:N0}{lotPlan} · P/L {position.ProfitLossPercent:+0.00;-0.00;0.00}%");
    }

    static string PortfolioPlan(PortfolioDecision decision)
    {
        if (Loc.IsBuyAction(decision.ActionCode))
            return Loc.T(
                $"Rencana: {Loc.Action(decision)} pada limit Rp {decision.EntryHigh:N0}.",
                $"Plan: {Loc.Action(decision)} at the Rp {decision.EntryHigh:N0} limit.");
        if (Loc.IsSellAction(decision.ActionCode))
            return Loc.T(
                $"Rencana: {Loc.Action(decision)} pada harga Rp {decision.ExecutionPrice:N0}.",
                $"Plan: {Loc.Action(decision)} at Rp {decision.ExecutionPrice:N0}.");
        return Loc.T(
            $"Rencana: {Loc.Action(decision)} pada harga acuan Rp {decision.ReferencePrice:N0}.",
            $"Plan: {Loc.Action(decision)} at reference price Rp {decision.ReferencePrice:N0}.");
    }

    static Color DecisionColor(string? actionCode) =>
        Loc.IsSellAction(actionCode)
            ? UiKit.Red
            : Loc.IsBuyAction(actionCode)
                ? UiKit.Green
                : UiKit.Blue;

    PortfolioDecision? Decision(Position position) =>
        _data.State.PortfolioDecisions.FirstOrDefault(x =>
            x.Symbol.Equals(position.Symbol, StringComparison.OrdinalIgnoreCase));

    static int DecisionPriority(PortfolioDecision? decision)
    {
        if (decision is null) return 0;
        if (Loc.IsSellAction(decision.ActionCode)) return 4;
        if (Loc.IsBuyAction(decision.ActionCode)) return 3;
        if (decision.ActionCode is "HOLD" or "HOLD_NO_ADD") return 1;
        return 2;
    }

    async Task RecordAsync(string side, string? initialSymbol = null)
    {
        try
        {
            var symbol = initialSymbol ?? await AppDialog.SelectSymbolAsync(
                this, _data.State.MarketUniverse);
            if (string.IsNullOrWhiteSpace(symbol)) return;
            var lotsText = await AppDialog.PromptAsync(
                this, "Jumlah lot", "Jumlah yang benar-benar dieksekusi",
                keyboard: Keyboard.Numeric);
            if (string.IsNullOrWhiteSpace(lotsText)) return;
            var priceText = await AppDialog.PromptAsync(
                this, "Harga transaksi", "Harga buy/sell per lembar",
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
                symbol, side, lots, price, note);
            await AppDialog.ShowAsync(this,
                result.Ok ? "Transaksi tersimpan" : "Tidak dapat disimpan",
                result.Message, danger: !result.Ok);
            if (result.Ok)
                await _decisions.RebuildAsync();
            Render();
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

    async Task SetRiskAsync(Position p)
    {
        try
        {
            var stopText = await AppDialog.PromptAsync(
                this, "Stop loss", "Isi 0 untuk menghapus",
                p.StopLoss.ToString("0.##"), Keyboard.Numeric);
            if (stopText is null) return;
            var targetText = await AppDialog.PromptAsync(
                this, "Take profit", "Isi 0 untuk menghapus",
                p.TakeProfit.ToString("0.##"), Keyboard.Numeric);
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
            p.StopLoss = stop;
            p.TakeProfit = target;
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
}
