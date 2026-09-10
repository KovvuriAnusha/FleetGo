using FleetGo.Mobile.Core.Session;

namespace FleetGo.Mobile.Core.Biometrics;

/// <inheritdoc cref="IBiometricUnlockCoordinator" />
public sealed class BiometricUnlockCoordinator : IBiometricUnlockCoordinator
{
    private const string UnlockReason = "Unlock FleetGo";
    private const string EnableConfirmationReason = "Confirm it's you to turn on biometric unlock";

    private readonly IBiometricAuthenticator _authenticator;
    private readonly IBiometricPreferenceStore _preferenceStore;
    private readonly IAuthenticationService _authenticationService;

    public BiometricUnlockCoordinator(
        IBiometricAuthenticator authenticator,
        IBiometricPreferenceStore preferenceStore,
        IAuthenticationService authenticationService)
    {
        _authenticator = authenticator;
        _preferenceStore = preferenceStore;
        _authenticationService = authenticationService;
    }

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default) =>
        _preferenceStore.GetEnabledAsync(cancellationToken);

    public Task<BiometricAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default) =>
        _authenticator.CheckAvailabilityAsync(cancellationToken);

    public async Task<BiometricUnlockOutcome> TryUnlockAsync(CancellationToken cancellationToken = default)
    {
        // Cheapest checks first: no point prompting for a fingerprint over a session that
        // does not exist, or that the driver never opted into unlocking this way.
        if (!await _preferenceStore.GetEnabledAsync(cancellationToken))
        {
            return BiometricUnlockOutcome.NotEnabled;
        }

        if (!await _authenticationService.HasStoredSessionAsync(cancellationToken))
        {
            return BiometricUnlockOutcome.NoStoredSession;
        }

        BiometricAvailability availability = await _authenticator.CheckAvailabilityAsync(cancellationToken);

        if (availability != BiometricAvailability.Available)
        {
            return BiometricUnlockOutcome.NotAvailable;
        }

        BiometricAuthenticationResult challenge = await _authenticator.AuthenticateAsync(UnlockReason, cancellationToken);

        if (!challenge.Succeeded)
        {
            return challenge.FailureReason == BiometricAuthenticationFailureReason.Cancelled
                ? BiometricUnlockOutcome.Cancelled
                : BiometricUnlockOutcome.Failed;
        }

        // The biometric challenge only ever gates whether this call happens - the actual
        // session comes from the same server-verified restore/refresh every launch already
        // uses (see AuthenticationService.TryRestoreSessionAsync). Biometrics never mint or
        // unlock a token on their own.
        bool restored = await _authenticationService.TryRestoreSessionAsync(cancellationToken);
        return restored ? BiometricUnlockOutcome.Unlocked : BiometricUnlockOutcome.NoStoredSession;
    }

    public async Task<BiometricEnableOutcome> EnableAsync(CancellationToken cancellationToken = default)
    {
        BiometricAvailability availability = await _authenticator.CheckAvailabilityAsync(cancellationToken);

        if (availability != BiometricAvailability.Available)
        {
            return BiometricEnableOutcome.NotAvailable;
        }

        // Require one successful challenge before turning the preference on, rather than
        // trusting CheckAvailabilityAsync alone - it confirms biometrics actually work on
        // this device right now, instead of a driver enabling a toggle that then fails on
        // every subsequent launch for a reason CheckAvailabilityAsync could not predict.
        BiometricAuthenticationResult confirmation = await _authenticator.AuthenticateAsync(EnableConfirmationReason, cancellationToken);

        if (!confirmation.Succeeded)
        {
            return BiometricEnableOutcome.Cancelled;
        }

        await _preferenceStore.SetEnabledAsync(true, cancellationToken);
        return BiometricEnableOutcome.Enabled;
    }

    public Task DisableAsync(CancellationToken cancellationToken = default) =>
        _preferenceStore.SetEnabledAsync(false, cancellationToken);
}
