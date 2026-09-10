using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FleetGo.API.Data;
using FleetGo.API.Data.Entities;
using FleetGo.Shared;
using FleetGo.Shared.Contracts.Auth;
using FleetGo.Shared.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FleetGo.API.Tests;

/// <summary>
/// Covers the full login → refresh → logout lifecycle, plus the failure paths a client
/// actually needs to handle. Each test seeds its own, uniquely-emailed user rather than
/// sharing fixture data: <see cref="FleetGoApiFactory"/> hands out one SQLite database per
/// test <em>class</em> (via <see cref="IClassFixture{TFixture}"/>), shared by every test
/// method in it, so per-test data keeps mutations (a login issuing a token, a refresh
/// rotating one) from leaking between tests that run against the same database.
/// </summary>
public sealed class AuthEndpointsTests : IClassFixture<FleetGoApiFactory>
{
    private const string Password = "correct horse battery staple 42!";

    private readonly FleetGoApiFactory _factory;

    public AuthEndpointsTests(FleetGoApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_Succeeds_ForAnActiveUserWithTheCorrectPassword()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string email = await SeedUserAsync(ct);
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await PostLoginAsync(client, email, Password, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TokenResponse? tokens = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.TokenResponse, ct);

        Assert.NotNull(tokens);
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
        Assert.True(tokens.AccessTokenExpiresAtUtc > DateTimeOffset.UtcNow);
        Assert.True(tokens.RefreshTokenExpiresAtUtc > tokens.AccessTokenExpiresAtUtc);
    }

