namespace FleetGo.API.Auth;

/// <summary>Custom claim types carried in the FleetGo access token, beyond the standard registered ones.</summary>
internal static class FleetGoClaimTypes
{
    public const string DriverId = "driver_id";

    public const string DriverCode = "driver_code";
}

