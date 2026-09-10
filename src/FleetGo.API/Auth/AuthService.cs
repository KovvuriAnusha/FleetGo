using System.Security.Cryptography;
using System.Text;
using FleetGo.API.Auth.Options;
using FleetGo.API.Data;
using FleetGo.API.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FleetGo.API.Auth;

/// <inheritdoc cref="IAuthService" />
internal sealed class AuthService : IAuthService
{
    private readonly FleetGoDbContext _db;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly RefreshTokenOptions _refreshTokenOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        FleetGoDbContext db,
        IJwtTokenService jwtTokenService,
        IPasswordHasher<User> passwordHasher,
        IOptions<RefreshTokenOptions> refreshTokenOptions,
        TimeProvider timeProvider,
        ILogger<AuthService> logger)
    {
        _db = db;
        _jwtTokenService = jwtTokenService;
        _passwordHasher = passwordHasher;
        _refreshTokenOptions = refreshTokenOptions.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AuthOutcome> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        string normalizedEmail = email.Trim().ToLowerInvariant();

        User? user = await _db.Users
            .Include(u => u.Driver)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        // Deliberately the same failure, logged the same way, whether the email does not
        // exist, the password is wrong, or the account is inactive - the caller must not be
        // able to tell those three apart from the response.
        if (user is null || !user.IsActive)
        {
            _logger.LogInformation("Login failed for {Email}: no active account.", normalizedEmail);
            return AuthOutcome.Failed;
        }

        PasswordVerificationResult verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);

        if (verification == PasswordVerificationResult.Failed)
        {
            _logger.LogInformation("Login failed for {Email}: incorrect password.", normalizedEmail);
            return AuthOutcome.Failed;
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            // The hasher's default algorithm/iteration count moved on since this password
            // was last set. Re-hashing on a successful login costs nothing extra to the
            // user and keeps every stored hash current without a separate migration.
            user.PasswordHash = _passwordHasher.HashPassword(user, password);
        }

        return await IssueTokenPairAsync(user, cancellationToken);
    }

    public async Task<AuthOutcome> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        byte[] presentedHash = Hash(refreshToken);
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

        RefreshToken? existing = await _db.RefreshTokens
            .Include(t => t.User)
            .ThenInclude(u => u!.Driver)
            .FirstOrDefaultAsync(t => t.TokenHash == presentedHash, cancellationToken);

        if (existing is null)
        {
            return AuthOutcome.Failed;
        }

        if (existing.RevokedAtUtc is not null)
        {
            // A token that was already rotated away is being presented again. The
            // legitimate client has no reason to replay a token it already exchanged, so
            // this most likely means the token was copied by someone else. Revoke every
            // other still-active token on the account to force a fresh login everywhere.
            _logger.LogWarning(
                "Reused refresh token detected for user {UserId}. Revoking all active refresh tokens.",
                existing.UserId);

            await RevokeAllActiveTokensAsync(existing.UserId, now, cancellationToken);
            return AuthOutcome.Failed;
        }

        if (now >= existing.ExpiresAtUtc || existing.User is null || !existing.User.IsActive)
        {
            return AuthOutcome.Failed;
        }

        User user = existing.User;

        AuthOutcome outcome = await IssueTokenPairAsync(user, cancellationToken, rotatedFrom: existing, now: now);
        return outcome;
    }

    public Task<AuthOutcome> IssueSessionForUserAsync(User user, CancellationToken cancellationToken) =>
        IssueTokenPairAsync(user, cancellationToken);

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        byte[] presentedHash = Hash(refreshToken);

        RefreshToken? existing = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == presentedHash, cancellationToken);

        // Idempotent: an unknown or already-revoked token still "succeeds", because the
        // caller's goal (this token must not work) is already true either way.
        if (existing is not null && existing.RevokedAtUtc is null)
        {
            existing.RevokedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<AuthOutcome> IssueTokenPairAsync(
        User user,
        CancellationToken cancellationToken,
        RefreshToken? rotatedFrom = null,
        DateTime? now = null)
    {
        DateTime issuedAtUtc = now ?? _timeProvider.GetUtcNow().UtcDateTime;

        AccessToken accessToken = _jwtTokenService.CreateAccessToken(user);

        (string rawRefreshToken, byte[] refreshTokenHash) = GenerateRefreshTokenMaterial();
        DateTime refreshTokenExpiresAtUtc = issuedAtUtc.AddDays(_refreshTokenOptions.LifetimeDays);

        RefreshToken newToken = new()
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = refreshTokenHash,
            CreatedAtUtc = issuedAtUtc,
            ExpiresAtUtc = refreshTokenExpiresAtUtc,
        };

        _db.RefreshTokens.Add(newToken);

        if (rotatedFrom is not null)
        {
            rotatedFrom.RevokedAtUtc = issuedAtUtc;
            rotatedFrom.ReplacedByTokenId = newToken.Id;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return AuthOutcome.Success(accessToken, rawRefreshToken, refreshTokenExpiresAtUtc);
    }

    private async Task RevokeAllActiveTokensAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        List<RefreshToken> activeTokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null && t.ExpiresAtUtc > nowUtc)
            .ToListAsync(cancellationToken);

        foreach (RefreshToken token in activeTokens)
        {
            token.RevokedAtUtc = nowUtc;
        }

        if (activeTokens.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private static (string RawToken, byte[] Hash) GenerateRefreshTokenMaterial()
    {
        byte[] randomBytes = RandomNumberGenerator.GetBytes(64); // 512 bits of entropy.

        // URL/JSON-safe Base64 (RFC 4648 §5): '+'/'/' would otherwise need escaping in a
        // JSON string and would not survive unescaped in a URL or header value verbatim.
        string rawToken = Convert.ToBase64String(randomBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return (rawToken, Hash(rawToken));
    }

    private static byte[] Hash(string rawToken) => SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
}

