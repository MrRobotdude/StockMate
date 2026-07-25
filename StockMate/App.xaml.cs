using Microsoft.Extensions.DependencyInjection;

namespace StockMate;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = default!;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        Services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new Pages.StartupPage());
}
