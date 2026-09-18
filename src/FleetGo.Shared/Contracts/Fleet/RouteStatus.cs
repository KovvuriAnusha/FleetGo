using System.Text.Json.Serialization;

namespace FleetGo.Shared.Contracts.Fleet;

/// <summary>
/// Wire-contract mirror of <c>FleetGo.API.Data.Entities.RouteStatus</c>. Kept as a
/// separate type (rather than shared) because <c>FleetGo.Shared</c> has no reference to
/// <c>FleetGo.API</c> - the two are mapped explicitly at the API boundary.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RouteStatus
{
    /// <summary>Created and scheduled, not yet started.</summary>
    Planned,

    /// <summary>The driver has started working the route.</summary>
    InProgress,

    /// <summary>Every stop has been reached (or explicitly skipped) and the route is closed out.</summary>
    Completed,

    /// <summary>Called off before completion.</summary>
    Cancelled,
}
