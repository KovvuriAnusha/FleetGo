namespace FleetGo.API.Auth;

/// <summary>
/// Delivers a one-time code to a driver, however that actually happens. The API is
/// deliberately coupled to nothing more specific than this: <see cref="OtpService"/> never
/// knows whether a code went out over SMS, a push notification, or (today) a log line -
/// swapping in a real SMS provider (Twilio, AWS SNS, Vonage, ...) in a later phase means
/// writing one class and changing one registration in <c>Program.cs</c>, nothing else.
/// </summary>
public interface IOtpSender
{
    /// <summary>
    /// Delivers <paramref name="code"/> to <paramref name="phoneNumber"/>. Implementations
    /// must never log <paramref name="code"/> outside of an explicitly non-production
    /// development aid (see <see cref="DevelopmentOtpSender"/>) - see
    /// docs/architecture.md, "OTP delivery", for why.
    /// </summary>
    Task SendAsync(string phoneNumber, string code, string purpose, CancellationToken cancellationToken);
}
