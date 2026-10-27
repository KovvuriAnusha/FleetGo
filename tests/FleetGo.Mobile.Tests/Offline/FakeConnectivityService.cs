using FleetGo.Mobile.Core.Offline.Connectivity;

namespace FleetGo.Mobile.Tests.Offline;

/// <summary>
/// A network a test can switch on and off. Everything interesting about the offline layer
/// happens at the moment connectivity changes, so being able to flip it deterministically is
/// most of what makes these tests possible without a device.
/// </summary>
internal sealed class FakeConnectivityService : IConnectivityService
{
    private NetworkStatus _status;

    public FakeConnectivityService(NetworkStatus status = NetworkStatus.Online) => _status = status;

    public event EventHandler<NetworkStatus>? StatusChanged;

    public NetworkStatus Status => _status;

    public bool IsOnline => _status == NetworkStatus.Online;

    public void GoOffline() => Set(NetworkStatus.Offline);

    public void GoOnline() => Set(NetworkStatus.Online);

    private void Set(NetworkStatus status)
    {
        if (_status == status)
        {
            return;
        }

        _status = status;
        StatusChanged?.Invoke(this, status);
    }
}
