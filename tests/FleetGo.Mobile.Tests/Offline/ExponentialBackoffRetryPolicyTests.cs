using FleetGo.Mobile.Core.Offline.Sync;

namespace FleetGo.Mobile.Tests.Offline;

public class ExponentialBackoffRetryPolicyTests
{
    /// <summary>Jitter pinned to its midpoint, so the delays are arithmetic rather than a range.</summary>
    private static ExponentialBackoffRetryPolicy Policy(RetryPolicyOptions? options = null) =>
        new(options ?? RetryPolicyOptions.Default, jitterSource: () => 0.5);

    [Fact]
    public void Delay_DoublesWithEachAttempt()
    {
        ExponentialBackoffRetryPolicy policy = Policy(
            new RetryPolicyOptions(TimeSpan.FromSeconds(4), TimeSpan.FromMinutes(10), MaxAttempts: 10));

        // Equal jitter at its midpoint is 0.75x the raw delay: half fixed, half of the other
        // half. 4s -> 3s, 8s -> 6s, 16s -> 12s.
        Assert.Equal(TimeSpan.FromSeconds(3), policy.Next(1).Delay);
        Assert.Equal(TimeSpan.FromSeconds(6), policy.Next(2).Delay);
        Assert.Equal(TimeSpan.FromSeconds(12), policy.Next(3).Delay);
    }

    [Fact]
    public void Delay_StopsGrowingAtTheCeiling()
    {
        ExponentialBackoffRetryPolicy policy = Policy(
            new RetryPolicyOptions(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20), MaxAttempts: 20));

        Assert.Equal(TimeSpan.FromSeconds(15), policy.Next(9).Delay);
        Assert.Equal(TimeSpan.FromSeconds(15), policy.Next(19).Delay);
    }

    [Fact]
    public void Retrying_StopsOnceTheBudgetIsSpent()
    {
        ExponentialBackoffRetryPolicy policy = Policy(
            new RetryPolicyOptions(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), MaxAttempts: 3));

        Assert.True(policy.Next(1).ShouldRetry);
        Assert.True(policy.Next(2).ShouldRetry);

        // The line that turns "retry until it works" into something that finishes.
        Assert.False(policy.Next(3).ShouldRetry);
        Assert.False(policy.Next(99).ShouldRetry);
    }

    [Fact]
    public void Jitter_SpreadsRetriesWithoutEverFiringImmediately()
    {
        RetryPolicyOptions options = new(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5), MaxAttempts: 5);

        TimeSpan earliest = new ExponentialBackoffRetryPolicy(options, jitterSource: () => 0).Next(1).Delay;
        TimeSpan latest = new ExponentialBackoffRetryPolicy(options, jitterSource: () => 0.999).Next(1).Delay;

        // A fleet of devices leaving the same underpass must not all retry in the same
        // millisecond - but no retry should collapse to "right now" either: half the delay is
        // guaranteed whatever the jitter does.
        Assert.Equal(TimeSpan.FromSeconds(5), earliest);
        Assert.True(latest > earliest);
        Assert.True(latest <= TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void ZeroAttempts_IsTreatedAsTheFirstOne()
    {
        ExponentialBackoffRetryPolicy policy = Policy(
            new RetryPolicyOptions(TimeSpan.FromSeconds(8), TimeSpan.FromMinutes(1), MaxAttempts: 4));

        Assert.Equal(TimeSpan.FromSeconds(6), policy.Next(0).Delay);
        Assert.Equal(TimeSpan.FromSeconds(6), policy.Next(-1).Delay);
    }

    [Fact]
    public void Options_AreValidatedUpFront()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ExponentialBackoffRetryPolicy(new RetryPolicyOptions(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), MaxAttempts: 0)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ExponentialBackoffRetryPolicy(new RetryPolicyOptions(TimeSpan.Zero, TimeSpan.FromSeconds(10), MaxAttempts: 3)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ExponentialBackoffRetryPolicy(new RetryPolicyOptions(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), MaxAttempts: 3)));
    }
}
