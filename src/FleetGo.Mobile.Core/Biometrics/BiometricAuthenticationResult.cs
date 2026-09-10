namespace FleetGo.Mobile.Core.Biometrics;

/// <summary>Outcome of one <see cref="IBiometricAuthenticator.AuthenticateAsync"/> call.</summary>
public sealed record BiometricAuthenticationResult(bool Succeeded, BiometricAuthenticationFailureReason? FailureReason)
{
    public static BiometricAuthenticationResult Success { get; } = new(true, null);

    public static BiometricAuthenticationResult Failure(BiometricAuthenticationFailureReason reason) => new(false, reason);
}
