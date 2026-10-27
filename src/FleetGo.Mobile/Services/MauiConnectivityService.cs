using FleetGo.Mobile.Core.Offline.Connectivity;
using Microsoft.Maui.Networking;

namespace FleetGo.Mobile.Services;

/// <summary>
/// <see cref="IConnectivityService"/> over MAUI Essentials' <see cref="IConnectivity"/>.
/// <para>
/// A thin adapter on purpose: everything interesting about connectivity in this app is in
/// what the sync engine does with it, and that lives in <c>FleetGo.Mobile.Core</c>, where it
/// can be tested without a device. All this class does is translate one enum into another and
/// forward one event.
/// </para>
/// </summary>
public sealed class MauiConnectivityService : IConnectivityService, IDisposable
{
    private readonly IConnectivity _connectivity;

    public MauiConnectivityService(IConnectivity connectivity)
    {
        ArgumentNullException.ThrowIfNull(connectivity);

        _connectivity = connectivity;
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
    }

    public event EventHandler<NetworkStatus>? StatusChanged;

    public NetworkStatus Status => Translate(_connectivity.NetworkAccess);

    public bool IsOnline => Status == NetworkStatus.Online;

    public void Dispose() => _connectivity.ConnectivityChanged -= OnConnectivityChanged;

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e) =>
        StatusChanged?.Invoke(this, Translate(e.NetworkAccess));

    /// <summary>
    /// <see cref="NetworkAccess.ConstrainedInternet"/> means a captive portal - a network that
    /// will answer, but with a sign-in page rather than the API. Treated as offline, because
    /// the alternative is the app cheerfully reporting a connection and then failing every
    /// request against a hotel wifi splash screen.
    /// </summary>
    private static NetworkStatus Translate(NetworkAccess access) => access switch
    {
        NetworkAccess.Internet => NetworkStatus.Online,
        NetworkAccess.Unknown => NetworkStatus.Unknown,
        _ => NetworkStatus.Offline,
    };
}
