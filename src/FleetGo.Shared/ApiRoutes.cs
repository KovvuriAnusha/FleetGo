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

    /// <summary>Base path for authentication endpoints.</summary>
    public const string AuthBase = Base + "/auth";

    /// <summary>Exchanges an email/password pair for an access and refresh token.</summary>
    public const string AuthLogin = AuthBase + "/login";

    /// <summary>Exchanges a valid, unexpired refresh token for a new token pair.</summary>
    public const string AuthRefresh = AuthBase + "/refresh";

    /// <summary>Revokes a refresh token so it can no longer be used.</summary>
    public const string AuthLogout = AuthBase + "/logout";

    /// <summary>Returns the profile of the currently authenticated user.</summary>
    public const string AuthMe = AuthBase + "/me";

    /// <summary>Base path for one-time-code endpoints.</summary>
    public const string AuthOtpBase = AuthBase + "/otp";

    /// <summary>Requests a one-time login code be sent to the account's phone number on file.</summary>
    public const string AuthOtpRequest = AuthOtpBase + "/request";

    /// <summary>Exchanges a valid one-time code for an access and refresh token, exactly as a password login does.</summary>
    public const string AuthOtpVerify = AuthOtpBase + "/verify";

    /// <summary>Base path for fleet operations endpoints (vehicles, customers, routes, stops, packages).</summary>
    public const string FleetBase = Base + "/fleet";

    /// <summary>Base path for vehicle endpoints. A single vehicle is addressed as "{VehiclesBase}/{id}".</summary>
    public const string VehiclesBase = FleetBase + "/vehicles";

    /// <summary>Base path for customer endpoints. A single customer is addressed as "{CustomersBase}/{id}".</summary>
    public const string CustomersBase = FleetBase + "/customers";

    /// <summary>Base path for route endpoints. A single route is addressed as "{RoutesBase}/{id}".</summary>
    public const string RoutesBase = FleetBase + "/routes";

    /// <summary>
    /// Base path for stop endpoints. A single stop is addressed as "{StopsBase}/{id}"; the
    /// list endpoint accepts a "routeId" query parameter to scope results to one route.
    /// </summary>
    public const string StopsBase = FleetBase + "/stops";

    /// <summary>
    /// Base path for package endpoints. A single package is addressed as "{PackagesBase}/{id}";
    /// the list endpoint accepts a "stopId" query parameter to scope results to one stop.
    /// </summary>
    public const string PackagesBase = FleetBase + "/packages";
}

