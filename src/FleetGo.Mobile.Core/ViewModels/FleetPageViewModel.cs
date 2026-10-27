using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using FleetGo.Shared.Http;

namespace FleetGo.Mobile.Core.ViewModels;

/// <summary>
/// Shared plumbing for the fleet screens: the busy/error/loaded state every one of them
/// shows, one place that turns an exception from the API into a message a driver can act on,
/// and the sync banner they all display.
/// <para>
/// This exists because the four fleet view models would otherwise repeat the same
/// twenty-line try/catch. It deliberately adds no navigation, no service locator and no
/// lifecycle of its own - <see cref="LoginViewModel"/> and <see cref="OtpVerificationViewModel"/>
/// stay exactly as they are, on plain <see cref="ObservableObject"/>.
/// </para>
/// </summary>
public abstract partial class FleetPageViewModel : ObservableObject
{
    protected FleetPageViewModel(SyncStatusViewModel sync)
    {
        ArgumentNullException.ThrowIfNull(sync);
        Sync = sync;
    }

    /// <summary>
    /// Raised when the API rejects the session outright (401). By this point the token
    /// provider has already tried to refresh - see <c>AuthenticationService.GetAccessTokenAsync</c> -
    /// so a 401 here means the session is genuinely gone and the driver has to sign in again.
    /// The page handles the navigation, exactly as <c>HomeViewModel.SignedOut</c> does; this
    /// view model knows nothing about Shell.
    /// </summary>
    public event EventHandler? SessionExpired;

    /// <summary>
    /// The shared sync banner. A singleton, so every fleet screen says the same thing about
    /// the network and the queue rather than each keeping its own opinion.
    /// </summary>
    public SyncStatusViewModel Sync { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// True once a load has completed, successfully or not. An empty list means "nothing to
    /// show" only after this is set - before it, the same empty list just means "not asked yet",
    /// and showing an empty-state message then would be a lie.
    /// </summary>
    [ObservableProperty]
    public partial bool HasLoaded { get; set; }

    /// <summary>
    /// True when something on this screen is still waiting to reach the server. Bound next to
    /// the record it belongs to rather than only in the banner, because "your change is saved
    /// on this device but the server has not seen it" is a fact about that stop, not about the
    /// app as a whole.
    /// </summary>
    [ObservableProperty]
    public partial bool HasPendingLocalChange { get; set; }

    public bool IsNotBusy => !IsBusy;

    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Runs one load operation with the busy flag, error reset and exception mapping the
    /// fleet screens all share.
    /// </summary>
    /// <param name="operation">The work to run. Anything it throws is mapped to <see cref="ErrorMessage"/>.</param>
    /// <param name="cancellationToken">Cancellation supplied by the generated command.</param>
    /// <param name="showBusyIndicator">
    /// False for a pull-to-refresh or a load-more, where the list is already on screen and a
    /// full-page spinner over it would be worse than no spinner at all.
    /// </param>
    /// <returns>True when <paramref name="operation"/> completed without error.</returns>
    protected async Task<bool> RunAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken,
        bool showBusyIndicator = true)
    {
        ErrorMessage = null;

        if (showBusyIndicator)
        {
            IsBusy = true;
        }

        try
        {
            await operation(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when the driver navigates away mid-load - not an error, and not a
            // state worth reporting on a page that is already going away.
            return false;
        }
        catch (TaskCanceledException)
        {
            // HttpClient reports its own timeout as a cancellation (see LoginViewModel).
            ErrorMessage = "The request timed out. Check your connection and try again.";
        }
        catch (FleetGoApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            ErrorMessage = "Your session has expired. Please sign in again.";
            SessionExpired?.Invoke(this, EventArgs.Empty);
        }
        catch (FleetGoApiException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            ErrorMessage = "This account does not have a driver profile.";
        }
        catch (FleetGoApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // The API answers 404 both for "no such id" and "belongs to another driver", so
            // this message deliberately does not claim to know which.
            ErrorMessage = "We couldn't find that. It may have been removed.";
        }
        catch (FleetGoApiException)
        {
            ErrorMessage = "The API could not complete that request. Please try again.";
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Could not reach the API. Is it running?";
        }
        finally
        {
            HasLoaded = true;

            if (showBusyIndicator)
            {
                IsBusy = false;
            }
        }

        return false;
    }

    /// <summary>
    /// Runs an optional network refresh and reports whether it worked, swallowing the two
    /// failures that mean "no network right now".
    /// <para>
    /// Since Phase 5 the screens read from the device, so losing the network is not a failed
    /// load - there is still data to show. Turning "the API was unreachable" into a red box
    /// over a perfectly usable list would undo the whole point of the offline work. Anything
    /// the API actually answered with is left to <see cref="RunAsync"/> to map, because those
    /// mean something specific.
    /// </para>
    /// </summary>
    protected static async Task<bool> TryRefreshAsync(Func<Task> refresh)
    {
        ArgumentNullException.ThrowIfNull(refresh);

        try
        {
            await refresh();
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }
}
