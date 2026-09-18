using FleetGo.Mobile.Core.Session;
using FleetGo.Shared.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FleetGo.Mobile.Tests;

/// <summary>
/// Guards the shape of the app's composition root around authentication.
/// <para>
/// AuthenticationService consumes <see cref="IFleetGoApiClient"/> (it has to call
/// login/refresh/me) and also implements <see cref="IAccessTokenProvider"/>, which
/// <see cref="BearerTokenHandler"/> needs in order to authenticate that very client. That is
/// a cycle, and <c>IHttpClientFactory</c> walks straight into it: it builds the handler chain
/// <em>while</em> the typed client is being constructed, so resolving the token provider in
/// the handler's constructor re-enters the half-built session singleton. Nothing else in this
/// suite would notice - every other test constructs its view model or service by hand with a
/// fake client, and the Android build never resolves the graph at all.
/// </para>
/// <para>
/// These tests therefore build the real container and resolve through it. The registrations
/// below mirror <c>MauiProgram.RegisterAppServices</c> and
/// <c>RegisterAuthenticationServices</c>; only the MAUI-only singletons the authentication
/// graph never touches (SecureStorage, Connectivity, Preferences) are left out, and
/// on-device secure storage is swapped for the in-memory fake. Keep this in step with
/// MauiProgram if those registrations change.
/// </para>
/// </summary>
public sealed class AuthenticationDependencyGraphTests
{
    [Fact]
    public void ResolvingTheSession_BuildsTheWholeGraph()
    {
        using ServiceProvider provider = BuildAppServiceProvider();

        // Before the token provider was resolved lazily this threw: constructing
        // AuthenticationService needed the typed client, whose handler chain needed
        // AuthenticationService again.
        IAuthenticationService session = provider.GetRequiredService<IAuthenticationService>();

        Assert.NotNull(session);
        Assert.False(session.IsAuthenticated);
    }

    [Fact]
    public void ResolvingTheApiClientFirst_BuildsTheWholeGraph()
    {
        using ServiceProvider provider = BuildAppServiceProvider();

        // The other entry point into the same cycle, and the one that actually forces
        // IHttpClientFactory to build the handler chain first.
        IFleetGoApiClient apiClient = provider.GetRequiredService<IFleetGoApiClient>();

        Assert.NotNull(apiClient);
        Assert.NotNull(provider.GetRequiredService<IAccessTokenProvider>());
    }

    [Fact]
    public void TheSessionAndTheTokenProvider_AreTheSameInstance()
    {
        using ServiceProvider provider = BuildAppServiceProvider();

        IAuthenticationService session = provider.GetRequiredService<IAuthenticationService>();
        IAccessTokenProvider tokenProvider = provider.GetRequiredService<IAccessTokenProvider>();

        // One in-memory record of the current session, not two that could drift apart - the
        // other way the old cycle could have resolved, had the container built a second
        // AuthenticationService instead of failing outright.
        Assert.Same(session, tokenProvider);
    }

    [Fact]
    public void TheHandlerResolvesTheTokenProviderThroughTheContainer()
    {
        using ServiceProvider provider = BuildAppServiceProvider();

        // The handler is resolvable on its own, which is what AddHttpMessageHandler does when
        // it assembles the chain. If its dependency ever goes back to being constructor-
        // injected, this is where the container gives up.
        BearerTokenHandler handler = provider.GetRequiredService<BearerTokenHandler>();

        Assert.NotNull(handler);
    }

    /// <summary>Mirrors the app's own registrations - see this class's remarks.</summary>
    private static ServiceProvider BuildAppServiceProvider()
    {
        ServiceCollection services = new();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISecureTokenStore, FakeSecureTokenStore>();

        services.AddTransient(serviceProvider =>
            new BearerTokenHandler(() => serviceProvider.GetRequiredService<IAccessTokenProvider>()));

        services.AddHttpClient<IFleetGoApiClient, FleetGoApiClient>(client =>
            {
                client.BaseAddress = new Uri("http://localhost:5266");
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler<BearerTokenHandler>();

        services.AddSingleton<AuthenticationService>();
        services.AddSingleton<IAuthenticationService>(sp => sp.GetRequiredService<AuthenticationService>());
        services.AddSingleton<IAccessTokenProvider>(sp => sp.GetRequiredService<AuthenticationService>());

        return services.BuildServiceProvider(validateScopes: true);
    }
}
