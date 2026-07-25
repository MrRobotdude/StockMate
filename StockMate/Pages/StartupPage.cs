using Microsoft.Extensions.DependencyInjection;
using StockMate.Services;
using StockMate.Ui;

namespace StockMate.Pages;

public sealed class StartupPage : ContentPage
{
    readonly Label _status;
    readonly ActivityIndicator _spinner;
    readonly Button _retry;
    bool _started;

    public StartupPage()
    {
        BackgroundColor = UiKit.Navy;

        _status = new Label
        {
            Text = "Menyiapkan aplikasi…",
            TextColor = UiKit.Muted,
            FontSize = 14,
            HorizontalTextAlignment = TextAlignment.Center
        };
        _spinner = new ActivityIndicator
        {
            IsRunning = true,
            Color = UiKit.Green,
            WidthRequest = 34,
            HeightRequest = 34
        };
        _retry = UiKit.Primary("Coba lagi");
        _retry.IsVisible = false;
        _retry.WidthRequest = 180;
        _retry.Clicked += async (_, _) => await InitializeAsync();

        Content = new Grid
        {
            Padding = new Thickness(28),
            Children =
            {
                new VerticalStackLayout
                {
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Center,
                    Spacing = 16,
                    Children =
                    {
                        new Image
                        {
                            Source = "stockmate_logo.svg",
                            WidthRequest = 128,
                            HeightRequest = 128
                        },
                        new Label
                        {
                            Text = "StockMate",
                            TextColor = Colors.White,
                            FontSize = 30,
                            FontAttributes = FontAttributes.Bold,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Label
                        {
                            Text = "Your smarter market companion",
                            TextColor = UiKit.Muted,
                            FontSize = 13,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        _spinner,
                        _status,
                        _retry
                    }
                }
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started) return;
        _started = true;
        await InitializeAsync();
    }

    async Task InitializeAsync()
    {
        _retry.IsVisible = false;
        _spinner.IsVisible = true;
        _spinner.IsRunning = true;
        _status.Text = "Memuat portofolio dan pengaturan…";

        try
        {
            // Yield first so Android can draw this page and animate the indicator.
            await Task.Yield();
            var data = App.Services.GetRequiredService<AppDataService>();
            await data.LoadAsync();

            _status.Text = "Menyiapkan dashboard…";

            if (Window is not null)
            {
                var hasImportedHistory = data.State.TransactionImports.Count > 0 &&
                    data.State.Transactions.Any(x => x.IsActive && x.Source == "HISTORY");
                Window.Page = hasImportedHistory
                    ? data.State.CashReconciled
                        ? new AppShell()
                        : new SyncUpPage(data)
                    : new ImportRequiredPage(
                        data,
                        App.Services.GetRequiredService<TransactionHistoryService>());
            }
        }
        catch (Exception ex)
        {
            _spinner.IsRunning = false;
            _spinner.IsVisible = false;
            _status.Text = $"Aplikasi gagal dimuat.\n{ex.Message}";
            _retry.IsVisible = true;
        }
    }
}
