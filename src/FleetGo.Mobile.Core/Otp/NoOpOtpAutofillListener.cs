namespace FleetGo.Mobile.Core.Otp;

/// <summary>
/// Registered for iOS and Mac Catalyst, where autofill is a keyboard-level OS feature
/// rather than something application code listens for (see <see cref="IOtpAutofillListener"/>).
/// Contains no platform API calls, so - unlike the Android listener - it is safe to keep in
/// the platform-agnostic core project rather than under FleetGo.Mobile/Platforms.
/// </summary>
public sealed class NoOpOtpAutofillListener : IOtpAutofillListener
{
    public Task<string?> ListenForCodeAsync(CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);
}
