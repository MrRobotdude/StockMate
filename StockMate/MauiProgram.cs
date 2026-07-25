using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using StockMate.Services;

namespace StockMate;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().ConfigureFonts(fonts =>
        {
            fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
        });

        builder.Services.AddSingleton<AppDataService>();
        builder.Services.AddSingleton<MarketDataService>();
        builder.Services.AddSingleton<UniverseService>();
        builder.Services.AddSingleton<ScanEngine>();
        builder.Services.AddSingleton<TransactionHistoryService>();
        builder.Services.AddSingleton<PortfolioDecisionService>();
        builder.Services.AddSingleton<EventIntelligenceService>();
#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
