namespace FleetGo.Mobile.Views;

/// <summary>
/// The shared sync banner. No code of its own: it is bound to the page's
/// <c>FleetPageViewModel.Sync</c> and everything it shows is decided there.
/// </summary>
public partial class SyncBannerView : ContentView
{
    public SyncBannerView() => InitializeComponent();
}
