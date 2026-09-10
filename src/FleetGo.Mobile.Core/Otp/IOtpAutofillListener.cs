namespace FleetGo.Mobile.Core.Otp;

/// <summary>
/// Listens for an incoming OTP arriving by whatever platform-native autofill mechanism
/// exists, without ever reading SMS content in application code.
/// <para>
/// On Android, the real implementation uses the Google Play Services SMS Retriever API -
/// no <c>READ_SMS</c>/<c>RECEIVE_SMS</c> permission, and it only ever sees messages that
/// match the app's own signature hash (see <c>AndroidOtpAutofillListener</c>). On iOS and
/// Mac Catalyst there is nothing to listen for: the one-time-code keyboard/QuickType
/// suggestion is entirely an OS-level feature tied to the <c>Entry</c>'s text content type
/// (see <c>MauiProgram</c>'s handler mapping), so those platforms register
/// <see cref="NoOpOtpAutofillListener"/> instead.
/// </para>
/// </summary>
public interface IOtpAutofillListener
{
    /// <summary>
    /// Waits for a code to arrive via autofill, or for <paramref name="cancellationToken"/>
    /// to fire (e.g. the user navigated away, or manually finished entering the code).
    /// Returns <see langword="null"/> if cancelled or if this platform has nothing to listen
    /// for - callers must always still allow manual entry regardless of what this returns.
    /// </summary>
    Task<string?> ListenForCodeAsync(CancellationToken cancellationToken);
}
