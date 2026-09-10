using FleetGo.Mobile.Views;

namespace FleetGo.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Detail routes that are pushed/absolute-navigated rather than tabbed get
        // registered here. HomePage is reached only after authentication (see
        // LoginPage.xaml.cs and HomePage.xaml.cs), never as a tab a signed-out driver
        // could switch to directly.
        Routing.RegisterRoute("home", typeof(HomePage));
    }
}

