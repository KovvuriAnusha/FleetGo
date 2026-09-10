using UIKit;

namespace FleetGo.Mobile;

public class Program
{
    // The Mac Catalyst entry point. UIApplication.Main hands control to AppDelegate,
    // which builds the MAUI app.
    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
