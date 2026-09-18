namespace FleetGo.API.Data.Entities;

/// <summary>Delivery status of a single <see cref="Stop"/> on a route.</summary>
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
