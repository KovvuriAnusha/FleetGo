namespace FleetGo.Mobile.Core.Threading;

/// <summary>
/// Runs a callback where it is safe to touch bound properties.
/// <para>
/// The sync engine runs on background threads and raises events from them. A view model that
/// updated an <c>ObservableProperty</c> straight from one of those would be raising
/// PropertyChanged off the UI thread, which on Android and iOS is not a race that shows up in
/// testing - it is an intermittent crash in a driver's hand weeks later.
/// </para>
/// <para>
/// This is an interface rather than a direct call to <c>MainThread</c> because
/// <c>FleetGo.Mobile.Core</c> has no MAUI dependency by design, and because tests need the
/// callback to run inline instead of being posted somewhere nothing is pumping.
/// </para>
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Queues <paramref name="action"/> to run on the UI thread. Returns immediately.</summary>
    void Post(Action action);
}

/// <summary>
/// Runs the callback on the calling thread. The default for tests and for any host with no
/// UI thread to speak of.
/// </summary>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public static ImmediateUiDispatcher Instance { get; } = new();

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
    }
}
