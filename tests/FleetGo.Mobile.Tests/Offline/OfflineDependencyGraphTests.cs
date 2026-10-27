using FleetGo.Mobile.Core.Offline;
using FleetGo.Mobile.Core.Offline.Connectivity;
using FleetGo.Mobile.Core.Offline.Sync;
using FleetGo.Mobile.Core.Session;
using FleetGo.Mobile.Core.Threading;
using FleetGo.Mobile.Core.ViewModels;
using FleetGo.Shared.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FleetGo.Mobile.Tests.Offline;

/// <summary>
/// Guards the shape of the offline half of the composition root, the same way
/// <see cref="AuthenticationDependencyGraphTests"/> guards the authentication half.
/// <para>
/// It matters here for the same reason it mattered there: the sync graph has a shape that is
/// easy to get subtly wrong - the service and the status provider must be the same object, the
/// coordinator and the trigger must be the same object, and the whole thing has to resolve
/// without a cycle. None of that is exercised by a test that constructs its subject by hand,
/// and on a device the failure arrives as a blank screen at launch.
/// </para>
/// <para>
/// The registrations below mirror <c>MauiProgram.RegisterOfflineServices</c>, with the two
/// MAUI-only pieces (connectivity and the UI thread) swapped for their test equivalents.
/// Keep this in step with MauiProgram.
/// </para>
/// </summary>
public sealed class OfflineDependencyGraphTests
{
    [Fact]
    public void TheWholeOfflineGraphResolves()
    {
        using ServiceProvider provider = BuildOfflineServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IFleetRepository>());
        Assert.NotNull(provider.GetRequiredService<ISyncService>());
        Assert.NotNull(provider.GetRequiredService<SyncCoordinator>());
        Assert.NotNull(provider.GetRequiredService<SyncStatusViewModel>());
    }

    [Fact]
    public void TheSyncServiceAndTheStatusProvider_AreTheSameInstance()
    {
        using ServiceProvider provider = BuildOfflineServiceProvider();

        // Two instances would mean two answers to "are we up to date?", and the banner would
        // be watching the one that never runs.
        Assert.Same(
            provider.GetRequiredService<ISyncService>(),
            provider.GetRequiredService<ISyncStatusProvider>());
    }

    [Fact]
    public void TheCoordinatorAndTheTrigger_AreTheSameInstance()
    {
        using ServiceProvider provider = BuildOfflineServiceProvider();

        Assert.Same(
            provider.GetRequiredService<SyncCoordinator>(),
            provider.GetRequiredService<ISyncTrigger>());
    }

    [Fact]
    public void EveryDriverScreenResolves()
    {
        using ServiceProvider provider = BuildOfflineServiceProvider();

        Assert.NotNull(provider.GetRequiredService<DashboardViewModel>());
        Assert.NotNull(provider.GetRequiredService<RouteListViewModel>());
        Assert.NotNull(provider.GetRequiredService<RouteDetailViewModel>());
        Assert.NotNull(provider.GetRequiredService<StopDetailViewModel>());
    }

    [Fact]
    public void EveryScreenSharesOneSyncBanner()
    {
        using ServiceProvider provider = BuildOfflineServiceProvider();

        DashboardViewModel dashboard = provider.GetRequiredService<DashboardViewModel>();
        RouteListViewModel list = provider.GetRequiredService<RouteListViewModel>();

        // The dashboard and the route list must never disagree about whether the device is
        // online - which they would, silently, if the banner were transient.
        Assert.Same(dashboard.Sync, list.Sync);
    }

    private static ServiceProvider BuildOfflineServiceProvider()
    {
        ServiceCollection services = new();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // Stand-ins for the parts of the app this graph depends on but that are not what is
        // under test here.
        services.AddSingleton<IFleetGoApiClient>(new FakeFleetGoApiClient());
        services.AddSingleton<IAuthenticationService>(new FakeAuthenticationService());

        string databasePath = Path.Combine(
            Path.GetTempPath(),
            "fleetgo-tests",
            Guid.NewGuid().ToString("N"),
            LocalStoreOptions.DefaultFileName);

        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        // --- mirrors MauiProgram.RegisterOfflineServices -------------------------------
        services.AddSingleton(new LocalStoreOptions(databasePath));
        services.AddSingleton<ILocalFleetStore, SqliteLocalFleetStore>();
        services.AddSingleton<IConnectivityService>(new FakeConnectivityService());
        services.AddSingleton<IUiDispatcher>(ImmediateUiDispatcher.Instance);
        services.AddSingleton<IRetryPolicy>(_ => new ExponentialBackoffRetryPolicy());
        services.AddSingleton<IFleetRepository, FleetRepository>();
        services.AddSingleton<SyncService>();
        services.AddSingleton<ISyncService>(sp => sp.GetRequiredService<SyncService>());
        services.AddSingleton<ISyncStatusProvider>(sp => sp.GetRequiredService<SyncService>());
        services.AddSingleton<SyncCoordinator>();
        services.AddSingleton<ISyncTrigger>(sp => sp.GetRequiredService<SyncCoordinator>());
        services.AddSingleton<SyncStatusViewModel>();

        services.AddTransient<DashboardViewModel>();
        services.AddTransient<RouteListViewModel>();
        services.AddTransient<RouteDetailViewModel>();
        services.AddTransient<StopDetailViewModel>();

        return services.BuildServiceProvider(validateScopes: true);
    }
}
