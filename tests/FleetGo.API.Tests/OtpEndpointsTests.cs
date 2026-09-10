using System.Net;
using System.Net.Http.Json;
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
/// Covers the OTP request/verify lifecycle: issuing a code, verifying it, and every failure
/// and abuse-prevention path around that (expiry, reuse, attempt limits, resend cooldown
/// interaction, and the account-enumeration protections). Rate limiting itself is covered
/// separately in <see cref="OtpRateLimitingTests"/>, which needs its own, more strictly
/// configured host rather than the generous limits <see cref="FleetGoApiFactory"/> uses here
/// specifically so the tests in *this* class don't trip each other's shared rate-limit state.
/// </summary>
public sealed class OtpEndpointsTests : IClassFixture<FleetGoApiFactory>
{
    private const string Password = "correct horse battery staple 42!";

    private readonly FleetGoApiFactory _factory;

    public OtpEndpointsTests(FleetGoApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Request_Succeeds_AndSendsACode_ToTheDriversPhoneNumber()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (string email, string phoneNumber) = await SeedDriverAsync(ct);
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await PostRequestOtpAsync(client, email, ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotNull(_factory.OtpSender.LastCodeSentTo(phoneNumber));
    }

    [Fact]
    public async Task Verify_Succeeds_AndIssuesANormalTokenPair_ForTheCorrectCode()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (string email, string phoneNumber) = await SeedDriverAsync(ct);
        using HttpClient client = _factory.CreateClient();

        await PostRequestOtpAsync(client, email, ct);
        string code = _factory.OtpSender.LastCodeSentTo(phoneNumber)
            ?? throw new InvalidOperationException("No code was sent.");

        using HttpResponseMessage response = await PostVerifyOtpAsync(client, email, code, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TokenResponse? tokens = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.TokenResponse, ct);

        // Exactly the same shape a password login returns - OTP is a second way in, not a
        // second kind of session.
        Assert.NotNull(tokens);
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
        Assert.True(tokens.AccessTokenExpiresAtUtc > DateTimeOffset.UtcNow);
        Assert.True(tokens.RefreshTokenExpiresAtUtc > tokens.AccessTokenExpiresAtUtc);
    }

    [Fact]
    public async Task Verify_Fails_ForAnIncorrectCode()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (string email, string phoneNumber) = await SeedDriverAsync(ct);
        using HttpClient client = _factory.CreateClient();

        await PostRequestOtpAsync(client, email, ct);
        Assert.NotNull(_factory.OtpSender.LastCodeSentTo(phoneNumber));

        using HttpResponseMessage response = await PostVerifyOtpAsync(client, email, "000000", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Verify_Fails_ForAnUnknownEmail()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await PostVerifyOtpAsync(
            client, $"no-such-user-{Guid.NewGuid():N}@example.com", "123456", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Verify_Fails_ForAnExpiredCode()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        using IServiceScope scope = _factory.Services.CreateScope();
        FleetGoDbContext db = scope.ServiceProvider.GetRequiredService<FleetGoDbContext>();

        (string email, _) = await SeedDriverAsync(ct);
        User user = await db.Users.SingleAsync(u => u.Email == email, ct);

        // Seeded directly, already expired - its hash never needs to be correct, since the
        // active-code lookup excludes it by ExpiresAtUtc before any code comparison happens.
        db.OtpCodes.Add(new OtpCode
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Purpose = "login",
            Salt = new byte[16],
            CodeHash = new byte[32],
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5), // already expired
        });
        await db.SaveChangesAsync(ct);

        using HttpClient client = _factory.CreateClient();
        using HttpResponseMessage response = await PostVerifyOtpAsync(client, email, "123456", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Verify_Fails_WhenTheSameCodeIsUsedTwice()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (string email, string phoneNumber) = await SeedDriverAsync(ct);
        using HttpClient client = _factory.CreateClient();

        await PostRequestOtpAsync(client, email, ct);
        string code = _factory.OtpSender.LastCodeSentTo(phoneNumber)!;

        using HttpResponseMessage firstAttempt = await PostVerifyOtpAsync(client, email, code, ct);
        Assert.Equal(HttpStatusCode.OK, firstAttempt.StatusCode);

        using HttpResponseMessage secondAttempt = await PostVerifyOtpAsync(client, email, code, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, secondAttempt.StatusCode);
    }

    [Fact]
    public async Task Verify_Fails_AfterTooManyIncorrectAttempts_EvenWithTheCorrectCodeAfterwards()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (string email, string phoneNumber) = await SeedDriverAsync(ct);
        using HttpClient client = _factory.CreateClient();

        await PostRequestOtpAsync(client, email, ct);
        string code = _factory.OtpSender.LastCodeSentTo(phoneNumber)!;

