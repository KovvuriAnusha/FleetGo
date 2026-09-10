using Android.App;
using AndroidX.Biometric;
using AndroidX.Core.Content;
using AndroidX.Fragment.App;
using FleetGo.Mobile.Core.Biometrics;
using Microsoft.Maui.ApplicationModel;
using Java.Lang;

namespace FleetGo.Mobile.Platforms.Android;

/// <summary>
/// <see cref="IBiometricAuthenticator"/> via AndroidX Biometric (<c>BiometricManager</c> /
/// <c>BiometricPrompt</c>), which is Google's recommended API over the older, deprecated
/// <c>FingerprintManager</c> - it covers face unlock and any future biometric modality on
/// a given device without this class needing to know which one is actually enrolled.
/// Requires <c>android.permission.USE_BIOMETRIC</c> in AndroidManifest.xml - a normal
/// permission the system grants automatically, never a runtime prompt.
/// </summary>
public sealed class AndroidBiometricAuthenticator : IBiometricAuthenticator
{
    public Task<BiometricAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        BiometricManager manager = BiometricManager.From(global::Android.App.Application.Context);
        int result = manager.CanAuthenticate(BiometricManager.Authenticators.BiometricWeak);

        BiometricAvailability availability = result switch
        {
            BiometricManager.BiometricSuccess => BiometricAvailability.Available,
            BiometricManager.BiometricErrorNoneEnrolled => BiometricAvailability.NotEnrolled,
            BiometricManager.BiometricErrorNoHardware => BiometricAvailability.NotSupported,
            BiometricManager.BiometricErrorHwUnavailable => BiometricAvailability.NotSupported,
            BiometricManager.BiometricErrorSecurityUpdateRequired => BiometricAvailability.NotSupported,
            _ => BiometricAvailability.Unknown,
        };

        return Task.FromResult(availability);
    }

    public Task<BiometricAuthenticationResult> AuthenticateAsync(string reason, CancellationToken cancellationToken = default)
    {
        if (Platform.CurrentActivity is not FragmentActivity activity)
        {
            return Task.FromResult(BiometricAuthenticationResult.Failure(BiometricAuthenticationFailureReason.NotAvailable));
        }

        TaskCompletionSource<BiometricAuthenticationResult> tcs = new();

        BiometricPrompt.PromptInfo promptInfo = new BiometricPrompt.PromptInfo.Builder()
            .SetTitle("FleetGo")
            .SetSubtitle(reason)
            .SetNegativeButtonText("Cancel")
            .Build();

        BiometricPrompt prompt = new(
            activity,
            ContextCompat.GetMainExecutor(activity),
            new AuthenticationCallback(tcs));

        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            if (tcs.TrySetResult(BiometricAuthenticationResult.Failure(BiometricAuthenticationFailureReason.Cancelled)))
            {
                prompt.CancelAuthentication();
            }
        });

        prompt.Authenticate(promptInfo);

        return tcs.Task;
    }

    /// <summary>
    /// Bridges AndroidX Biometric's callback-based API to a <see cref="Task"/>. A single
    /// failed fingerprint read (<see cref="OnAuthenticationFailed"/>) is not terminal - the
    /// system prompt stays open and lets the user try again - only a hard error or an
    /// explicit success ends the wait.
    /// </summary>
    private sealed class AuthenticationCallback : BiometricPrompt.AuthenticationCallback
    {
        private readonly TaskCompletionSource<BiometricAuthenticationResult> _tcs;

        public AuthenticationCallback(TaskCompletionSource<BiometricAuthenticationResult> tcs)
        {
            _tcs = tcs;
        }

        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult result) =>
            _tcs.TrySetResult(BiometricAuthenticationResult.Success);

        public override void OnAuthenticationError(int errorCode, ICharSequence errString) =>
            _tcs.TrySetResult(BiometricAuthenticationResult.Failure(MapError(errorCode)));

        public override void OnAuthenticationFailed()
        {
            // A single non-matching attempt - the prompt remains open for another try.
        }

        private static BiometricAuthenticationFailureReason MapError(int errorCode) => errorCode switch
        {
            BiometricPrompt.ErrorUserCanceled
                or BiometricPrompt.ErrorNegativeButton
                or BiometricPrompt.ErrorCanceled => BiometricAuthenticationFailureReason.Cancelled,
            BiometricPrompt.ErrorLockout
                or BiometricPrompt.ErrorLockoutPermanent => BiometricAuthenticationFailureReason.LockedOut,
            BiometricPrompt.ErrorNoBiometrics
                or BiometricPrompt.ErrorHwNotPresent
                or BiometricPrompt.ErrorHwUnavailable => BiometricAuthenticationFailureReason.NotAvailable,
            _ => BiometricAuthenticationFailureReason.Unknown,
        };
    }
}
