using System.Text.Json.Serialization;

namespace FleetGo.Shared.Contracts.Fleet;

/// <summary>
/// Wire-contract mirror of <c>FleetGo.API.Data.Entities.PackageStatus</c>. Kept as a
/// separate type (rather than shared) because <c>FleetGo.Shared</c> has no reference to
/// <c>FleetGo.API</c> - the two are mapped explicitly at the API boundary.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PackageStatus
{
    /// <summary>Loaded and awaiting delivery.</summary>
    Pending,

    /// <summary>Out on the vehicle for delivery.</summary>
    OutForDelivery,

    /// <summary>Successfully delivered.</summary>
    Delivered,

    /// <summary>Delivery attempt failed.</summary>
    Failed,

    /// <summary>Returned to the depot without being delivered.</summary>
    Returned,
}
