using StockMate.Services;
using StockMate.Ui;

namespace StockMate.Pages;

public sealed class ImportRequiredPage : ContentPage
{
    readonly AppDataService _data;
    readonly TransactionHistoryService _history;
    readonly Label _status = UiKit.Sub(
        "StockMate tidak menggunakan portofolio contoh. Impor e-Statement Stockbit untuk mulai membangun posisi dan riwayat transaksi.");
    readonly Button _import = UiKit.Primary("Impor e-Statement Stockbit");

    public ImportRequiredPage(AppDataService data, TransactionHistoryService history)
    {
        _data = data;
        _history = history;
        Title = "Mulai";
        BackgroundColor = UiKit.Navy;

        _import.Clicked += async (_, _) => await ImportAsync();

        var root = UiKit.PageStack();
        root.VerticalOptions = LayoutOptions.Center;
        root.Children.Add(new Image
        {
            Source = "stockmate_logo.svg",
            WidthRequest = 112,
            HeightRequest = 112,
            HorizontalOptions = LayoutOptions.Center
        });
        root.Children.Add(UiKit.Title("Masukkan data portofolio kamu"));
        root.Children.Add(UiKit.ExpandableCard(
            Loc.T("Data awal", "Initial data"),
            Loc.T("Impor e-Statement untuk memulai", "Import an e-Statement to begin"),
            new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    _status,
                    UiKit.Sub("Format: e-Statement PDF Stockbit atau Transaction History CSV/TSV.")
                }
            },
            "?", UiKit.Blue));
        root.Children.Add(_import);
        Content = new ScrollView { Content = root };
    }

    async Task ImportAsync()
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Pilih e-Statement PDF Stockbit"
            });
            if (file is null) return;

            _import.IsEnabled = false;
            _status.Text = "Membaca dan memvalidasi e-Statement…";
            var result = await _history.ImportAsync(file);
            if (!result.Ok)
            {
                _status.Text = result.Message;
                await AppDialog.ShowAsync(this, "Impor gagal", result.Message, danger:true);
                return;
            }

            _data.RebuildPositions();
            _status.Text = result.Message;
            await AppDialog.ShowAsync(this, "Impor berhasil",
                $"{result.Message}\n\nTahap berikutnya: Sync Up saldo kas Stockbit saat ini.",
                "Sync Up");
            if (Window is not null)
                Window.Page = new SyncUpPage(_data);
        }
        catch (Exception ex)
        {
            _status.Text = $"File tidak dapat diproses: {ex.Message}";
            await AppDialog.ShowAsync(this, "Impor gagal", _status.Text, danger:true);
        }
        finally
        {
            _import.IsEnabled = true;
        }
    }
}
