namespace FleetGo.Mobile.Tests;

/// <summary>Fixed-clock <see cref="TimeProvider"/> for tests that need to control "now" precisely (token expiry checks).</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FakeTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;
}

