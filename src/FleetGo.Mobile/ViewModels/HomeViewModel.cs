using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FleetGo.Mobile.Configuration;
using FleetGo.Mobile.Core.Session;
using FleetGo.Shared.Contracts;
using FleetGo.Shared.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Networking;

namespace FleetGo.Mobile.ViewModels;

/// <summary>
/// View model behind the landing page. Its only job in this phase is to prove
/// the whole stack is wired up: DI resolves it, it calls the API through the
/// shared typed client, and the page renders the result through data binding.
/// </summary>
/// <remarks>
/// Note what it does NOT do: it never touches HttpClient, never reads a static
/// singleton, and never references a page. That is what keeps it testable once
/// the mobile test project can reference it.
/// </remarks>
public sealed partial class HomeViewModel : ObservableObject
{
    private const string HealthyStatus = "Healthy";
    private const string Unknown = "—"; // em dash

    private readonly IFleetGoApiClient _apiClient;
    private readonly IConnectivity _connectivity;
    private readonly IAuthenticationService _authenticationService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HomeViewModel> _logger;

    public HomeViewModel(
        IFleetGoApiClient apiClient,
        IConnectivity connectivity,
        IAuthenticationService authenticationService,
        ApiSettings apiSettings,
        TimeProvider timeProvider,
        ILogger<HomeViewModel> logger)
    {
        _apiClient = apiClient;
        _connectivity = connectivity;
        _authenticationService = authenticationService;
        _timeProvider = timeProvider;
        _logger = logger;

        ApiBaseAddress = apiSettings.BaseAddress.ToString();
        StatusMessage = "Not checked yet.";
        ApiVersion = Unknown;
        ApiEnvironment = Unknown;
        LastCheckedText = "Never";

        // HomeViewModel is registered as a singleton and constructed once at app start,
        // before sign-in happens - subscribing here (rather than re-reading CurrentUser on
        // every page appearance) means the "Signed in as..." line updates itself the
        // moment login, session restore, or sign-out changes it.
        _authenticationService.AuthenticationStateChanged += OnAuthenticationStateChanged;
        SignedInAsText = BuildSignedInAsText();
    }

    /// <summary>Raised after a successful sign-out. The page handles navigation back to the login screen.</summary>
    public event EventHandler? SignedOut;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    public partial bool IsBusy { get; set; }

    /// <summary>Null until the first check completes, so the UI can show a neutral state.</summary>
    [ObservableProperty]
    public partial bool? IsApiReachable { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    [ObservableProperty]
    public partial string ApiBaseAddress { get; set; }

    [ObservableProperty]
    public partial string ApiVersion { get; set; }

    [ObservableProperty]
    public partial string ApiEnvironment { get; set; }

    [ObservableProperty]
    public partial string LastCheckedText { get; set; }

    [ObservableProperty]
    public partial string SignedInAsText { get; set; }

    public bool IsNotBusy => !IsBusy;

    /// <summary>
    /// Calls the API and reports what came back. Generates a
    /// <c>CheckApiCommand</c> that the page binds to; the cancellation token is
    /// supplied by the generated command so navigating away stops the call.
    /// </summary>
    [RelayCommand]
    private async Task CheckApiAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Contacting the API…";

        try
        {
            // Cheap local check first: no point spending a 30 second timeout
            // discovering the device is in a lift.
            if (_connectivity.NetworkAccess != NetworkAccess.Internet)
            {
                SetFailure("No network connection on this device.");
                return;
            }

            ApiInfoResponse info = await _apiClient.GetApiInfoAsync(cancellationToken);
            HealthReportResponse health = await _apiClient.GetHealthAsync(cancellationToken);

            ApiVersion = info.Version;
            ApiEnvironment = info.Environment;
            IsApiReachable = true;
            StatusMessage = string.Equals(health.Status, HealthyStatus, StringComparison.OrdinalIgnoreCase)
                ? $"{info.Name} is healthy."
                : $"{info.Name} responded, but reports {health.Status}.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when the user navigates away mid-call - not an error.
            StatusMessage = "Check cancelled.";
        }
        catch (TaskCanceledException exception)
        {
            // HttpClient reports its own timeout as a cancellation, which is why
            // this is caught separately from a real user cancellation above.
            _logger.LogWarning(exception, "The FleetGo API did not respond within the configured timeout.");
            SetFailure("The API did not respond in time.");
        }
        catch (FleetGoApiException exception)
        {
            _logger.LogWarning(exception, "The FleetGo API returned an unusable response.");
            SetFailure($"The API answered with an error ({(int?)exception.StatusCode}).");
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "The FleetGo API could not be reached at {BaseAddress}.", ApiBaseAddress);
            SetFailure("Could not reach the API. Is it running?");
        }
        finally
        {
            LastCheckedText = _timeProvider.GetLocalNow().ToString("HH:mm:ss");
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        await _authenticationService.LogoutAsync();
        SignedOut?.Invoke(this, EventArgs.Empty);
    }

    private void SetFailure(string message)
    {
        IsApiReachable = false;
        ApiVersion = Unknown;
        ApiEnvironment = Unknown;
        StatusMessage = message;
    }

    private void OnAuthenticationStateChanged(object? sender, EventArgs e) => SignedInAsText = BuildSignedInAsText();

    private string BuildSignedInAsText() =>
        _authenticationService.CurrentUser is { } user ? $"Signed in as {user.Email}" : "Signed in";
}

