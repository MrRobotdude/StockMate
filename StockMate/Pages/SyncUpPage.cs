using StockMate.Services;
using StockMate.Ui;

namespace StockMate.Pages;

public sealed class SyncUpPage : ContentPage
{
    readonly AppDataService _data;
    readonly VerticalStackLayout _missingList = new() { Spacing = 12 };
    readonly Dictionary<string, (Entry Lots, Entry Price, Entry Fee, Picker Type)> _basisInputs = [];
    readonly Entry _cash = new()
    {
        Keyboard = Keyboard.Numeric,
        Placeholder = Loc.T("Contoh: 876436", "Example: 876436"),
        TextColor = Colors.White
    };
    readonly Entry _officialRealized = new()
    {
        Keyboard = Keyboard.Numeric,
        Placeholder = Loc.T("Contoh: -56961", "Example: -56961"),
        TextColor = Colors.White
    };
    readonly Label _status = UiKit.Sub(
        Loc.T("Lengkapi perolehan saham yang tidak muncul sebagai BUY (misalnya penjatahan IPO), lalu masukkan Trading Balance Stockbit saat ini."));
    bool _saving;

    public SyncUpPage(AppDataService data)
    {
        _data = data;
        Title = "Sync Up";
        BackgroundColor = UiKit.Navy;

        var save = UiKit.Primary(Loc.T("Simpan dan validasi"));
        save.Clicked += async (_, _) => await SaveAsync();

        var root = UiKit.PageStack();
        root.VerticalOptions = LayoutOptions.Center;
        root.Children.Add(UiKit.Heading(this, "Sync Up", "Sync Up",
            "Rekonsiliasi menghubungkan transaksi, perolehan IPO/transfer, kas saat ini, dan realized resmi. Detail bantuan tersedia pada setiap bagian.",
            "Reconciliation connects transactions, IPO/transfer acquisitions, current cash, and official realized P/L. Help is available in each section."));
        root.Children.Add(UiKit.ExpandableCard(
            Loc.T("Status rekonsiliasi", "Reconciliation status"),
            Loc.T("Ketuk untuk melihat petunjuk atau error.", "Tap for instructions or errors."),
            _status));
        BuildMissingBasisCards();
        if (_basisInputs.Count > 0)
        {
            root.Children.Add(UiKit.SectionHeading(this,
                "Perolehan yang hilang", "Missing acquisitions",
                "SELL ditemukan tanpa BUY sebelumnya. Isi penjatahan IPO, transfer masuk, atau sumber perolehan lain agar cost basis dapat dihitung.",
                "A SELL was found without an earlier BUY. Enter IPO allocation, incoming transfer, or another acquisition source to calculate cost basis."));
            root.Children.Add(_missingList);
        }
        root.Children.Add(UiKit.Box(new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                UiKit.SectionTitle("Trading Balance"),
                _cash,
                UiKit.Caption(Loc.T("Masukkan saldo kas, bukan total equity atau nilai saham."))
            }
        }));
        root.Children.Add(UiKit.Box(new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                UiKit.SectionTitle("Realized P/L"),
                _officialRealized,
                UiKit.Caption(Loc.T("Gunakan periode yang sama dengan e-Statement. Tanda minus berarti rugi."))
            }
        }));
        root.Children.Add(save);
        Content = new ScrollView { Content = root };
    }

    async Task SaveAsync()
    {
        if (_saving) return;
        _saving = true;
        var completed = false;
        try
        {
            await UiKit.RunBusyAsync(this,
                Loc.T("Menyimpan dan memvalidasi…", "Saving and validating…"),
                async () =>
                {
                    foreach (var missing in _data.GetMissingCostBasis())
                    {
                        if (!_basisInputs.TryGetValue(
                                missing.Symbol, out var input) ||
                            !int.TryParse(input.Lots.Text, out var lots) ||
                            !decimal.TryParse(input.Price.Text, out var price))
                        {
                            _status.Text = Loc.T(
                                $"{missing.Symbol}: lengkapi lot dan harga perolehan terlebih dahulu.",
                                $"{missing.Symbol}: enter the acquisition lots and price first.");
                            return;
                        }
                        var fee = 0m;
                        if (!string.IsNullOrWhiteSpace(input.Fee.Text) &&
                            !decimal.TryParse(input.Fee.Text, out fee))
                        {
                            _status.Text = Loc.T(
                                $"{missing.Symbol}: biaya perolehan tidak valid.",
                                $"{missing.Symbol}: the acquisition fee is invalid.");
                            return;
                        }
                        var saved =
                            await _data.UpsertExternalAcquisitionAsync(
                                missing, lots, price, fee,
                                input.Type.SelectedItem?.ToString() ?? "IPO");
                        if (!saved.Ok)
                        {
                            _status.Text = saved.Message;
                            return;
                        }
                    }

                    var unresolved = _data.GetMissingCostBasis();
                    if (unresolved.Count > 0)
                    {
                        _status.Text = Loc.T(
                            $"Sync Up belum lengkap: {string.Join(", ", unresolved.Select(x => x.Symbol))} masih tidak memiliki cost basis.",
                            $"Sync Up is incomplete: {string.Join(", ", unresolved.Select(x => x.Symbol))} still has no cost basis.");
                        return;
                    }

                    if (!decimal.TryParse(
                            _cash.Text, out var currentCash) ||
                        currentCash < 0)
                    {
                        _status.Text = Loc.T(
                            "Saldo kas tidak valid. Masukkan Trading Balance Stockbit dalam angka.",
                            "The cash balance is invalid. Enter the numeric Stockbit Trading Balance.");
                        return;
                    }
                    if (!decimal.TryParse(
                            _officialRealized.Text, out var officialRealized))
                    {
                        _status.Text = Loc.T(
                            "Realized P/L resmi tidak valid. Gunakan tanda minus untuk rugi, misalnya -56961.",
                            "The official realized P/L is invalid. Use a minus sign for a loss, for example -56961.");
                        return;
                    }

                    var activeFlows = _data.State.Transactions
                        .Where(x => x.IsActive && x.AffectsCash)
                        .Sum(x => x.NetCashFlow);
                    _data.State.CashOpeningBalance =
                        currentCash - activeFlows;
                    _data.State.CashReconciled = true;
                    _data.State.CashReconciledAt = DateTime.Now;
                    _data.State.OfficialRealizedProfit =
                        officialRealized;
                    _data.State.RealizedReconciledAt = DateTime.Now;
                    _data.RecalculateCash();
                    await _data.SaveAsync();
                    completed = true;
                });

            if (completed && Window is not null)
            {
                var window = Window;
                await MainThread.InvokeOnMainThreadAsync(() =>
                    window.Page = new AppShell());
            }
        }
        catch (Exception ex)
        {
            _status.Text = Loc.T(
                $"Sync Up gagal: {ex.Message}",
                $"Sync Up failed: {ex.Message}");
            await AppDialog.ShowAsync(
                this, Loc.T("Gagal", "Failed"), _status.Text,
                danger: true);
        }
        finally
        {
            _saving = false;
        }
    }

    void BuildMissingBasisCards()
    {
        foreach (var missing in _data.GetMissingCostBasis())
        {
            var lots = new Entry
            {
                Keyboard = Keyboard.Numeric,
                Text = missing.MinimumLots.ToString(),
                Placeholder = Loc.T("Jumlah lot perolehan"),
                TextColor = Colors.White
            };
            var price = new Entry
            {
                Keyboard = Keyboard.Numeric,
                Placeholder = Loc.T("Harga IPO / average perolehan"),
                TextColor = Colors.White
            };
            var fee = new Entry
            {
                Keyboard = Keyboard.Numeric,
                Placeholder = Loc.T("Total biaya perolehan (opsional)"),
                TextColor = Colors.White
            };
            var type = new Picker
            {
                Title = Loc.T("Jenis perolehan"),
                TextColor = Colors.White,
                ItemsSource = new[]
                {
                    "IPO", Loc.T("Transfer masuk"), Loc.T("Perolehan lain")
                },
                SelectedIndex = 0
            };
            _basisInputs[missing.Symbol] = (lots, price, fee, type);
            _missingList.Children.Add(UiKit.ExpandableCard(
                missing.Symbol,
                Loc.T(
                    $"Minimal {missing.MinimumLots} lot · {missing.MissingShares:N0} saham",
                    $"Minimum {missing.MinimumLots} lots · {missing.MissingShares:N0} shares"),
                new VerticalStackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        UiKit.Sub(Loc.T(
                            $"Penjualan pertama tanpa modal: {missing.FirstUncoveredSellDate:dd MMM yyyy}.",
                            $"First sale without cost basis: {missing.FirstUncoveredSellDate:dd MMM yyyy}.")),
                        type, lots, price, fee,
                        UiKit.Caption(Loc.T("Untuk IPO, lot boleh lebih besar dari minimum. Sisanya menjadi posisi aktif."))
                    }
                },
                Loc.T("LENGKAPI", "COMPLETE"), UiKit.Red,
                initiallyExpanded: true));
        }
    }
}
