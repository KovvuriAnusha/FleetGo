using FleetGo.API.Endpoints;
using FleetGo.API.Health;
using FleetGo.Shared;
using FleetGo.Shared.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Services (the composition root - everything the app can resolve is declared here)
// ---------------------------------------------------------------------------

// Injected instead of using DateTimeOffset.UtcNow directly, so time can be
// frozen in tests.
builder.Services.AddSingleton(TimeProvider.System);

// Serialise responses with the same source-generated metadata the mobile client
// uses to read them.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, FleetGoJsonSerializerContext.Default));

// RFC 9457 problem+json for every unhandled error and every bare status code,
// so clients get one predictable error shape instead of HTML error pages.
builder.Services.AddProblemDetails();

// Liveness/readiness probes. Later phases add checks for SQL Server and any
// external dependency, tagged "ready" so orchestrators can tell "process is up"
// apart from "can actually serve traffic".
builder.Services.AddHealthChecks()
    .AddCheck(
        name: "self",
        check: () => HealthCheckResult.Healthy("The API process is running."),
        tags: ["live", "ready"]);

// OpenAPI document generation. The document is served from /openapi/v1.json and
// rendered by Scalar in development.
builder.Services.AddOpenApi(ApiRoutes.Version, options =>
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        if (document.Info is not null)
        {
            document.Info.Title = "FleetGo API";
            document.Info.Version = ApiRoutes.Version;
            document.Info.Description =
                "Backend for the FleetGo driver and delivery application.";
        }

        return Task.CompletedTask;
    }));

WebApplication app = builder.Build();

// ---------------------------------------------------------------------------
// HTTP pipeline (order matters - each piece wraps the ones registered after it)
// ---------------------------------------------------------------------------

app.UseExceptionHandler();   // turns unhandled exceptions into problem+json
app.UseStatusCodePages();    // turns bare 404/405 responses into problem+json

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();                 // /openapi/v1.json
    app.MapScalarApiReference();      // /scalar/v1 - interactive API reference
}
else
{
    // Only enforced outside development: the Android emulator talks to the host
    // over plain HTTP, and a redirect would break it.
    app.UseHttpsRedirection();
}

// ---------------------------------------------------------------------------
// Endpoints
// ---------------------------------------------------------------------------

app.MapHealthChecks(ApiRoutes.Health, new HealthCheckOptions
{
    ResponseWriter = HealthReportResponseWriter.WriteAsync,
});

app.MapHealthChecks(ApiRoutes.HealthLive, new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
    ResponseWriter = HealthReportResponseWriter.WriteAsync,
});

app.MapHealthChecks(ApiRoutes.HealthReady, new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthReportResponseWriter.WriteAsync,
});

app.MapSystemEndpoints();

app.Run();

/// <summary>
/// Exposed so the integration test project can boot this exact host through
/// <c>WebApplicationFactory&lt;Program&gt;</c>. Top-level statements generate an
/// internal Program class, which the test project could not otherwise reach.
/// </summary>
public partial class Program;
