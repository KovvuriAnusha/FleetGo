using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FleetGo.Mobile.Core.Offline.Sync;
using FleetGo.Mobile.Core.Threading;

namespace FleetGo.Mobile.Core.ViewModels;

/// <summary>
/// The sync banner every driver screen shows, and the one place the app is allowed to make a
/// claim about how current its data is.
/// </summary>
/// <remarks>
/// <para>
/// The rule this type exists to keep is honesty. An offline-first app is only trustworthy if
/// the screen never implies more than it knows: a list rendered from a cache that was last
/// refreshed on Tuesday must not look identical to one that came off the wire a second ago,
/// and a stop the driver marked delivered in a car park must not look saved when it is still
/// sitting in a queue. So there is no optimistic wording here, no "Synced" that really means
/// "queued", and no silent success - a conflict says a person has to look at it, because that
/// is what is true.
/// </para>
/// <para>
/// Registered as a singleton: one banner state shared by every screen, so the dashboard and
/// the stop detail page can never disagree about whether the device is online.
/// </para>
/// </remarks>
public sealed partial class SyncStatusViewModel : ObservableObject, IDisposable
{
    private readonly ISyncStatusProvider _statusProvider;
    private readonly ISyncTrigger _syncTrigger;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;

    public SyncStatusViewModel(
        ISyncStatusProvider statusProvider,
        ISyncTrigger syncTrigger,
        IUiDispatcher dispatcher,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(statusProvider);
        ArgumentNullException.ThrowIfNull(syncTrigger);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _statusProvider = statusProvider;
        _syncTrigger = syncTrigger;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;

        _statusProvider.Changed += OnStatusChanged;
        Apply(_statusProvider.Current);
    }

    [ObservableProperty]
    public partial bool IsOnline { get; set; }

    [ObservableProperty]
    public partial bool IsSyncing { get; set; }

    [ObservableProperty]
    public partial int PendingCount { get; set; }

    [ObservableProperty]
    public partial int ConflictCount { get; set; }

    [ObservableProperty]
    public partial int FailedCount { get; set; }

    /// <summary>The one line the banner shows. Never optimistic - see the remarks on this class.</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = "Checking…";

    /// <summary>"Last synced 4 minutes ago", or the honest admission that it never has been.</summary>
    [ObservableProperty]
    public partial string LastSyncedText { get; set; } = "Not synced yet";

    /// <summary>True when something is wrong that another retry will not fix.</summary>
    [ObservableProperty]
    public partial bool NeedsAttention { get; set; }

    /// <summary>
    /// False only when the device is online, the queue is empty and a sync has actually
    /// happened - the one situation where there is nothing worth saying.
    /// </summary>
    [ObservableProperty]
    public partial bool IsBannerVisible { get; set; }

    /// <summary>Pull-to-refresh and the "Try now" button both come here.</summary>
    [RelayCommand]
    private async Task SyncNowAsync(CancellationToken cancellationToken) =>
        await _syncTrigger.SyncNowAsync(SyncReason.Manual, cancellationToken);

    public void Dispose() => _statusProvider.Changed -= OnStatusChanged;

    /// <summary>
    /// The sync engine raises its events from whatever thread it happened to be on. Property
    /// changes have to reach the UI thread, or the bindings they drive become an intermittent
    /// crash rather than a repaint.
    /// </summary>
    private void OnStatusChanged(object? sender, SyncSnapshot snapshot) => _dispatcher.Post(() => Apply(snapshot));

    private void Apply(SyncSnapshot snapshot)
    {
        IsOnline = snapshot.IsOnline;
        IsSyncing = snapshot.IsSyncing;
        PendingCount = snapshot.PendingCount;
        ConflictCount = snapshot.ConflictCount;
        FailedCount = snapshot.FailedCount;
        NeedsAttention = snapshot.NeedsAttention;

        LastSyncedText = DescribeLastSync(snapshot.LastSuccessfulSyncUtc);
        StatusText = DescribeStatus(snapshot);
        IsBannerVisible = !snapshot.IsOnline
            || snapshot.HasUnsentWork
            || snapshot.IsSyncing
            || snapshot.LastSuccessfulSyncUtc is null
            || snapshot.LastError is not null;
    }

    private string DescribeStatus(SyncSnapshot snapshot)
    {
        if (snapshot.IsSyncing)
        {
            return "Syncing…";
        }

        if (snapshot.ConflictCount > 0)
        {
            return snapshot.ConflictCount == 1
                ? "1 change clashed with the server and needs you to decide."
                : $"{snapshot.ConflictCount} changes clashed with the server and need you to decide.";
        }

        if (snapshot.FailedCount > 0)
        {
            return snapshot.FailedCount == 1
                ? "1 change couldn't be saved."
                : $"{snapshot.FailedCount} changes couldn't be saved.";
        }

        if (!snapshot.IsOnline)
        {
            return snapshot.PendingCount > 0
                ? $"Offline. {Changes(snapshot.PendingCount)} waiting to send."
                : "Offline. Showing saved data.";
        }

        if (snapshot.PendingCount > 0)
        {
            return $"{Changes(snapshot.PendingCount)} waiting to send.";
        }

        if (snapshot.LastError is { } error)
        {
            return error;
        }

        return snapshot.LastSuccessfulSyncUtc is null
            ? "Not synced yet."
            : "Up to date.";
    }

    private string DescribeLastSync(DateTime? lastSyncUtc)
    {
        if (lastSyncUtc is not { } synced)
        {
            return "Not synced yet";
        }

        TimeSpan age = _timeProvider.GetUtcNow().UtcDateTime - synced;

        // A clock that has moved backwards - a manual time change, a timezone fix applied
        // while the app was running - should not produce "last synced in -3 minutes".
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age < TimeSpan.FromMinutes(1))
        {
            return "Last synced just now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            int minutes = (int)age.TotalMinutes;
            return $"Last synced {minutes} minute{(minutes == 1 ? string.Empty : "s")} ago";
        }

        if (age < TimeSpan.FromDays(1))
        {
            int hours = (int)age.TotalHours;
            return $"Last synced {hours} hour{(hours == 1 ? string.Empty : "s")} ago";
        }

        // SpecifyKind, not ToLocalTime alone: a DateTime read back out of SQLite comes back
        // Unspecified, and ToLocalTime would then treat a UTC value as if it were already
        // local and shift it a second time.
        DateTime local = DateTime.SpecifyKind(synced, DateTimeKind.Utc).ToLocalTime();
        return "Last synced " + local.ToString("d MMM, HH:mm", CultureInfo.CurrentCulture);
    }

    private static string Changes(int count) => count == 1 ? "1 change" : $"{count} changes";
}
