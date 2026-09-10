namespace FleetGo.Mobile;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Creates the app's main window. Overriding CreateWindow (rather than setting
    /// the obsolete MainPage property) is the .NET 9+ pattern and is what makes
    /// multi-window support on desktop possible later.
    /// </summary>
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new AppShell()) { Title = "FleetGo" };
}
