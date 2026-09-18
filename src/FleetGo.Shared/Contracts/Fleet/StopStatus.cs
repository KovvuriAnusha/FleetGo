using System.Text.Json.Serialization;

namespace FleetGo.Shared.Contracts.Fleet;

/// <summary>
/// Wire-contract mirror of <c>FleetGo.API.Data.Entities.StopStatus</c>. Kept as a
/// separate type (rather than shared) because <c>FleetGo.Shared</c> has no reference to
/// <c>FleetGo.API</c> - the two are mapped explicitly at the API boundary.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StopStatus
{
    /// <summary>Not yet reached.</summary>
    Pending,

    /// <summary>Reached, delivery/collection in progress. Set by the driver on arrival.</summary>
    Arrived,

    /// <summary>Successfully completed.</summary>
    Completed,

    /// <summary>Attempted but not completed (e.g. no one available, address inaccessible).</summary>
    Failed,

    /// <summary>Deliberately not attempted this run (e.g. re-scheduled).</summary>
    Skipped,
}
