namespace FleetGo.Shared;

/// <summary>
/// Single source of truth for the HTTP routes exposed by the FleetGo API.
/// The server maps them and the mobile client calls them, so a typo can never
/// silently break only one side of the contract.
/// </summary>
public static class ApiRoutes
{
    /// <summary>Current API version segment.</summary>
    public const string Version = "v1";

    /// <summary>Base path all versioned endpoints hang off.</summary>
    public const string Base = "/api/" + Version;

    /// <summary>Returns metadata about the running API instance.</summary>
    public const string SystemInfo = Base + "/system/info";

    /// <summary>Aggregate health of the API and its dependencies.</summary>
    public const string Health = "/health";

    /// <summary>Liveness probe - is the process up at all?</summary>
    public const string HealthLive = "/health/live";

    /// <summary>Readiness probe - can the API serve traffic right now?</summary>
    public const string HealthReady = "/health/ready";
}
