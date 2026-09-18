namespace FleetGo.API.Data.Entities;

/// <summary>Lifecycle status of a <see cref="Route"/>.</summary>
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
