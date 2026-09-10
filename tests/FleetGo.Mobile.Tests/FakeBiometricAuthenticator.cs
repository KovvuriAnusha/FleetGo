using FleetGo.Mobile.Core.Biometrics;

namespace FleetGo.Mobile.Tests;

/// <summary>Test double for <see cref="IBiometricAuthenticator"/> - no real hardware, no platform code.</summary>
internal sealed class FakeBiometricAuthenticator : IBiometricAuthenticator
{
    private readonly BiometricAvailability _availability;
    private readonly BiometricAuthenticationResult _result;

    private FakeBiometricAuthenticator(BiometricAvailability availability, BiometricAuthenticationResult result)
    {
        _availability = availability;
        _result = result;
    }

    public static FakeBiometricAuthenticator Available(BiometricAuthenticationResult? challengeResult = null) =>
        new(BiometricAvailability.Available, challengeResult ?? BiometricAuthenticationResult.Success);

    public static FakeBiometricAuthenticator Unavailable(BiometricAvailability availability) =>
        new(availability, BiometricAuthenticationResult.Failure(BiometricAuthenticationFailureReason.NotAvailable));

    public int AuthenticateCallCount { get; private set; }

    public string? LastReason { get; private set; }

    public Task<BiometricAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_availability);

    public Task<BiometricAuthenticationResult> AuthenticateAsync(string reason, CancellationToken cancellationToken = default)
    {
        AuthenticateCallCount++;
        LastReason = reason;
        return Task.FromResult(_result);
    }
}
