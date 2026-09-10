namespace FleetGo.Mobile.Tests;

/// <summary>Controllable-clock <see cref="TimeProvider"/> for tests that need to control "now" precisely (token expiry checks, cooldown timers).</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FakeTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward, for tests proving something changes once enough time has passed (e.g. a resend cooldown expiring).</summary>
    public void Advance(TimeSpan delta) => _now += delta;
}
