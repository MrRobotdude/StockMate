using StockMate.Models;
using StockMate.Services;
using StockMate.Ui;

namespace StockMate.Pages;

public sealed class JournalPage : ContentPage
{
    readonly AppDataService _data;
    readonly TransactionHistoryService _history;
    readonly VerticalStackLayout _list = new() { Spacing = 10 };
    readonly Label _importInfo = UiKit.Sub("");
    readonly SearchBar _search = new() { Placeholder = Loc.T("Cari kode / Search symbol…") };
    readonly Picker _side = new() { Title = "BUY / SELL" };
    readonly Picker _source = new() { Title = "Sumber / Source" };
    readonly Picker _period = new() { Title = "Periode / Period" };
    readonly Picker _sort = new() { Title = "Urutkan / Sort" };
    readonly Button _previous = UiKit.Secondary("← " + Loc.T("Sebelumnya", "Previous"));
    readonly Button _next = UiKit.Secondary(Loc.T("Berikutnya", "Next") + " →");
    readonly Label _pageInfo = UiKit.Sub("");
    const int PageSize = 15;
    int _page = 1;

    public JournalPage(AppDataService data, TransactionHistoryService history)
    {
        _data = data; _history = history;
        Title = Loc.T("Transaksi", "Transactions");
        BackgroundColor = UiKit.Navy;
        _side.ItemsSource = new[] { Loc.T("Semua", "All"), "BUY", "SELL" };
        _source.ItemsSource = new[]
        {
            Loc.T("Semua sumber"),
            Loc.T("History broker", "Broker history"),
            Loc.T("Manual", "Manual"),
            Loc.T("Sync Up IPO", "IPO Sync Up"),
            Loc.T("Transfer masuk", "Incoming transfer"),
            Loc.T("Perolehan lain", "Other acquisition"),
            Loc.T("Dikoreksi", "Corrected")
        };
        _period.ItemsSource = new[]
        {
            Loc.T("Semua waktu"), Loc.T("7 hari"), Loc.T("30 hari"),
            Loc.T("Bulan ini"), Loc.T("Tahun ini")
        };
        _sort.ItemsSource = new[]
        {
            Loc.T("Terbaru"), Loc.T("Terlama"), Loc.T("Nilai terbesar"),
            Loc.T("Nilai terkecil"), Loc.T("Kode A–Z"), Loc.T("Fee terbesar")
        };
        foreach (var picker in new[] { _side, _source, _period, _sort }) picker.SelectedIndex = 0;
        _search.TextChanged += (_, _) => ResetRender();
        _side.SelectedIndexChanged += (_, _) => ResetRender();
        _source.SelectedIndexChanged += (_, _) => ResetRender();
        _period.SelectedIndexChanged += (_, _) => ResetRender();
        _sort.SelectedIndexChanged += (_, _) => ResetRender();
        _previous.Clicked += (_, _) => { if (_page > 1) { _page--; Render(); } };
        _next.Clicked += (_, _) => { if (_next.IsEnabled) { _page++; Render(); } };

        var root = UiKit.PageStack();
        root.Children.Add(UiKit.Heading(this, "Riwayat transaksi", "Transaction history",
            "History impor menjadi sumber utama. Transaksi manual yang cocok dinonaktifkan, tetapi tetap tersedia untuk audit. Filter tidak mengubah perhitungan portofolio.",
            "Imported history is the primary source. Matching manual entries are disabled but kept for audit. Filters do not change portfolio calculations."));
        var import = UiKit.Primary(Loc.T("Impor Transaction History", "Import transaction history"));
        import.Clicked += async (_, _) => await ImportAsync();
        root.Children.Add(import);
        root.Children.Add(_importInfo);
        var filters = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)],
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)],
            ColumnSpacing = 8, RowSpacing = 8
        };
        filters.Add(_side, 0, 0); filters.Add(_source, 1, 0);
        filters.Add(_period, 0, 1); filters.Add(_sort, 1, 1);
        root.Children.Add(UiKit.Box(new VerticalStackLayout { Spacing = 8, Children = { _search, filters } }));
        root.Children.Add(_list);
        root.Children.Add(UiKit.Pager(_previous, _pageInfo, _next));
        Content = new ScrollView { Content = root };
        Appearing += (_, _) => Render();
    }

    void ResetRender() { _page = 1; Render(); }

    void Render()
    {
        _list.Children.Clear();
        var realized = _data.GetRealizedSummary();
        var latestImport = _data.State.TransactionImports
            .OrderByDescending(x => x.ImportedAt)
            .FirstOrDefault();
        _importInfo.Text = Loc.T(
            $"{_data.State.TransactionImports.Count} file riwayat • " +
            $"Realized Rp {realized.DisplayValue:N0} • Fee Rp {realized.Fees:N0}" +
            (latestImport is null ? "" :
                $"\nTerakhir: {latestImport.Provider} {latestImport.CoverageStart:dd MMM yyyy}–{latestImport.CoverageEnd:dd MMM yyyy} • BUY Rp{latestImport.ParsedBuyValue:N0} • SELL Rp{latestImport.ParsedSellValue:N0} • tax Rp{latestImport.ParsedSalesTax:N2} • total tervalidasi"),
            $"{_data.State.TransactionImports.Count} history files • " +
            $"Realized Rp {realized.DisplayValue:N0} • Fees Rp {realized.Fees:N0}" +
            (latestImport is null ? "" :
                $"\nLatest: {latestImport.Provider} {latestImport.CoverageStart:dd MMM yyyy}–{latestImport.CoverageEnd:dd MMM yyyy} • BUY Rp{latestImport.ParsedBuyValue:N0} • SELL Rp{latestImport.ParsedSellValue:N0} • tax Rp{latestImport.ParsedSalesTax:N2} • totals validated"));

        IEnumerable<TradeTransaction> query = _data.State.Transactions;
        if (!string.IsNullOrWhiteSpace(_search.Text))
            query = query.Where(x => x.Symbol.Contains(_search.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        var side = _side.SelectedItem?.ToString();
        if (side is "BUY" or "SELL") query = query.Where(x => x.Side == side);
        query = _source.SelectedIndex switch
        {
            1 => query.Where(x => x.Source == "HISTORY" && x.IsActive),
            2 => query.Where(x => x.Source == "MANUAL" && x.IsActive),
            3 => query.Where(x => x.Source == "IPO_SYNC" && x.IsActive),
            4 => query.Where(x => x.Source == "TRANSFER_SYNC" && x.IsActive),
            5 => query.Where(x => x.Source == "OTHER_SYNC" && x.IsActive),
            6 => query.Where(x => !x.IsActive),
            _ => query.Where(x => x.IsActive)
        };
        var today = DateTime.Today;
        query = _period.SelectedIndex switch
        {
            1 => query.Where(x => x.Time >= today.AddDays(-7)),
            2 => query.Where(x => x.Time >= today.AddDays(-30)),
            3 => query.Where(x => x.Time >= new DateTime(today.Year, today.Month, 1)),
            4 => query.Where(x => x.Time >= new DateTime(today.Year, 1, 1)),
            _ => query
        };
        query = _sort.SelectedIndex switch
        {
            1 => query.OrderBy(x => x.Time),
            2 => query.OrderByDescending(x => x.GrossValue),
            3 => query.OrderBy(x => x.GrossValue),
            4 => query.OrderBy(x => x.Symbol).ThenByDescending(x => x.Time),
            5 => query.OrderByDescending(x => x.Fee),
            _ => query.OrderByDescending(x => x.Time)
        };
        var filtered = query.ToList();
        var pages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)PageSize));
        _page = Math.Clamp(_page, 1, pages);
        _pageInfo.Text = $"{Loc.T("Halaman", "Page")} {_page}/{pages} • {filtered.Count}";
        _previous.IsEnabled = _page > 1; _next.IsEnabled = _page < pages;
        foreach (var tx in filtered.Skip((_page - 1) * PageSize).Take(PageSize))
            _list.Children.Add(UiKit.ExpandableCard(
                $"{tx.Side} · {tx.Symbol}",
                $"{Loc.Lots(tx.Lots)} · Rp {tx.GrossValue:N0}",
                new VerticalStackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        UiKit.Sub(Loc.T(
                            $"Rp {tx.Price:N0} / lembar",
                            $"Rp {tx.Price:N0} / share")),
                        UiKit.Sub($"Fee Rp {tx.Fee:N0}"),
                        UiKit.Sub($"{tx.Time:dd MMM yyyy HH:mm}"),
                        UiKit.Sub($"{Loc.T("Sumber", "Source")}: {SourceLabel(tx.Source)}"),
                        UiKit.Sub($"{Loc.T("Status", "Status")}: {(tx.IsActive ? Loc.T("Aktif","Active") : Loc.T("Dikoreksi","Corrected"))}"),
                        UiKit.Sub(string.IsNullOrWhiteSpace(tx.Note)
                            ? "—"
                            : LocalizeAutomaticNote(tx.Note))
                    }
                },
                tx.IsActive ? Loc.T("AKTIF", "ACTIVE") : Loc.T("DIKOREKSI", "CORRECTED"),
                tx.Side == "BUY" ? UiKit.Green : UiKit.Red));
        if (filtered.Count == 0)
            _list.Children.Add(UiKit.EmptyState("↕", Loc.T("Tidak ada transaksi", "No transactions"),
                Loc.T("Ubah filter atau impor history.", "Change the filters or import history.")));
    }

    async Task ImportAsync()
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = Loc.T("Pilih e-Statement PDF atau CSV/TSV", "Choose e-Statement PDF or CSV/TSV") });
            if (file is null) return;
            var result = await _history.ImportAsync(file);
            await AppDialog.ShowAsync(this, result.Ok ? Loc.T("History diterapkan", "History applied") : Loc.T("Impor gagal", "Import failed"), result.Message, danger: !result.Ok);
            Render();
        }
        catch (Exception ex) { await AppDialog.ShowAsync(this, Loc.T("Impor gagal", "Import failed"), ex.Message, danger: true); }
    }

    static string SourceLabel(string source) => source switch
    {
        "HISTORY" => Loc.T("History broker", "Broker history"),
        "MANUAL" => Loc.T("Manual", "Manual"),
        "IPO_SYNC" => Loc.T("Sync Up IPO", "IPO Sync Up"),
        "TRANSFER_SYNC" => Loc.T("Transfer masuk", "Incoming transfer"),
        "OTHER_SYNC" => Loc.T("Perolehan lain", "Other acquisition"),
        _ => source
    };

    static string LocalizeAutomaticNote(string note)
    {
        if (note.Equals("Impor Transaction History",
                StringComparison.OrdinalIgnoreCase))
            return Loc.T(
                "Impor Transaction History",
                "Imported Transaction History");
        if (Loc.English &&
            note.StartsWith("Impor e-Statement Stockbit;",
                StringComparison.OrdinalIgnoreCase))
            return note
                .Replace("Impor e-Statement Stockbit",
                    "Imported Stockbit e-Statement",
                    StringComparison.OrdinalIgnoreCase)
                .Replace("sales tax tercatat",
                    "recorded sales tax",
                    StringComparison.OrdinalIgnoreCase)
                .Replace("total fee diestimasi dari tarif broker",
                    "total fees estimated from broker rates",
                    StringComparison.OrdinalIgnoreCase);
        if (Loc.English)
            return note.Replace(
                "cost basis dilengkapi saat Sync Up",
                "cost basis completed during Sync Up",
                StringComparison.OrdinalIgnoreCase);
        return note.Replace(
            "cost basis completed during Sync Up",
            "cost basis dilengkapi saat Sync Up",
            StringComparison.OrdinalIgnoreCase);
    }
}
