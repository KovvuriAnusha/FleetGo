using FleetGo.Shared.Contracts;
using FleetGo.Shared.Serialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FleetGo.API.Health;

/// <summary>
/// Projects ASP.NET Core's internal <see cref="HealthReport"/> onto the shared
/// <see cref="HealthReportResponse"/> contract.
/// <para>
/// The default health endpoint writes the plain text "Healthy", which a client
/// cannot do much with. Writing our own shared contract means the mobile app can
/// deserialise the report with the same types the API produced it from.
/// </para>
/// </summary>
internal static class HealthReportResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        HealthReportResponse response = new(
            Status: report.Status.ToString(),
            TotalDurationMs: Math.Round(report.TotalDuration.TotalMilliseconds, 2),
            Entries: [.. report.Entries.Select(entry => new HealthCheckEntryResponse(
                Name: entry.Key,
                Status: entry.Value.Status.ToString(),
                DurationMs: Math.Round(entry.Value.Duration.TotalMilliseconds, 2),
                Description: entry.Value.Description))]);

        return context.Response.WriteAsJsonAsync(
            response,
            FleetGoJsonSerializerContext.Default.HealthReportResponse,
            contentType: "application/json; charset=utf-8",
            cancellationToken: context.RequestAborted);
    }
}
