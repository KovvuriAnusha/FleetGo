using System.Reflection;
using FleetGo.Shared;
using FleetGo.Shared.Contracts;

namespace FleetGo.API.Endpoints;

/// <summary>
/// Endpoints that describe the API itself. Grouping route registration into
/// static extension methods keeps Program.cs readable as the API grows - each
/// feature area gets one Map*Endpoints file instead of a 400-line startup class.
/// </summary>
internal static class SystemEndpoints
{
    /// <summary>
    /// Informational version stamped into the assembly at build time. The suffix
    /// after '+' is the source-control commit hash, which is noise for clients.
    /// </summary>
    private static readonly string BuildVersion =
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            .Split('+')[0]
        ?? "unknown";

    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Routes come from the shared ApiRoutes constants, so the server and the
        // mobile client can never drift apart on a URL.
        endpoints.MapGet(ApiRoutes.SystemInfo, (IHostEnvironment environment, TimeProvider timeProvider) =>
                TypedResults.Ok(new ApiInfoResponse(
                    Name: "FleetGo API",
                    Version: BuildVersion,
                    Environment: environment.EnvironmentName,
                    ServerTimeUtc: timeProvider.GetUtcNow())))
            .WithName("GetApiInfo")
            .WithSummary("Returns metadata about the running API instance.")
            .WithDescription("Used by clients to confirm which backend build and environment they are talking to.")
            .WithTags("System");

        return endpoints;
    }
}
