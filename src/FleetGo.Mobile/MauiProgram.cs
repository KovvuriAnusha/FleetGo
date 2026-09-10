using FleetGo.Mobile.Configuration;
using FleetGo.Mobile.ViewModels;
using FleetGo.Mobile.Views;
using FleetGo.Shared.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Networking;

namespace FleetGo.Mobile;

/// <summary>
/// The app's composition root. Everything the app can resolve is registered here
/// exactly once, so there is a single place to look when asking "where does this
/// dependency come from?".
/// </summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        MauiAppBuilder builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        RegisterAppServices(builder.Services);
        RegisterViewsAndViewModels(builder.Services);

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static void RegisterAppServices(IServiceCollection services)
    {
        ApiSettings apiSettings = ApiSettings.CreateDevelopmentDefaults();
        services.AddSingleton(apiSettings);

        // Injected instead of DateTimeOffset.UtcNow so time-dependent logic stays testable.
        services.AddSingleton(TimeProvider.System);

        // MAUI Essentials singletons are registered as interfaces so view models
        // depend on abstractions, not on static Current properties.
        services.AddSingleton(Connectivity.Current);

        // Typed client over IHttpClientFactory: the factory pools and recycles the
        // underlying handlers (a raw long-lived HttpClient misses DNS changes; a
        // new one per call exhausts sockets). It is also the seam where retry,
        // timeout and circuit-breaker policies plug in later.
        services.AddHttpClient<IFleetGoApiClient, FleetGoApiClient>(client =>
        {
            client.BaseAddress = apiSettings.BaseAddress;
            client.Timeout = apiSettings.Timeout;
        });
    }

    private static void RegisterViewsAndViewModels(IServiceCollection services)
    {
        // Pages and their view models are registered together: Shell resolves the
        // page from this container, and the page takes its view model by constructor.
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<HomePage>();
    }
}
