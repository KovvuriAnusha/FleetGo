namespace FleetGo.API.Data.Entities;

/// <summary>Delivery status of a single <see cref="Package"/>.</summary>
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
