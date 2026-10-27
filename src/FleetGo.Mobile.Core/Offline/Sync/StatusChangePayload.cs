using FleetGo.Mobile.Core.Offline.Models;
using FleetGo.Shared.Contracts.Fleet;

namespace FleetGo.Mobile.Core.Offline.Sync;

/// <summary>
/// What a queued status change actually contains: the new status the driver chose, and a
/// snapshot of the record as this device last knew it from the server.
/// <para>
/// The snapshot is not bookkeeping. The API's update endpoints are full replacements - there
/// is no PATCH and no way to send "only the status" - so the sync engine has to supply every
/// other field too. Sending the snapshot's values back would quietly undo anything dispatch
/// changed in the meantime, so the snapshot is used the other way round: as the thing to
/// compare fresh server state against. Same server state as when this was queued means the
/// change is safe to apply on top of it; different means a conflict a person has to settle.
/// </para>
/// </summary>
public sealed record StatusChangePayload
{
    /// <summary>The status name the driver chose - <c>Arrived</c>, <c>Completed</c> and so on.</summary>
    public string NewStatus { get; init; } = string.Empty;

    /// <summary>The record as the server last reported it, at the moment this was queued.</summary>
    public EntitySnapshot Baseline { get; init; } = new();
}

/// <summary>
/// The server-side state of one route, stop or package, flattened into a single shape.
/// <para>
/// One type rather than three because the alternative - a polymorphic payload with a
/// discriminator - buys nothing here: the operation row already records which entity type it
/// targets, and the fields that do not apply are simply null. Three near-identical records
/// and a converter would be architecture for its own sake.
/// </para>
/// </summary>
public sealed record EntitySnapshot
{
    /// <summary>The status the server had. A change here means someone else moved the record.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// The server's own last-modified stamp. The API has no ETag, version column or
    /// <c>If-Match</c> support, so this is the closest thing to a version it offers - and it
    /// is the server's value, not one this app invented.
    /// </summary>
    public DateTime UpdatedAtUtc { get; init; }

    // Route
    public string? RouteNumber { get; init; }

    public Guid? VehicleId { get; init; }

    /// <summary>ISO yyyy-MM-dd, matching how the local store holds dates.</summary>
    public string? RouteDate { get; init; }

    // Stop
    public Guid? CustomerId { get; init; }

    public int? Sequence { get; init; }

    public string? DeliveryNotes { get; init; }

    // Package
    public string? TrackingNumber { get; init; }

    public string? Description { get; init; }

    /// <summary>
    /// True when the parts of the record that this app is <em>not</em> changing still look the
    /// way they did when the change was queued.
    /// <para>
    /// Compared per entity type rather than field by field across the whole record, so a null
    /// that simply does not apply to a package cannot masquerade as "dispatch cleared it".
    /// Status is checked too: someone else moving a stop to Failed while the driver's
    /// "Completed" sat in the queue is precisely the case this has to catch.
    /// </para>
    /// </summary>
    public bool MatchesServerState(OutboxEntityType entityType, EntitySnapshot server)
    {
        ArgumentNullException.ThrowIfNull(server);

        if (!string.Equals(Status, server.Status, StringComparison.Ordinal) ||
            UpdatedAtUtc != server.UpdatedAtUtc)
        {
            return false;
        }

        return entityType switch
        {
            OutboxEntityType.Route =>
                string.Equals(RouteNumber, server.RouteNumber, StringComparison.Ordinal) &&
                VehicleId == server.VehicleId &&
                string.Equals(RouteDate, server.RouteDate, StringComparison.Ordinal),

            OutboxEntityType.Stop =>
                CustomerId == server.CustomerId &&
                Sequence == server.Sequence &&
                string.Equals(DeliveryNotes, server.DeliveryNotes, StringComparison.Ordinal),

            OutboxEntityType.Package =>
                string.Equals(TrackingNumber, server.TrackingNumber, StringComparison.Ordinal) &&
                string.Equals(Description, server.Description, StringComparison.Ordinal),

            _ => false,
        };
    }

    /// <summary>
    /// Describes, without naming the customer or repeating delivery notes, what moved. Kept
    /// deliberately vague about content: this string is shown in the UI and written to the
    /// outbox row, and neither is a place for a customer's address or a driver's notes.
    /// </summary>
    public string DescribeDifference(OutboxEntityType entityType, EntitySnapshot server)
    {
        ArgumentNullException.ThrowIfNull(server);

        if (!string.Equals(Status, server.Status, StringComparison.Ordinal))
        {
            return $"The server now shows this as {server.Status}, not {Status}.";
        }

        return entityType switch
        {
            OutboxEntityType.Route => "The route's details were changed on the server after this was queued.",
            OutboxEntityType.Stop => "The stop's details were changed on the server after this was queued.",
            OutboxEntityType.Package => "The package's details were changed on the server after this was queued.",
            _ => "This record was changed on the server after the change was queued.",
        };
    }

    // --- building a snapshot ----------------------------------------------------------
    //
    // Two sources, deliberately: the wire contract (what the server just said) and the local
    // record (what the cache holds). They have to produce identical snapshots for the same
    // server state, which is why both live here rather than being written out at each call
    // site.

    public static EntitySnapshot FromRoute(RouteResponse route)
    {
        ArgumentNullException.ThrowIfNull(route);

        return new EntitySnapshot
        {
            Status = route.Status.ToString(),
            UpdatedAtUtc = route.UpdatedAtUtc,
            RouteNumber = route.RouteNumber,
            VehicleId = route.VehicleId,
            RouteDate = route.RouteDate.ToString("yyyy-MM-dd"),
        };
    }

    public static EntitySnapshot FromStop(StopResponse stop)
    {
        ArgumentNullException.ThrowIfNull(stop);

        return new EntitySnapshot
        {
            Status = stop.Status.ToString(),
            UpdatedAtUtc = stop.UpdatedAtUtc,
            CustomerId = stop.CustomerId,
            Sequence = stop.Sequence,
            DeliveryNotes = stop.DeliveryNotes,
        };
    }

    public static EntitySnapshot FromPackage(PackageResponse package)
    {
        ArgumentNullException.ThrowIfNull(package);

        return new EntitySnapshot
        {
            Status = package.Status.ToString(),
            UpdatedAtUtc = package.UpdatedAtUtc,
            TrackingNumber = package.TrackingNumber,
            Description = package.Description,
        };
    }

    public static EntitySnapshot FromLocalRoute(LocalRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);

        return new EntitySnapshot
        {
            Status = route.Status,
            UpdatedAtUtc = route.UpdatedAtUtc,
            RouteNumber = route.RouteNumber,
            VehicleId = route.VehicleId,
            RouteDate = route.RouteDate,
        };
    }

    public static EntitySnapshot FromLocalStop(LocalStop stop)
    {
        ArgumentNullException.ThrowIfNull(stop);

        return new EntitySnapshot
        {
            Status = stop.Status,
            UpdatedAtUtc = stop.UpdatedAtUtc,
            CustomerId = stop.CustomerId,
            Sequence = stop.Sequence,
            DeliveryNotes = stop.DeliveryNotes,
        };
    }

    public static EntitySnapshot FromLocalPackage(LocalPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        return new EntitySnapshot
        {
            Status = package.Status,
            UpdatedAtUtc = package.UpdatedAtUtc,
            TrackingNumber = package.TrackingNumber,
            Description = package.Description,
        };
    }
}
