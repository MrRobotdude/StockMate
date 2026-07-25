using StockMate.Pages;
using StockMate.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace StockMate;

public sealed class AppShell : Shell
{
    public AppShell()
    {
        BackgroundColor = UiKit.Navy;
        Shell.SetForegroundColor(this, Colors.White);
        FlyoutBehavior = FlyoutBehavior.Disabled;
        var data = App.Services.GetRequiredService<Services.AppDataService>();
        Loc.Use(data.State.LanguageCode);
        Items.Add(Tab(Loc.T("Ringkasan", "Summary"), "⌂", () => new DashboardPage(
            data,
            App.Services.GetRequiredService<Services.PortfolioDecisionService>(),
            App.Services.GetRequiredService<Services.EventIntelligenceService>())));
        Items.Add(Tab(Loc.T("Portofolio", "Portfolio"), "▤", () => new PortfolioPage(
            data, App.Services.GetRequiredService<Services.PortfolioDecisionService>())));
        Items.Add(Tab(Loc.T("Scanner", "Scanner"), "⌕", () => new ScannerPage(
            data,
            App.Services.GetRequiredService<Services.ScanEngine>(),
            App.Services.GetRequiredService<Services.PortfolioDecisionService>())));
        Items.Add(Tab(Loc.T("Transaksi", "Transactions"), "✎", () => new JournalPage(
            data, App.Services.GetRequiredService<Services.TransactionHistoryService>())));
        Items.Add(Tab(Loc.T("Atur", "Settings"), "⚙", () => new SettingsPage(data, App.Services.GetRequiredService<Services.UniverseService>())));
    }

    static Tab Tab(string title, string icon, Func<Page> page)
    {
        var tab = new Tab { Title = title, Icon = new FontImageSource { Glyph = icon, Color = Colors.White, Size = 22 } };
        tab.Items.Add(new ShellContent { ContentTemplate = new DataTemplate(page) });
        return tab;
    }
}
