namespace FleetGo.API.Data.Entities;

/// <summary>Operational status of a <see cref="Vehicle"/>.</summary>
public enum VehicleStatus
{
    /// <summary>In active service and available for route assignment.</summary>
    Active,

    /// <summary>Temporarily out of service for maintenance or repair.</summary>
    InMaintenance,

    /// <summary>Permanently removed from the fleet. Kept for history rather than deleted.</summary>
    Retired,
}
