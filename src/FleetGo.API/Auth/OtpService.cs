using System.Security.Cryptography;
using System.Text;
using FleetGo.API.Auth.Options;
using FleetGo.API.Data;
using FleetGo.API.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FleetGo.API.Auth;

/// <inheritdoc cref="IOtpService" />
internal sealed class OtpService : IOtpService
{
    private readonly FleetGoDbContext _db;
    private readonly IOtpSender _otpSender;
    private readonly IAuthService _authService;
    private readonly OtpOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OtpService> _logger;

    public OtpService(
        FleetGoDbContext db,
        IOtpSender otpSender,
        IAuthService authService,
        IOptions<OtpOptions> options,
        TimeProvider timeProvider,
        ILogger<OtpService> logger)
    {
        _db = db;
        _otpSender = otpSender;
        _authService = authService;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task RequestOtpAsync(string email, CancellationToken cancellationToken)
    {
        string normalizedEmail = email.Trim().ToLowerInvariant();
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

        User? user = await _db.Users
            .Include(u => u.Driver)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        // Every one of these is a reason not to send a code, and every one of them is
        // handled identically: no code is generated, no exception is thrown, nothing in the
        // response differs. An unknown email, an inactive account, and an account with no
        // phone number on file must all be indistinguishable from a legitimate request that
        // is simply being rate-limited - otherwise the endpoint becomes a way to test which
        // email addresses have an active driver profile.
        if (user is null || !user.IsActive || user.Driver is not { PhoneNumber: { Length: > 0 } phoneNumber })
        {
            _logger.LogInformation("OTP request ignored for {Email}: no eligible account.", normalizedEmail);
            return;
        }

        OtpCode? mostRecent = await _db.OtpCodes
            .Where(o => o.UserId == user.Id && o.Purpose == OtpPurposes.Login)
            .OrderByDescending(o => o.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        // The per-account cooldown - independent of the ASP.NET Core rate limiter on the
        // endpoint (see Program.cs), which only ever sees a caller's IP address. An attacker
        // spreading requests for the same account across many IPs would sail straight past
        // an IP-keyed limiter; this rule catches that regardless of where the requests come
        // from. Silently doing nothing (rather than an error) keeps the response generic.
        if (mostRecent is not null && now < mostRecent.CreatedAtUtc.AddSeconds(_options.ResendCooldownSeconds))
        {
            _logger.LogInformation("OTP request ignored for {Email}: inside the resend cooldown.", normalizedEmail);
            return;
        }

        // A new code invalidates whatever was active before it - at most one usable code
        // per user/purpose at a time, exactly as a refresh token rotates the one it replaces.
        List<OtpCode> activeCodes = await _db.OtpCodes
            .Where(o => o.UserId == user.Id
                && o.Purpose == OtpPurposes.Login
                && o.ConsumedAtUtc == null
                && o.InvalidatedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (OtpCode active in activeCodes)
        {
            active.InvalidatedAtUtc = now;
        }

        string code = GenerateCode(_options.CodeLength);
        byte[] salt = RandomNumberGenerator.GetBytes(16);

        _db.OtpCodes.Add(new OtpCode
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Purpose = OtpPurposes.Login,
            Salt = salt,
            CodeHash = ComputeHash(salt, code),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(_options.ExpiryMinutes),
            AttemptCount = 0,
        });

        await _db.SaveChangesAsync(cancellationToken);

        // The sender receives the plaintext code exactly once, for exactly long enough to
        // hand it to the delivery channel - it is never persisted anywhere in this class.
        await _otpSender.SendAsync(phoneNumber, code, OtpPurposes.Login, cancellationToken);
    }

    public async Task<AuthOutcome> VerifyOtpAsync(string email, string code, CancellationToken cancellationToken)
    {
        string normalizedEmail = email.Trim().ToLowerInvariant();
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

        User? user = await _db.Users
            .Include(u => u.Driver)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user is null || !user.IsActive)
        {
            _logger.LogInformation("OTP verification failed for {Email}: no active account.", normalizedEmail);
            return AuthOutcome.Failed;
        }

        OtpCode? active = await _db.OtpCodes
            .Where(o => o.UserId == user.Id
                && o.Purpose == OtpPurposes.Login
                && o.ConsumedAtUtc == null
                && o.InvalidatedAtUtc == null
                && o.ExpiresAtUtc > now)
            .OrderByDescending(o => o.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (active is null)
        {
            // Covers "no code was ever requested", "the code expired", "it was already used"
            // and "it was superseded by a newer request" - all one and the same failure from
            // the caller's point of view.
            _logger.LogInformation("OTP verification failed for {Email}: no active code.", normalizedEmail);
            return AuthOutcome.Failed;
        }

        byte[] candidateHash = ComputeHash(active.Salt, code);

        // Fixed-time comparison: an OTP hash is short-lived and single-use, so the timing
        // side-channel a naive == would open is a much smaller concern than for a password
        // hash, but it costs nothing to close it the same way.
        if (!CryptographicOperations.FixedTimeEquals(candidateHash, active.CodeHash))
        {
            active.AttemptCount++;

            if (active.AttemptCount >= _options.MaxVerificationAttempts)
            {
                // Locked out for good, even if the next guess would have been correct - the
                // caller gets no signal that this happened differently from any other failure.
                active.InvalidatedAtUtc = now;
                _logger.LogWarning(
                    "OTP for user {UserId} locked out after {AttemptCount} failed verification attempts.",
                    user.Id,
                    active.AttemptCount);
            }

            await _db.SaveChangesAsync(cancellationToken);
            return AuthOutcome.Failed;
        }

        active.ConsumedAtUtc = now;
        await _db.SaveChangesAsync(cancellationToken);

        return await _authService.IssueSessionForUserAsync(user, cancellationToken);
    }

    private static string GenerateCode(int length)
    {
        int exclusiveUpperBound = (int)Math.Pow(10, length);

        // RandomNumberGenerator.GetInt32 is a cryptographically secure, unbiased random
        // integer generator - Random/System.Random is not suitable for anything
        // security-sensitive, since its output is predictable from a handful of samples.
        int value = RandomNumberGenerator.GetInt32(0, exclusiveUpperBound);

        return value.ToString(new string('0', length));
    }

    private byte[] ComputeHash(byte[] salt, string code)
    {
        byte[] message = new byte[salt.Length + Encoding.UTF8.GetByteCount(code)];
        salt.CopyTo(message, 0);
        Encoding.UTF8.GetBytes(code, 0, code.Length, message, salt.Length);

        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.HashingKey), message);
    }
}
