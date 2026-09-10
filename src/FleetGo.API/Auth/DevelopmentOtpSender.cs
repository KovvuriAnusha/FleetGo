using Microsoft.Extensions.Logging;

namespace FleetGo.API.Auth;

/// <summary>
/// The only <see cref="IOtpSender"/> Phase 3 ships. It does not call any third-party SMS
/// API - Phase 3's brief deliberately excludes adding a paid provider dependency - it just
/// logs the code clearly enough for a developer or a test to find it.
/// <para>
/// <c>Program.cs</c> registers this ONLY for the Development and Testing environments, and
/// refuses to start in any other named environment unless a real <see cref="IOtpSender"/>
/// has been registered in its place - so the one place a plaintext code is ever written to
/// a log is a class that is structurally incapable of running in production, rather than a
/// runtime flag someone could leave on by mistake. See docs/architecture.md, "OTP delivery".
/// </para>
/// </summary>
public sealed class DevelopmentOtpSender : IOtpSender
{
    private readonly ILogger<DevelopmentOtpSender> _logger;

    public DevelopmentOtpSender(ILogger<DevelopmentOtpSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string phoneNumber, string code, string purpose, CancellationToken cancellationToken)
    {
        // The trailing app-signature line is what makes this message format compatible with
        // the Android SMS Retriever API (see FleetGo.Mobile's AndroidOtpAutofillListener):
        // a real SMS provider integration must send exactly this shape, ending in the app's
        // 11-character hash string, or Android autofill will not recognise the message.
        _logger.LogInformation(
            "[DEV-ONLY OTP - NEVER LOGGED IN PRODUCTION] To {PhoneNumber} ({Purpose}): " +
            "\"Your FleetGo verification code is {Code}. It expires shortly.\n\n<app-hash-placeholder>\"",
            phoneNumber,
            purpose,
            code);

        return Task.CompletedTask;
    }
}
