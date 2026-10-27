namespace FleetGo.Mobile.Core.Offline.Sync;

/// <summary>How long to wait before the next attempt, or that there should not be one.</summary>
/// <param name="ShouldRetry">False once the attempt budget is spent.</param>
/// <param name="Delay">How long to wait. Meaningless when <paramref name="ShouldRetry"/> is false.</param>
public readonly record struct RetryDecision(bool ShouldRetry, TimeSpan Delay);

/// <summary>Tuning for <see cref="ExponentialBackoffRetryPolicy"/>.</summary>
/// <param name="BaseDelay">Wait before the first retry. Doubles from there.</param>
/// <param name="MaxDelay">Ceiling on the wait, so backoff cannot grow into hours.</param>
/// <param name="MaxAttempts">
/// Total attempts allowed for one operation, including the first. This is the bound that
/// turns "retry until it works" into something that finishes: an operation that exhausts it
/// is marked failed and surfaced, not retried forever.
/// </param>
public sealed record RetryPolicyOptions(TimeSpan BaseDelay, TimeSpan MaxDelay, int MaxAttempts)
{
    /// <summary>
    /// Eight attempts, with waits of roughly 5s, 10s, 20s, 40s, 80s, 160s and then 300s
    /// (capped) between them - about ten minutes of trying before an operation is declared
    /// stuck. Long enough to ride out a tunnel or a depot with bad signal, short enough that a
    /// driver who is genuinely blocked finds out during their shift rather than at the end of
    /// it. The figures are pre-jitter: each actual wait lands between half and all of them.
    /// </summary>
    public static RetryPolicyOptions Default { get; } =
        new(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5), MaxAttempts: 8);
}

/// <summary>Decides when a failed operation may be tried again.</summary>
public interface IRetryPolicy
{
    /// <summary>The attempt budget, so callers can report "attempt 3 of 8" honestly.</summary>
    int MaxAttempts { get; }

    /// <summary>
    /// Given how many attempts have already been made, decides whether there is another one
    /// and how long to wait first.
    /// </summary>
    RetryDecision Next(int attemptsAlreadyMade);
}

/// <summary>
/// Exponential backoff with equal jitter, and a hard attempt limit.
/// <para>
/// Exponential because a server that is down stays down for longer than a second, and
/// hammering it - or the driver's battery and data allowance - helps nobody. Jittered because
/// a fleet of devices that all lost signal in the same underpass would otherwise all retry in
/// the same millisecond when it came back. "Equal jitter" keeps half the delay fixed and
/// randomises the other half, so the spread is real but no retry ever fires immediately.
/// </para>
/// <para>
/// The attempt limit is the part that matters most for correctness: Phase 5's brief asks for
/// no infinite retry loop, and an unbounded backoff is an infinite loop with better manners.
/// When the budget runs out the operation is marked failed and shown to the driver.
/// </para>
/// </summary>
public sealed class ExponentialBackoffRetryPolicy : IRetryPolicy
{
    private readonly RetryPolicyOptions _options;
    private readonly Func<double> _jitterSource;

    /// <param name="options">Defaults to <see cref="RetryPolicyOptions.Default"/>.</param>
    /// <param name="jitterSource">
    /// Returns a value in [0,1). Injected so tests get deterministic delays instead of
    /// asserting against a range.
    /// </param>
    public ExponentialBackoffRetryPolicy(RetryPolicyOptions? options = null, Func<double>? jitterSource = null)
    {
        _options = options ?? RetryPolicyOptions.Default;
        _jitterSource = jitterSource ?? Random.Shared.NextDouble;

        ArgumentOutOfRangeException.ThrowIfLessThan(_options.MaxAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_options.BaseDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.MaxDelay, _options.BaseDelay);
    }

    /// <inheritdoc />
    public int MaxAttempts => _options.MaxAttempts;

    /// <inheritdoc />
    public RetryDecision Next(int attemptsAlreadyMade)
    {
        if (attemptsAlreadyMade < 0)
        {
            attemptsAlreadyMade = 0;
        }

        if (attemptsAlreadyMade >= _options.MaxAttempts)
        {
            return new RetryDecision(ShouldRetry: false, TimeSpan.Zero);
        }

        // attemptsAlreadyMade is 1 after the first failure, and the first retry should wait
        // BaseDelay rather than twice it - hence the -1. Capped before the shift, not after:
        // an unbounded exponent here would be a bug that only appears on a device that has
        // been failing for a long time, which is the worst possible place to find one.
        int exponent = Math.Min(Math.Max(attemptsAlreadyMade - 1, 0), 16);
        double scaled = _options.BaseDelay.TotalMilliseconds * Math.Pow(2, exponent);
        double capped = Math.Min(scaled, _options.MaxDelay.TotalMilliseconds);

        // Equal jitter: half the delay is guaranteed, half is random.
        double jittered = (capped / 2) + (capped / 2 * _jitterSource());

        return new RetryDecision(ShouldRetry: true, TimeSpan.FromMilliseconds(jittered));
    }
}
