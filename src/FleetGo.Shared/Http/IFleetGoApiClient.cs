using FleetGo.Shared.Contracts;

namespace FleetGo.Shared.Http;

/// <summary>
/// Typed client for the FleetGo API. Consumers depend on this interface rather
/// than on <see cref="HttpClient"/>, which keeps view models unit-testable.
/// </summary>
public interface IFleetGoApiClient
{
    /// <summary>Reads metadata about the API instance the app is pointed at.</summary>
    Task<ApiInfoResponse> GetApiInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the API health report. A degraded or unhealthy API still returns a
    /// report (HTTP 503), so this only throws when the response is unusable.
    /// </summary>
    Task<HealthReportResponse> GetHealthAsync(CancellationToken cancellationToken = default);
}
