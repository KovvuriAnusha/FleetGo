namespace FleetGo.Mobile.Core.Offline.Connectivity;

/// <summary>What the device believes about its network right now.</summary>
public enum NetworkStatus
{
    /// <summary>Not determined yet - treated as offline, because acting on a guess is worse than waiting.</summary>
    Unknown,

    /// <summary>No usable network.</summary>
    Offline,

    /// <summary>A network the OS considers usable for internet traffic.</summary>
    Online,
}

/// <summary>
/// Whether the device has a network, as a testable abstraction over
/// <c>Microsoft.Maui.Networking.IConnectivity</c>.
/// <para>
/// A deliberate caveat, and one worth saying out loud rather than hiding: this answers "does
/// the OS think there is a network", not "is the FleetGo API reachable". Captive portals,
/// a VPN that has dropped, a depot's wifi with no route out, and an API that is simply down
/// all report <see cref="NetworkStatus.Online"/>. The sync engine therefore never trusts this
/// alone - a transport-level failure on a real request is treated exactly like being offline
/// (see <c>SyncService</c>). What this is genuinely good for is the cheap negative: when the
/// OS says there is no network, there is no point spending a request, a timeout and the
/// driver's battery to find out.
/// </para>
/// </summary>
public interface IConnectivityService
{
    /// <summary>The current status.</summary>
    NetworkStatus Status { get; }

    /// <summary>Convenience for <c>Status == NetworkStatus.Online</c>.</summary>
    bool IsOnline { get; }

    /// <summary>
    /// Raised when the status changes. The sync coordinator subscribes to this: a network
    /// coming back is the single most useful moment to drain the outbox.
    /// </summary>
    event EventHandler<NetworkStatus>? StatusChanged;
}
