using SQLite;

namespace FleetGo.Mobile.Core.Offline.Models;

/// <summary>
/// One row describing the cache as a whole.
/// <para>
/// <see cref="OwnerDriverId"/> is the data-isolation guard: the cache belongs to exactly one
/// driver, and the store wipes itself if a different one signs in on the same device. It holds
/// no credentials - tokens stay in SecureStorage.
/// </para>
/// </summary>
[Table("sync_metadata")]
public sealed class SyncMetadata
{
    /// <summary>Always 1 - this table holds a single row.</summary>
    public const int SingletonId = 1;

    [PrimaryKey]
    public int Id { get; set; } = SingletonId;

    public Guid? OwnerDriverId { get; set; }

    public DateTime? LastSuccessfulSyncUtc { get; set; }

    public string? LastSyncError { get; set; }
}
