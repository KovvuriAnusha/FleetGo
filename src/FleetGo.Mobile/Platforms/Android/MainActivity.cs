using Android.App;
using Android.Content.PM;
using Android.OS;

namespace FleetGo.Mobile;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize
        | ConfigChanges.Orientation
        | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    // ConfigurationChanges above tells Android we handle rotation and dark-mode
    // switches ourselves, so the activity is not destroyed and recreated - which
    // would otherwise throw away in-progress work such as a delivery form.
}
