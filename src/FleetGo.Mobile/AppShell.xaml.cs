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
        Routing.RegisterRoute("otp-verify", typeof(OtpVerificationPage));

        // Phase 4B driver screens. Each page owns its own route name so a navigation call
        // and the registration can never drift apart.
        Routing.RegisterRoute(DashboardPage.RouteName, typeof(DashboardPage));
        Routing.RegisterRoute(RouteListPage.RouteName, typeof(RouteListPage));
        Routing.RegisterRoute(RouteDetailPage.RouteName, typeof(RouteDetailPage));
        Routing.RegisterRoute(StopDetailPage.RouteName, typeof(StopDetailPage));
    }
}

