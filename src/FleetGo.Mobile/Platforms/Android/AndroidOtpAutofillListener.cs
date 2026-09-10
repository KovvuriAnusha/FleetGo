using Android.Content;
using Android.Gms.Auth.Api.Phone;
using Android.Gms.Tasks;
using AndroidX.Core.Content;
using FleetGo.Mobile.Core.Otp;

namespace FleetGo.Mobile.Platforms.Android;

/// <summary>
/// <see cref="IOtpAutofillListener"/> via the Google Play Services SMS Retriever API. This
/// is the mechanism Android intends specifically for OTP autofill without a broad SMS
/// permission: it hands the app the text of exactly one incoming message - only if that
/// message ends with this app's own 11-character signature hash - and nothing else in the
/// device's inbox is ever visible to this code. <c>AndroidManifest.xml</c> requires no
/// <c>READ_SMS</c>/<c>RECEIVE_SMS</c> permission for this to work.
/// <para>
/// Talks to the underlying <c>Android.Gms.Tasks.Task</c> only through
/// <c>AddOnFailureListener</c> rather than an <c>await</c> extension, since whether such an
/// extension exists depends on the exact Play Services binding version - the explicit
/// listener form is guaranteed to exist on every version of this API.
/// </para>
/// <para>
/// <c>CancellationToken</c> is fully qualified as <see cref="System.Threading.CancellationToken"/>
/// throughout this file - the <c>Android.Gms.Tasks</c> namespace this file imports (for
/// <c>Task</c> and <see cref="IOnFailureListener"/>) binds its own
/// <c>Android.Gms.Tasks.CancellationToken</c> (the Java Play Services type used for
/// <em>its own</em> task-cancellation API, unrelated to .NET's), so the bare name is
/// ambiguous (CS0104) and would otherwise silently fail to satisfy
/// <see cref="IOtpAutofillListener.ListenForCodeAsync"/>, whose parameter is the .NET type.
/// The same namespace also binds an unused <c>Android.Gms.Tasks.CancellationTokenSource</c> -
/// same trap if this file ever needs one - always qualify or add a
/// <c>using CancellationToken = System.Threading.CancellationToken;</c> alias rather than
/// relying on the bare name.
/// </para>
/// <para>
/// For the same reason, <c>Android.Gms.Tasks.Task</c> below is written as
/// <c>global::Android.Gms.Tasks.Task</c>. This file's own namespace is
/// <c>FleetGo.Mobile.Platforms.Android</c>, so an unqualified reference starting with
/// <c>Android.</c> resolves against the enclosing <c>FleetGo.Mobile.Platforms.Android</c>
/// namespace itself before it ever reaches the real, global <c>Android</c> namespace the
/// bindings live in - without <c>global::</c> this fails to compile with CS0234 ("the type
/// or namespace name 'Gms' does not exist in the namespace
/// 'FleetGo.Mobile.Platforms.Android'"), not a missing-reference error. The existing
/// <c>global::Android.App.Application.Context</c> reference above already uses this same
/// pattern.
/// </para>
/// </summary>
public sealed class AndroidOtpAutofillListener : IOtpAutofillListener
{
    public async Task<string?> ListenForCodeAsync(System.Threading.CancellationToken cancellationToken)
    {
        Context context = global::Android.App.Application.Context;
        SmsRetrieverBroadcastReceiver receiver = new();
        TaskCompletionSource<string?> tcs = new();

        receiver.CodeReceived += code => tcs.TrySetResult(code);

        // A protected, system-only broadcast - only Play Services can ever send it - so this
        // is registered dynamically and unregistered again at the end of this one listen
        // call, rather than declared (and left running) in AndroidManifest.xml.
        ContextCompat.RegisterReceiver(
            context,
            receiver,
            new IntentFilter(SmsRetriever.SmsRetrievedAction),
            ContextCompat.ReceiverNotExported);

        using CancellationTokenRegistration registration = cancellationToken.Register(() => tcs.TrySetResult(null));

        try
        {
            Java.Lang.Object startTask = SmsRetriever.GetClient(context).StartSmsRetriever();

            if (startTask is global::Android.Gms.Tasks.Task gmsTask)
            {
                gmsTask.AddOnFailureListener(new FailureListener(() => tcs.TrySetResult(null)));
            }

            return await tcs.Task;
        }
        catch (Java.Lang.Exception)
        {
            // Play Services unavailable, or the retriever could not be armed - autofill
            // simply does not happen this time. The caller always still accepts manual entry.
            return null;
        }
        finally
        {
            context.UnregisterReceiver(receiver);
        }
    }

    private sealed class FailureListener : Java.Lang.Object, IOnFailureListener
    {
        private readonly Action _onFailure;

        public FailureListener(Action onFailure) => _onFailure = onFailure;

        public void OnFailure(Java.Lang.Exception e) => _onFailure();
    }
}
