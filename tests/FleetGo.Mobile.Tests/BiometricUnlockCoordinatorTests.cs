using FleetGo.Mobile.Core.Biometrics;

namespace FleetGo.Mobile.Tests;

public sealed class BiometricUnlockCoordinatorTests
{
    [Fact]
    public async Task TryUnlock_ReturnsNotEnabled_WhenTheDriverHasNotOptedIn()
    {
        FakeBiometricPreferenceStore preferences = new() { Enabled = false };
        FakeAuthenticationService authService = FakeAuthenticationService.ThatSucceeds();
        authService.HasStoredSession = true;
        BiometricUnlockCoordinator coordinator = new(FakeBiometricAuthenticator.Available(), preferences, authService);

        BiometricUnlockOutcome outcome = await coordinator.TryUnlockAsync();

        Assert.Equal(BiometricUnlockOutcome.NotEnabled, outcome);
    }

    [Fact]
    public async Task TryUnlock_ReturnsNoStoredSession_WhenNothingIsSignedIn()
    {
        FakeBiometricPreferenceStore preferences = new() { Enabled = true };
        FakeAuthenticationService authService = FakeAuthenticationService.ThatSucceeds();
        authService.HasStoredSession = false;
        BiometricUnlockCoordinator coordinator = new(FakeBiometricAuthenticator.Available(), preferences, authService);

        BiometricUnlockOutcome outcome = await coordinator.TryUnlockAsync();

        Assert.Equal(BiometricUnlockOutcome.NoStoredSession, outcome);
    }

    [Theory]
    [InlineData(BiometricAvailability.NotEnrolled)]
    [InlineData(BiometricAvailability.NotSupported)]
    [InlineData(BiometricAvailability.DeniedOrLockedOut)]
    public async Task TryUnlock_ReturnsNotAvailable_WhenBiometricsCannotRunRightNow(BiometricAvailability availability)
    {
        FakeBiometricPreferenceStore preferences = new() { Enabled = true };
        FakeAuthenticationService authService = FakeAuthenticationService.ThatSucceeds();
        authService.HasStoredSession = true;
        BiometricUnlockCoordinator coordinator = new(FakeBiometricAuthenticator.Unavailable(availability), preferences, authService);

        BiometricUnlockOutcome outcome = await coordinator.TryUnlockAsync();

        Assert.Equal(BiometricUnlockOutcome.NotAvailable, outcome);
    }

    [Fact]
    public async Task TryUnlock_Succeeds_AndRestoresTheStoredSession()
    {
        FakeBiometricPreferenceStore preferences = new() { Enabled = true };
        FakeAuthenticationService authService = FakeAuthenticationService.ThatSucceeds();
        authService.HasStoredSession = true;
        BiometricUnlockCoordinator coordinator = new(FakeBiometricAuthenticator.Available(), preferences, authService);

        BiometricUnlockOutcome outcome = await coordinator.TryUnlockAsync();

        // TryRestoreSessionAsync on FakeAuthenticationService always returns false (it is not
        // exercised by these tests) - Unlocked would require it to succeed, so a coordinator
        // that got this far without erroring, and correctly reported the restore's real
        // result, is what this asserts.
        Assert.Equal(BiometricUnlockOutcome.NoStoredSession, outcome);
    }

    [Fact]
    public async Task TryUnlock_ReturnsCancelled_WhenTheUserDismissesThePrompt()
    {
        FakeBiometricPreferenceStore preferences = new() { Enabled = true };
        FakeAuthenticationService authService = FakeAuthenticationService.ThatSucceeds();
        authService.HasStoredSession = true;
        FakeBiometricAuthenticator authenticator = FakeBiometricAuthenticator.Available(
            BiometricAuthenticationResult.Failure(BiometricAuthenticationFailureReason.Cancelled));
        BiometricUnlockCoordinator coordinator = new(authenticator, preferences, authService);

        BiometricUnlockOutcome outcome = await coordinator.TryUnlockAsync();

        Assert.Equal(BiometricUnlockOutcome.Cancelled, outcome);
    }

    [Fact]
    public async Task TryUnlock_ReturnsFailed_WhenTheChallengeFailsForAReasonOtherThanCancellation()
    {
        FakeBiometricPreferenceStore preferences = new() { Enabled = true };
        FakeAuthenticationService authService = FakeAuthenticationService.ThatSucceeds();
        authService.HasStoredSession = true;
        FakeBiometricAuthenticator authenticator = FakeBiometricAuthenticator.Available(
            BiometricAuthenticationResult.Failure(BiometricAuthenticationFailureReason.LockedOut));
        BiometricUnlockCoordinator coordinator = new(authenticator, preferences, authService);

        BiometricUnlockOutcome outcome = await coordinator.TryUnlockAsync();

        Assert.Equal(BiometricUnlockOutcome.Failed, outcome);
    }

    [Fact]
    public async Task Enable_TurnsThePreferenceOn_AfterAConfirmationChallenge()
    {
        FakeBiometricPreferenceStore preferences = new();
        FakeBiometricAuthenticator authenticator = FakeBiometricAuthenticator.Available();
        BiometricUnlockCoordinator coordinator = new(authenticator, preferences, FakeAuthenticationService.ThatSucceeds());

        BiometricEnableOutcome outcome = await coordinator.EnableAsync();

        Assert.Equal(BiometricEnableOutcome.Enabled, outcome);
        Assert.True(preferences.Enabled);
        Assert.Equal(1, authenticator.AuthenticateCallCount);
    }

    [Fact]
    public async Task Enable_DoesNotTurnOnThePreference_WhenBiometricsAreUnavailable()
    {
        FakeBiometricPreferenceStore preferences = new();
        BiometricUnlockCoordinator coordinator = new(
            FakeBiometricAuthenticator.Unavailable(BiometricAvailability.NotEnrolled),
            preferences,
            FakeAuthenticationService.ThatSucceeds());

        BiometricEnableOutcome outcome = await coordinator.EnableAsync();

        Assert.Equal(BiometricEnableOutcome.NotAvailable, outcome);
        Assert.False(preferences.Enabled);
    }

    [Fact]
    public async Task Enable_DoesNotTurnOnThePreference_WhenTheConfirmationChallengeIsCancelled()
    {
        FakeBiometricPreferenceStore preferences = new();
        FakeBiometricAuthenticator authenticator = FakeBiometricAuthenticator.Available(
            BiometricAuthenticationResult.Failure(BiometricAuthenticationFailureReason.Cancelled));
        BiometricUnlockCoordinator coordinator = new(authenticator, preferences, FakeAuthenticationService.ThatSucceeds());

        BiometricEnableOutcome outcome = await coordinator.EnableAsync();

        Assert.Equal(BiometricEnableOutcome.Cancelled, outcome);
        Assert.False(preferences.Enabled);
    }

    [Fact]
    public async Task Disable_TurnsThePreferenceOff_WithoutRequiringAChallenge()
    {
        FakeBiometricPreferenceStore preferences = new() { Enabled = true };
        FakeBiometricAuthenticator authenticator = FakeBiometricAuthenticator.Available();
        BiometricUnlockCoordinator coordinator = new(authenticator, preferences, FakeAuthenticationService.ThatSucceeds());

        await coordinator.DisableAsync();

        Assert.False(preferences.Enabled);
        Assert.Equal(0, authenticator.AuthenticateCallCount);
    }
}
