using SQLite;

namespace FleetGo.Mobile.Core.Offline.Models;

/// <summary>Which resource an outbox operation targets.</summary>
public enum OutboxEntityType
{
    Route,
    Stop,
    Package,
}

/// <summary>
/// What the operation does. Only status changes are queued: they are the mutations the driver
/// legitimately makes away from signal, and the only ones Phase 4A's API exposes that make
/// sense to defer. Creating routes, stops or packages is dispatch work, not driver work, and
/// is deliberately not offline-capable.
/// </summary>
public enum OutboxOperationType
{
    UpdateStatus,
}

/// <summary>Where an operation is in its life.</summary>
public enum OutboxOperationStatus
{
    /// <summary>Waiting to be sent, or waiting out a backoff delay.</summary>
    Pending,

    /// <summary>Accepted by the server. Kept briefly for the UI, then pruned.</summary>
    Completed,

    /// <summary>Rejected for a reason retrying cannot fix (validation, not found, forbidden).</summary>
    Failed,

    /// <summary>
    /// The server's copy changed after this was queued, or the server answered 409. Never
    /// retried automatically and never overwritten - a person decides.
    /// </summary>
    Conflict,
}

/// <summary>
/// One durable, queued mutation.
/// <para>
/// <see cref="Sequence"/> is the primary key and autoincrements, so "the order the driver did
/// things in" survives restarts and clock changes alike - ordering by a timestamp would tie
/// whenever two changes land in the same millisecond. <see cref="Id"/> is the stable public
/// operation id used in logs and by the UI.
/// </para>
/// </summary>
[Table("outbox_operations")]
public sealed class OutboxOperation
{
    [PrimaryKey, AutoIncrement]
    public int Sequence { get; set; }

    [Indexed(Name = "ix_outbox_id", Unique = true)]
    public Guid Id { get; set; }

    public string EntityType { get; set; } = string.Empty;

    [Indexed(Name = "ix_outbox_entity")]
    public Guid EntityId { get; set; }

    public string OperationType { get; set; } = string.Empty;

    /// <summary>
    /// The intended change plus the snapshot of the server state it was based on - see
    /// <c>StatusChangePayload</c>. Never contains credentials: an outbox row is a fleet
    /// instruction, and authentication is applied when it is sent.
    /// </summary>
    public string PayloadJson { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public int RetryCount { get; set; }

    public DateTime? LastAttemptAtUtc { get; set; }

    /// <summary>When the backoff allows another attempt. Null means "as soon as possible".</summary>
    public DateTime? NextAttemptAtUtc { get; set; }

    [Indexed(Name = "ix_outbox_status")]
    public string Status { get; set; } = nameof(OutboxOperationStatus.Pending);

    public string? LastError { get; set; }

    /// <summary>Human-readable description of what the server had instead. Set only for <see cref="OutboxOperationStatus.Conflict"/>.</summary>
    public string? ConflictDetail { get; set; }
}
