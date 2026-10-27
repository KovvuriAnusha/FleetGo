using SQLite;

namespace FleetGo.Mobile.Core.Offline.Models;

/// <summary>
/// The locally cached shape of the driver's working day.
/// <para>
/// These are deliberately NOT the API's EF Core entities, and not the wire contracts either.
/// They are flattened for the questions the screens actually ask - "this driver's routes for
/// today", "this route's stops in order" - so a read is one indexed query rather than a join
/// the device has to reassemble. Dates and statuses are stored as text: SQLite has no native
/// type for either, and text keeps a database opened by a future version readable rather than
/// silently reinterpreting an enum whose numbering moved.
/// </para>
/// <para>
/// Every record carries <c>SyncedAtUtc</c> (when this device last heard it from the server)
/// and <c>HasPendingLocalChange</c> (an outbox operation is queued against it), which is what
/// lets the UI say "saved data" or "waiting to sync" honestly rather than implying it just
/// came off the wire.
/// </para>
/// </summary>
[Table("local_routes")]
public sealed class LocalRoute
{
    [PrimaryKey]
    public Guid Id { get; set; }

    public string RouteNumber { get; set; } = string.Empty;

    public Guid DriverId { get; set; }

    public string? DriverCode { get; set; }

    public Guid? VehicleId { get; set; }

    public string? VehicleRegistrationNumber { get; set; }

    /// <summary>ISO yyyy-MM-dd. Text sorts and compares correctly in that format, which is what the dashboard's "today" query needs.</summary>
    [Indexed(Name = "ix_local_routes_date", Order = 1)]
    public string RouteDate { get; set; } = string.Empty;

    /// <summary>The contract enum's name, not its number.</summary>
    [Indexed(Name = "ix_local_routes_status", Order = 1)]
    public string Status { get; set; } = string.Empty;

    public int StopCount { get; set; }

    /// <summary>The server's own last-updated stamp, used for conflict detection.</summary>
    public DateTime UpdatedAtUtc { get; set; }

    public DateTime SyncedAtUtc { get; set; }

    public bool HasPendingLocalChange { get; set; }
}

[Table("local_stops")]
public sealed class LocalStop
{
    [PrimaryKey]
    public Guid Id { get; set; }

    [Indexed(Name = "ix_local_stops_route", Order = 1)]
    public Guid RouteId { get; set; }

    public Guid CustomerId { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Position on the route. Second column of the route index so stops come back in running order.</summary>
    [Indexed(Name = "ix_local_stops_route", Order = 2)]
    public int Sequence { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? DeliveryNotes { get; set; }

    public int PackageCount { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime SyncedAtUtc { get; set; }

    public bool HasPendingLocalChange { get; set; }
}

[Table("local_packages")]
public sealed class LocalPackage
{
    [PrimaryKey]
    public Guid Id { get; set; }

    [Indexed(Name = "ix_local_packages_stop", Order = 1)]
    public Guid StopId { get; set; }

    public string TrackingNumber { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime SyncedAtUtc { get; set; }

    public bool HasPendingLocalChange { get; set; }
}

/// <summary>Shared fleet data, cached only as far as the screens need it.</summary>
[Table("local_vehicles")]
public sealed class LocalVehicle
{
    [PrimaryKey]
    public Guid Id { get; set; }

    public string RegistrationNumber { get; set; } = string.Empty;

    public string Make { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public int? Year { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime SyncedAtUtc { get; set; }
}

[Table("local_customers")]
public sealed class LocalCustomer
{
    [PrimaryKey]
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public string? Email { get; set; }

    public string AddressLine1 { get; set; } = string.Empty;

    public string? AddressLine2 { get; set; }

    public string City { get; set; } = string.Empty;

    public string? State { get; set; }

    public string PostalCode { get; set; } = string.Empty;

    public string? Country { get; set; }

    public DateTime SyncedAtUtc { get; set; }
}
