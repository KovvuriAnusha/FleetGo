using Android.Content;
using Android.Gms.Auth.Api.Phone;
using Android.Gms.Common.Apis;
using Android.OS;

namespace FleetGo.Mobile.Platforms.Android;

/// <summary>
/// Receives the broadcast the Google Play Services SMS Retriever API sends once it spots an
/// incoming SMS whose trailing 11-character hash matches this app's signing certificate -
/// see <c>AndroidOtpAutofillListener</c>, which registers/unregisters an instance of this
/// per listen call, and <c>FleetGo.API.Auth.DevelopmentOtpSender</c> for the message shape
/// this depends on. This receiver never touches the inbox or requests <c>READ_SMS</c>/
/// <c>RECEIVE_SMS</c> - the OS only ever hands it the one matching message via this
/// broadcast, exactly Google's intended replacement for those broader permissions.
/// <para>
/// <c>Statuses</c> and <c>CommonStatusCodes</c> live in <c>Android.Gms.Common.Apis</c> -
/// plural "Apis", not the singular "Api" this file originally imported - confirmed by
/// inspecting the installed Xamarin.GooglePlayServices.Basement 118.10.0.2 binding's own
/// type metadata rather than guessing; the singular namespace does exist in this binding
/// (it holds unrelated types) but does not contain these two.
/// </para>
/// </summary>
internal sealed class SmsRetrieverBroadcastReceiver : BroadcastReceiver
{
    private static readonly System.Text.RegularExpressions.Regex CodePattern = new(@"\d{4,8}", System.Text.RegularExpressions.RegexOptions.Compiled);

    public event Action<string>? CodeReceived;

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != SmsRetriever.SmsRetrievedAction)
        {
            return;
        }

        Bundle? extras = intent.Extras;
        if (extras is null)
        {
            return;
        }

        var status = extras.Get(SmsRetriever.ExtraStatus) as Statuses;
        if (status is null || status.StatusCode != CommonStatusCodes.Success)
        {
            return; // Timeout or failure - AndroidOtpAutofillListener's own cancellation/timeout still applies.
        }

        string? message = extras.GetString(SmsRetriever.ExtraSmsMessage);
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        System.Text.RegularExpressions.Match match = CodePattern.Match(message);
        if (match.Success)
        {
            CodeReceived?.Invoke(match.Value);
        }
    }
}
