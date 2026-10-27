namespace FleetGo.Mobile.Tests;

/// <summary>Controllable-clock <see cref="TimeProvider"/> for tests that need to control "now" precisely (token expiry checks, cooldown timers).</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FakeTimeProvider(DateTimeOffset now) => _now = now;

    /// <summary>
    /// Pinned to UTC so <see cref="TimeProvider.GetLocalNow"/> is not the real machine's
    /// timezone. Without this, a test that fixes "today" as 09:00 UTC would quietly become
    /// yesterday on a build agent west of the Atlantic - the kind of failure that only shows
    /// up in someone else's CI.
    /// </summary>
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward, for tests proving something changes once enough time has passed (e.g. a resend cooldown expiring).</summary>
    public void Advance(TimeSpan delta) => _now += delta;
}