    [Fact]
    public async Task Login_Fails_WithAnIncorrectPassword()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string email = await SeedUserAsync(ct);
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await PostLoginAsync(client, email, "the wrong password", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_Fails_ForAnUnknownEmail()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await PostLoginAsync(
            client, $"no-such-user-{Guid.NewGuid():N}@example.com", Password, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_Fails_ForAnInactiveUser()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string email = await SeedUserAsync(ct, isActive: false);
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await PostLoginAsync(client, email, Password, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownEmailAndWrongPassword_ReturnTheSameProblemDetail()
    {
        // The response body must not let a caller distinguish "no such account" from
        // "wrong password" - that distinction is exactly what would let someone probe
        // for which email addresses have accounts.
        CancellationToken ct = TestContext.Current.CancellationToken;
        string email = await SeedUserAsync(ct);
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage unknownUserResponse = await PostLoginAsync(
            client, $"no-such-user-{Guid.NewGuid():N}@example.com", Password, ct);
        using HttpResponseMessage wrongPasswordResponse = await PostLoginAsync(client, email, "nope", ct);

        // Compare the stable "detail" field rather than the whole body: ASP.NET Core's
        // problem+json writer may attach a per-request extension (e.g. traceId), which
        // would legitimately differ between the two calls without leaking anything.
        using JsonDocument unknownUserProblem = JsonDocument.Parse(await unknownUserResponse.Content.ReadAsStringAsync(ct));
        using JsonDocument wrongPasswordProblem = JsonDocument.Parse(await wrongPasswordResponse.Content.ReadAsStringAsync(ct));

        Assert.Equal(unknownUserResponse.StatusCode, wrongPasswordResponse.StatusCode);
        Assert.Equal(
            unknownUserProblem.RootElement.GetProperty("detail").GetString(),
            wrongPasswordProblem.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Refresh_Succeeds_AndRotatesTheToken()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string email = await SeedUserAsync(ct);
        using HttpClient client = _factory.CreateClient();

        TokenResponse original = await LoginAndReadTokensAsync(client, email, ct);

        using HttpResponseMessage refreshResponse = await client.PostAsJsonAsync(
            ApiRoutes.AuthRefresh,
            new RefreshTokenRequest(original.RefreshToken),
            FleetGoJsonSerializerContext.Default.RefreshTokenRequest,
            ct);

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        TokenResponse? rotated = await refreshResponse.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.TokenResponse, ct);

        Assert.NotNull(rotated);
        Assert.NotEqual(original.RefreshToken, rotated.RefreshToken);
        Assert.NotEqual(original.AccessToken, rotated.AccessToken);

        // The old refresh token was consumed by rotation and must not work a second time.
        using HttpResponseMessage reuseResponse = await client.PostAsJsonAsync(
            ApiRoutes.AuthRefresh,
            new RefreshTokenRequest(original.RefreshToken),
            FleetGoJsonSerializerContext.Default.RefreshTokenRequest,
            ct);

        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);
    }

    [Fact]
    public async Task Refresh_Fails_ForAnExpiredToken()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        using IServiceScope scope = _factory.Services.CreateScope();
        FleetGoDbContext db = scope.ServiceProvider.GetRequiredService<FleetGoDbContext>();

        string email = await SeedUserAsync(ct);
        User user = await db.Users.SingleAsync(u => u.Email == email, ct);

        string rawToken = $"expired-{Guid.NewGuid():N}";
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-31),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(-1), // already expired
        });
        await db.SaveChangesAsync(ct);

        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.AuthRefresh,
            new RefreshTokenRequest(rawToken),
            FleetGoJsonSerializerContext.Default.RefreshTokenRequest,
            ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_Fails_ForARevokedToken()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string email = await SeedUserAsync(ct);
        using HttpClient client = _factory.CreateClient();

        TokenResponse tokens = await LoginAndReadTokensAsync(client, email, ct);

        // Logging out revokes the refresh token outright.
        using HttpResponseMessage logoutResponse = await client.PostAsJsonAsync(
            ApiRoutes.AuthLogout,
            new LogoutRequest(tokens.RefreshToken),
            FleetGoJsonSerializerContext.Default.LogoutRequest,
            ct);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        using HttpResponseMessage refreshResponse = await client.PostAsJsonAsync(
            ApiRoutes.AuthRefresh,
            new RefreshTokenRequest(tokens.RefreshToken),
            FleetGoJsonSerializerContext.Default.RefreshTokenRequest,
            ct);

        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task Refresh_Fails_ForAnUnknownToken()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.AuthRefresh,
            new RefreshTokenRequest("this-token-was-never-issued"),
            FleetGoJsonSerializerContext.Default.RefreshTokenRequest,
            ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_IsIdempotent_ForAnAlreadyRevokedOrUnknownToken()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage firstAttempt = await client.PostAsJsonAsync(
            ApiRoutes.AuthLogout,
            new LogoutRequest("a-token-nobody-ever-issued"),
            FleetGoJsonSerializerContext.Default.LogoutRequest,
            ct);

        Assert.Equal(HttpStatusCode.NoContent, firstAttempt.StatusCode);
    }

    [Fact]
    public async Task GetCurrentUser_ReturnsTheProfile_ForAValidAccessToken()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string email = await SeedUserAsync(ct, withDriver: true);
        using HttpClient client = _factory.CreateClient();

        TokenResponse tokens = await LoginAndReadTokensAsync(client, email, ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        using HttpResponseMessage response = await client.GetAsync(ApiRoutes.AuthMe, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CurrentUserResponse? me = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.CurrentUserResponse, ct);

        Assert.NotNull(me);
        Assert.Equal(email, me.Email);
        Assert.NotNull(me.DriverId);
        Assert.NotNull(me.DriverCode);
    }

    [Fact]
    public async Task GetCurrentUser_ReturnsUnauthorized_WithoutAnAccessToken()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(ApiRoutes.AuthMe, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCurrentUser_ReturnsUnauthorized_ForAGarbageBearerToken()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-jwt");

        using HttpResponseMessage response = await client.GetAsync(ApiRoutes.AuthMe, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string email, string password, CancellationToken ct) =>
        client.PostAsJsonAsync(
            ApiRoutes.AuthLogin,
            new LoginRequest(email, password),
            FleetGoJsonSerializerContext.Default.LoginRequest,
            ct);

    private static async Task<TokenResponse> LoginAndReadTokensAsync(HttpClient client, string email, CancellationToken ct)
    {
        using HttpResponseMessage response = await PostLoginAsync(client, email, Password, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync(FleetGoJsonSerializerContext.Default.TokenResponse, ct)
            ?? throw new InvalidOperationException("Login did not return a token response.");
    }

    /// <summary>Creates a uniquely-emailed user directly in the database, bypassing the API (there is no registration endpoint yet).</summary>
    private async Task<string> SeedUserAsync(CancellationToken ct, bool isActive = true, bool withDriver = false)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        FleetGoDbContext db = scope.ServiceProvider.GetRequiredService<FleetGoDbContext>();
        IPasswordHasher<User> passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        string email = $"driver-{Guid.NewGuid():N}@example.com";
        DateTime now = DateTime.UtcNow;

        User user = new()
        {
            Id = Guid.NewGuid(),
            Email = email,
            FirstName = "Test",
            LastName = "Driver",
            IsActive = isActive,
            PasswordHash = string.Empty,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        user.PasswordHash = passwordHasher.HashPassword(user, Password);

        if (withDriver)
        {
            user.Driver = new Driver
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                DriverCode = $"D-{Guid.NewGuid():N}"[..8],
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
        }

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return email;
    }
}

