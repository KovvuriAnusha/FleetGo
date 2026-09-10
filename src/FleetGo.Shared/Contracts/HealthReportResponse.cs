namespace FleetGo.Shared.Contracts;

/// <summary>
/// Serialisable projection of an ASP.NET Core health report.
/// The framework's own report type is not shareable with a mobile client, so
/// the API projects it onto this contract before writing the response.
/// </summary>
/// <param name="Status">Aggregate status: Healthy, Degraded or Unhealthy.</param>
/// <param name="TotalDurationMs">How long the whole health evaluation took.</param>
/// <param name="Entries">Per-check results.</param>
public sealed record HealthReportResponse(
    string Status,
    double TotalDurationMs,
    IReadOnlyList<HealthCheckEntryResponse> Entries);

/// <summary>Result of a single named health check.</summary>
/// <param name="Name">Name the check was registered under.</param>
/// <param name="Status">Healthy, Degraded or Unhealthy.</param>
/// <param name="DurationMs">How long this individual check took.</param>
/// <param name="Description">Optional human-readable detail.</param>
public sealed record HealthCheckEntryResponse(
    string Name,
    string Status,
    double DurationMs,
    string? Description);