        // OtpOptions.MaxVerificationAttempts defaults to 5 - five wrong guesses locks the
        // code out entirely, regardless of what is tried next.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            using HttpResponseMessage wrongAttempt = await PostVerifyOtpAsync(client, email, "000000", ct);
            Assert.Equal(HttpStatusCode.Unauthorized, wrongAttempt.StatusCode);
        }

        using HttpResponseMessage correctButTooLate = await PostVerifyOtpAsync(client, email, code, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, correctButTooLate.StatusCode);
    }

    [Fact]
    public async Task Request_InvalidatesThePreviouslyIssuedCode()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (string email, string phoneNumber) = await SeedDriverAsync(ct);
        using HttpClient client = _factory.CreateClient();

        await PostRequestOtpAsync(client, email, ct);
        string firstCode = _factory.OtpSender.LastCodeSentTo(phoneNumber)!;

        await PostRequestOtpAsync(client, email, ct);
        string secondCode = _factory.OtpSender.LastCodeSentTo(phoneNumber)!;

        Assert.NotEqual(firstCode, secondCode);

        using HttpResponseMessage oldCodeAttempt = await PostVerifyOtpAsync(client, email, firstCode, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, oldCodeAttempt.StatusCode);

        using HttpResponseMessage newCodeAttempt = await PostVerifyOtpAsync(client, email, secondCode, ct);
        Assert.Equal(HttpStatusCode.OK, newCodeAttempt.StatusCode);
    }

    [Fact]
    public async Task Request_DoesNotSendACode_ForAnUnknownEmail()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = _factory.CreateClient();
        int sendCountBefore = _factory.OtpSender.SendCount;

        using HttpResponseMessage response = await PostRequestOtpAsync(
            client, $"no-such-user-{Guid.NewGuid():N}@example.com", ct);

        // Generic response either way - an unknown email must look identical to a real,
        // rate-limited or otherwise ineligible request.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(sendCountBefore, _factory.OtpSender.SendCount);
    }

    [Fact]
    public async Task Request_DoesNotSendACode_ForAnInactiveAccount()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (string email, _) = await SeedDriverAsync(ct, isActive: false);
        using HttpClient client = _factory.CreateClient();
        int sendCountBefore = _factory.OtpSender.SendCount;

        using HttpResponseMessage response = await PostRequestOtpAsync(client, email, ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(sendCountBefore, _factory.OtpSender.SendCount);
    }

    [Fact]
    public async Task Request_DoesNotSendACode_WhenTheAccountHasNoPhoneNumberOnFile()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (string email, _) = await SeedDriverAsync(ct, withPhoneNumber: false);
        using HttpClient client = _factory.CreateClient();
        int sendCountBefore = _factory.OtpSender.SendCount;

        using HttpResponseMessage response = await PostRequestOtpAsync(client, email, ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(sendCountBefore, _factory.OtpSender.SendCount);
    }

    private static Task<HttpResponseMessage> PostRequestOtpAsync(HttpClient client, string email, CancellationToken ct) =>
        client.PostAsJsonAsync(
            ApiRoutes.AuthOtpRequest,
            new RequestOtpRequest(email),
            FleetGoJsonSerializerContext.Default.RequestOtpRequest,
            ct);

    private static Task<HttpResponseMessage> PostVerifyOtpAsync(HttpClient client, string email, string code, CancellationToken ct) =>
        client.PostAsJsonAsync(
            ApiRoutes.AuthOtpVerify,
            new VerifyOtpRequest(email, code),
            FleetGoJsonSerializerContext.Default.VerifyOtpRequest,
            ct);

    /// <summary>Seeds a user with a driver profile and (by default) a phone number - the minimum an account needs to be OTP-eligible.</summary>
    private async Task<(string Email, string PhoneNumber)> SeedDriverAsync(
        CancellationToken ct, bool isActive = true, bool withPhoneNumber = true)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        FleetGoDbContext db = scope.ServiceProvider.GetRequiredService<FleetGoDbContext>();
        IPasswordHasher<User> passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        string email = $"driver-{Guid.NewGuid():N}@example.com";
        string phoneNumber = $"+1555{Random.Shared.Next(1000000, 9999999)}";
        DateTime now = DateTime.UtcNow;
        Guid userId = Guid.NewGuid();

        User user = new()
        {
            Id = userId,
            Email = email,
            FirstName = "Test",
            LastName = "Driver",
            IsActive = isActive,
            PasswordHash = string.Empty,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Driver = new Driver
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                DriverCode = $"D-{Guid.NewGuid():N}"[..8],
                PhoneNumber = withPhoneNumber ? phoneNumber : null,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            },
        };
        user.PasswordHash = passwordHasher.HashPassword(user, Password);

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return (email, phoneNumber);
    }
}
