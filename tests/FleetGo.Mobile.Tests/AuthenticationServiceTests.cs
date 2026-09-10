using System.Net;
using FleetGo.Mobile.Core.Session;
using FleetGo.Shared.Http;

namespace FleetGo.Mobile.Tests;

/// <summary>
/// <see cref="StubHttpMessageHandler"/> answers every request with the same canned
/// response, so a successful login's incidental follow-up call to GetCurrentUserAsync
/// receives the token JSON instead of a user profile and fails to deserialise - handled
/// by <see cref="AuthenticationService"/>'s documented non-fatal fallback (CurrentUser
/// stays null). That does not affect what these tests assert, which is the token/session
/// behaviour, not the profile fetch.
/// </summary>
public sealed class AuthenticationServiceTests
{
    private static readonly Uri BaseAddress = new("http://localhost:5266");

    [Fact]
    public async Task LoginAsync_Succeeds_StoresTokensAndReportsAuthenticated()
    {
        const string json = """
            {
              "accessToken": "access-1",
              "accessTokenExpiresAtUtc": "2026-01-01T00:15:00+00:00",
              "refreshToken": "refresh-1",
              "refreshTokenExpiresAtUtc": "2026-01-31T00:00:00+00:00"
            }
            """;

        FakeSecureTokenStore tokenStore = new();
        AuthenticationService service = CreateService(
            StubHttpMessageHandler.RespondWith(HttpStatusCode.OK, json), tokenStore);

        AuthResult result = await service.LoginAsync("driver@example.com", "correct-password", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.True(service.IsAuthenticated);
        Assert.Equal(1, tokenStore.SaveCount);
    }

    [Fact]
    public async Task LoginAsync_Fails_ForAnUnauthorizedResponse_AndDoesNotStoreAnything()
    {
        FakeSecureTokenStore tokenStore = new();
        AuthenticationService service = CreateService(
            StubHttpMessageHandler.RespondWith(HttpStatusCode.Unauthorized, "{}"), tokenStore);

        AuthResult result = await service.LoginAsync("driver@example.com", "wrong-password", TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
        Assert.False(service.IsAuthenticated);
        Assert.Equal(0, tokenStore.SaveCount);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ReturnsNull_WhenNoSessionIsStored()
    {
        AuthenticationService service = CreateService(
            StubHttpMessageHandler.RespondWith(HttpStatusCode.OK, "{}"), new FakeSecureTokenStore());

        string? token = await service.GetAccessTokenAsync(TestContext.Current.CancellationToken);

        Assert.Null(token);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ReturnsTheStoredToken_WhenItIsNotNearExpiry()
    {
        const string json = """
            {
              "accessToken": "fresh-access-token",
              "accessTokenExpiresAtUtc": "2026-01-01T01:00:00+00:00",
              "refreshToken": "refresh-1",
              "refreshTokenExpiresAtUtc": "2026-01-31T00:00:00+00:00"
            }
            """;

        FakeSecureTokenStore tokenStore = new();
        AuthenticationService service = CreateService(
            StubHttpMessageHandler.RespondWith(HttpStatusCode.OK, json),
            tokenStore,
            now: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        await service.LoginAsync("driver@example.com", "correct-password", TestContext.Current.CancellationToken);

        string? token = await service.GetAccessTokenAsync(TestContext.Current.CancellationToken);

        Assert.Equal("fresh-access-token", token);
    }

    [Fact]
    public async Task TryRestoreSessionAsync_ReturnsFalse_WhenNothingIsStored()
    {
        AuthenticationService service = CreateService(
            StubHttpMessageHandler.RespondWith(HttpStatusCode.OK, "{}"), new FakeSecureTokenStore());

        bool restored = await service.TryRestoreSessionAsync(TestContext.Current.CancellationToken);

        Assert.False(restored);
        Assert.False(service.IsAuthenticated);
    }

    [Fact]
    public async Task TryRestoreSessionAsync_ClearsStorage_WhenTheStoredRefreshTokenHasExpired()
    {
        FakeSecureTokenStore tokenStore = new();
        tokenStore.Seed(new StoredTokens(
            "stale-access-token",
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "stale-refresh-token",
            new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero))); // long expired

        AuthenticationService service = CreateService(
            StubHttpMessageHandler.RespondWith(HttpStatusCode.OK, "{}"),
            tokenStore,
            now: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        bool restored = await service.TryRestoreSessionAsync(TestContext.Current.CancellationToken);

        Assert.False(restored);
        Assert.False(service.IsAuthenticated);
        Assert.Equal(1, tokenStore.ClearCount);
    }

    [Fact]
    public async Task LogoutAsync_ClearsTheStoredSession()
    {
        const string loginJson = """
            {
              "accessToken": "access-1",
              "accessTokenExpiresAtUtc": "2026-01-01T00:15:00+00:00",
              "refreshToken": "refresh-1",
              "refreshTokenExpiresAtUtc": "2026-01-31T00:00:00+00:00"
            }
            """;

        FakeSecureTokenStore tokenStore = new();
        AuthenticationService service = CreateService(
            StubHttpMessageHandler.RespondWith(HttpStatusCode.OK, loginJson), tokenStore);

        await service.LoginAsync("driver@example.com", "correct-password", TestContext.Current.CancellationToken);
        Assert.True(service.IsAuthenticated);

        await service.LogoutAsync(TestContext.Current.CancellationToken);

        Assert.False(service.IsAuthenticated);
        Assert.Equal(1, tokenStore.ClearCount);
        Assert.Null(await tokenStore.LoadAsync(TestContext.Current.CancellationToken));
    }

    private static AuthenticationService CreateService(
        StubHttpMessageHandler handler,
        ISecureTokenStore tokenStore,
        DateTimeOffset? now = null)
    {
        FleetGoApiClient apiClient = new(new HttpClient(handler) { BaseAddress = BaseAddress });
        FakeTimeProvider timeProvider = new(now ?? DateTimeOffset.UtcNow);
        return new AuthenticationService(apiClient, tokenStore, timeProvider);
    }
}

