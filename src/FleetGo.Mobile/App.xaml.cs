using FleetGo.Mobile.Core.Offline.Sync;

namespace FleetGo.Mobile;

public partial class App : Application
{
    private readonly SyncCoordinator _syncCoordinator;

    public App(SyncCoordinator syncCoordinator)
    {
        ArgumentNullException.ThrowIfNull(syncCoordinator);

        InitializeComponent();

        _syncCoordinator = syncCoordinator;

        // Started here, at the composition root's own entry point, so the outbox begins
        // draining as soon as the app is alive rather than whenever a driver happens to open
        // a screen that touches it. It is idempotent, and it does nothing at all until a
        // session exists.
        _syncCoordinator.Start();
    }

    /// <summary>
    /// Creates the app's main window. Overriding CreateWindow (rather than setting
    /// the obsolete MainPage property) is the .NET 9+ pattern and is what makes
    /// multi-window support on desktop possible later.
    /// </summary>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        Window window = new(new AppShell()) { Title = "FleetGo" };

        // Coming back to the foreground is the moment a driver is most likely to be somewhere
        // with signal again after a stretch without it - and the moment they are about to look
        // at data they expect to be current.
        window.Resumed += OnWindowResumed;

        return window;
    }

    private void OnWindowResumed(object? sender, EventArgs e) => _syncCoordinator.NotifyAppResumed();
}
