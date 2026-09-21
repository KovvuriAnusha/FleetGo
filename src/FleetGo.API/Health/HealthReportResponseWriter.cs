using FleetGo.Shared.Contracts;
using FleetGo.Shared.Serialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

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
        // The probes have to stay anonymous - an orchestrator cannot authenticate - but the
        // per-check breakdown names the API's dependencies, which is more than an anonymous
        // caller on the public internet needs. Outside development the response keeps the
        // aggregate status (all a probe reads) and drops the detail. Nothing here ever
        // includes an exception or a connection string: the writer only ever emitted a
        // check's name, status, duration and its own description.
        IHostEnvironment environment = context.RequestServices.GetRequiredService<IHostEnvironment>();
        bool includeCheckDetail = environment.IsDevelopment() || environment.IsEnvironment("Testing");

        IReadOnlyList<HealthCheckEntryResponse> entries = [];

        if (includeCheckDetail)
        {
            entries = [.. report.Entries.Select(entry => new HealthCheckEntryResponse(
                Name: entry.Key,
                Status: entry.Value.Status.ToString(),
                DurationMs: Math.Round(entry.Value.Duration.TotalMilliseconds, 2),
                Description: entry.Value.Description))];
        }

        HealthReportResponse response = new(
            Status: report.Status.ToString(),
            TotalDurationMs: Math.Round(report.TotalDuration.TotalMilliseconds, 2),
            Entries: entries);

        return context.Response.WriteAsJsonAsync(
            response,
            FleetGoJsonSerializerContext.Default.HealthReportResponse,
            contentType: "application/json; charset=utf-8",
            cancellationToken: context.RequestAborted);
    }
}
