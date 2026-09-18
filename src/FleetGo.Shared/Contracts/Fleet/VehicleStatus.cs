using System.Text.Json.Serialization;

namespace FleetGo.Shared.Contracts.Fleet;

/// <summary>
/// Wire-contract mirror of <c>FleetGo.API.Data.Entities.VehicleStatus</c>. Kept as a
/// separate type (rather than shared) because <c>FleetGo.Shared</c> has no reference to
/// <c>FleetGo.API</c> - the two are mapped explicitly at the API boundary.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VehicleStatus
{
    /// <summary>In active service and available for route assignment.</summary>
    Active,

    /// <summary>Temporarily out of service for maintenance or repair.</summary>
    InMaintenance,

    /// <summary>Permanently removed from the fleet. Kept for history rather than deleted.</summary>
    Retired,
}
