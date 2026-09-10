using FleetGo.Mobile.Core.Biometrics;
using Foundation;
using LocalAuthentication;

namespace FleetGo.Mobile.Platforms.MacCatalyst;

/// <summary>
/// <see cref="IBiometricAuthenticator"/> via <c>LocalAuthentication</c>, Apple's own
/// biometric API. Identical in shape to the Mac Catalyst implementation - both platforms
/// share the same framework - kept as separate files only because MAUI's Platforms/
/// convention compiles each platform folder into only its matching target framework.
/// Requires <c>NSFaceIDUsageDescription</c> in Info.plist (Face ID only - Touch ID needs no
/// usage-description key), or the app is terminated the first time this runs on a
/// Face ID-capable device.
/// </summary>
public sealed class AppleBiometricAuthenticator : IBiometricAuthenticator
{
    public Task<BiometricAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        using LAContext context = new();

        if (context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthenticationWithBiometrics, out NSError? error))
        {
            return Task.FromResult(BiometricAvailability.Available);
        }

        BiometricAvailability availability = (LAStatus)(long)(error?.Code ?? 0) switch
        {
            LAStatus.BiometryNotEnrolled => BiometricAvailability.NotEnrolled,
            LAStatus.BiometryNotAvailable => BiometricAvailability.NotSupported,
            LAStatus.BiometryLockout => BiometricAvailability.DeniedOrLockedOut,
            LAStatus.PasscodeNotSet => BiometricAvailability.NotSupported,
            _ => BiometricAvailability.Unknown,
        };

        return Task.FromResult(availability);
    }

    public Task<BiometricAuthenticationResult> AuthenticateAsync(string reason, CancellationToken cancellationToken = default)
    {
        LAContext context = new();
        TaskCompletionSource<BiometricAuthenticationResult> tcs = new();

        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            context.Invalidate();
            tcs.TrySetResult(BiometricAuthenticationResult.Failure(BiometricAuthenticationFailureReason.Cancelled));
        });

        context.EvaluatePolicy(
            LAPolicy.DeviceOwnerAuthenticationWithBiometrics,
            reason,
            (success, error) =>
            {
                if (success)
                {
                    tcs.TrySetResult(BiometricAuthenticationResult.Success);
                    return;
                }

                BiometricAuthenticationFailureReason failureReason = (LAStatus)(long)(error?.Code ?? 0) switch
                {
                    LAStatus.UserCancel or LAStatus.AppCancel or LAStatus.SystemCancel or LAStatus.UserFallback
                        => BiometricAuthenticationFailureReason.Cancelled,
                    LAStatus.BiometryLockout => BiometricAuthenticationFailureReason.LockedOut,
                    LAStatus.BiometryNotAvailable or LAStatus.BiometryNotEnrolled
                        => BiometricAuthenticationFailureReason.NotAvailable,
                    _ => BiometricAuthenticationFailureReason.Unknown,
                };

                tcs.TrySetResult(BiometricAuthenticationResult.Failure(failureReason));
            });

        return tcs.Task;
    }
}
