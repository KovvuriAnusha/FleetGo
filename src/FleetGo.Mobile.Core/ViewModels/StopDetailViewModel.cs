using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FleetGo.Mobile.Core.Offline;
using FleetGo.Mobile.Core.Offline.Models;
using FleetGo.Mobile.Core.Offline.Sync;
using FleetGo.Shared.Contracts.Fleet;

namespace FleetGo.Mobile.Core.ViewModels;

/// <summary>One status a driver can move a stop to, with the wording that goes on the button.</summary>
public sealed record StopStatusAction(string Label, StopStatus Value);

/// <summary>
/// Backs the stop detail screen: one stop, the customer it delivers to, the packages going
/// there - and the one place in the app where a driver changes something.
/// </summary>
/// <remarks>
/// <para>
/// The status buttons are what Phase 5 is ultimately for. Pressing one does not call the API.
/// It writes the change to the device, queues it, and says so - so the same tap behaves
/// identically in a depot with full signal and in a stairwell with none, and the driver is
/// never left guessing which of those just happened.
/// </para>
/// <para>
/// Only statuses are offered here. The API's update endpoints can also change a stop's
/// customer, sequence and delivery notes, but those are dispatch decisions rather than
/// driver ones, and queueing them offline would invent a workflow the product does not have.
/// Phase 5 deliberately does not add speculative endpoints or speculative offline actions -
/// what is here is what the API already supports and a driver already does.
/// </para>
/// </remarks>
public sealed partial class StopDetailViewModel : FleetPageViewModel
{
    private readonly IFleetRepository _repository;
    private readonly ISyncTrigger _syncTrigger;

    /// <summary>Remembered so <see cref="ReloadCommand"/> can retry without the page passing the id again.</summary>
    private Guid _stopId;

    /// <summary>The queued change for this stop, when there is one that has not settled.</summary>
    private OutboxOperation? _unresolvedChange;

    public StopDetailViewModel(IFleetRepository repository, ISyncTrigger syncTrigger, SyncStatusViewModel sync)
        : base(sync)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(syncTrigger);

        _repository = repository;
        _syncTrigger = syncTrigger;
    }

    public ObservableCollection<PackageResponse> Packages { get; } = [];

    /// <summary>What a driver can move this stop to. Fixed, and deliberately short.</summary>
    public IReadOnlyList<StopStatusAction> StatusActions { get; } =
    [
        new StopStatusAction("Arrived", StopStatus.Arrived),
        new StopStatusAction("Delivered", StopStatus.Completed),
        new StopStatusAction("Failed", StopStatus.Failed),
        new StopStatusAction("Skipped", StopStatus.Skipped),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStop))]
    public partial StopResponse? Stop { get; set; }

    /// <summary>
    /// The customer's full record - address, phone, email - which the stop summary does not
    /// carry. Null when it has not been cached on this device; the customer's name still
    /// shows, because it travels on the stop itself.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomer))]
    public partial CustomerResponse? Customer { get; set; }

    /// <summary>True once the stop loaded and genuinely has no packages on it.</summary>
    [ObservableProperty]
    public partial bool HasNoPackages { get; set; }

    /// <summary>
    /// "Waiting to send: Delivered", when a change has been made but not yet accepted by the
    /// server. Null when there is nothing outstanding.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingChangeNote))]
    public partial string? PendingChangeNote { get; set; }

    /// <summary>
    /// What the server said instead, when this stop's change could not be applied. Never
    /// cleared by the app on its own - a conflict leaves this screen only when the driver
    /// picks one of the two ways out.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflict))]
    public partial string? ConflictNote { get; set; }

    public bool HasStop => Stop is not null;

    public bool HasCustomer => Customer is not null;

    public bool HasPendingChangeNote => !string.IsNullOrEmpty(PendingChangeNote);

    public bool HasConflict => !string.IsNullOrEmpty(ConflictNote);

    /// <summary>Loads the stop, its customer and its packages from the device.</summary>
    public async Task LoadAsync(Guid stopId, CancellationToken cancellationToken = default)
    {
        _stopId = stopId;

        bool succeeded = await RunAsync(token => LoadFromDeviceAsync(stopId, token), cancellationToken);

        HasNoPackages = succeeded && HasStop && Packages.Count == 0;
    }

    /// <summary>Retry after a failure, and the pull-to-refresh action.</summary>
    [RelayCommand]
    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        Guid stopId = _stopId;

        bool succeeded = await RunAsync(
            async token =>
            {
                await TryRefreshAsync(() => _repository.RefreshStopDetailAsync(stopId, token));
                await LoadFromDeviceAsync(stopId, token);
            },
            cancellationToken);

        HasNoPackages = succeeded && HasStop && Packages.Count == 0;
    }

    /// <summary>
    /// Records a status change. Applied to the device immediately, queued for the server, and
    /// nudged along with a sync attempt that is allowed to fail quietly - the change is
    /// already safe on disk by then.
    /// </summary>
    [RelayCommand]
    private async Task SetStatusAsync(StopStatusAction? action, CancellationToken cancellationToken)
    {
        if (action is null || Stop is null)
        {
            return;
        }

        Guid stopId = _stopId;

        await RunAsync(
            async token =>
            {
                QueuedChangeResult result = await _repository.QueueStopStatusAsync(stopId, action.Value, token);

                if (!result.Queued)
                {
                    ErrorMessage = result.Message;
                    return;
                }

                await LoadFromDeviceAsync(stopId, token);

                // Fire-and-forget on purpose: the driver's change is already durable, and
                // making them wait on a network round trip to see the button take effect is
                // exactly the delay offline-first exists to remove.
                _syncTrigger.RequestSync(SyncReason.ChangeQueued);
            },
            cancellationToken,
            showBusyIndicator: false);
    }

    /// <summary>
    /// Conflict resolution, option one: drop this device's change. The stop goes back to
    /// whatever the server says at the next refresh.
    /// </summary>
    [RelayCommand]
    private async Task DiscardChangeAsync(CancellationToken cancellationToken)
    {
        if (_unresolvedChange is not { } change)
        {
            return;
        }

        Guid stopId = _stopId;

        await RunAsync(
            async token =>
            {
                await _repository.DiscardQueuedChangeAsync(change.Id, token);
                await TryRefreshAsync(() => _repository.RefreshStopDetailAsync(stopId, token));
                await LoadFromDeviceAsync(stopId, token);
            },
            cancellationToken,
            showBusyIndicator: false);
    }

    /// <summary>
    /// Conflict resolution, option two: keep this device's change and apply it on top of what
    /// the server has now. The same status is queued afresh, so the comparison the sync engine
    /// makes is against the server's current state rather than the stale one that caused the
    /// clash, and the operation that conflicted is then dropped.
    /// <para>
    /// <b>The order matters, and it is the whole point of this method.</b> Queueing comes
    /// first; the conflicted operation is discarded only once a replacement is safely on disk.
    /// Discarding first looks equivalent and is not: the queue can refuse - the session may
    /// have expired (a conflict and an expired session arrive from the same sync run), another
    /// driver may have signed in and cleared the cache, or the queue may be at its ceiling -
    /// and a refusal after the delete would leave the driver with no record of the status they
    /// had just chosen to keep, reported through a message about something else entirely.
    /// </para>
    /// <para>
    /// Queueing first is safe because a conflicted operation is invisible to the outbox's
    /// de-duplication: <c>FindPendingOperationAsync</c> matches on <c>Pending</c> only, so the
    /// replacement is always a new row and can neither absorb nor mutate the one it replaces.
    /// It also costs nothing, because the queue ceiling counts pending work only - discarding
    /// first would not have freed a slot. The worst case of this ordering is both rows
    /// existing for a moment, which is visible, recoverable, and strictly better than losing
    /// the driver's work.
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task KeepChangeAsync(CancellationToken cancellationToken)
    {
        if (_unresolvedChange is not { } change ||
            OfflineJson.TryDeserialize(change.PayloadJson) is not { } payload ||
            !Enum.TryParse(payload.NewStatus, out StopStatus intended))
        {
            return;
        }

        Guid stopId = _stopId;

        await RunAsync(
            async token =>
            {
                QueuedChangeResult result = await _repository.QueueStopStatusAsync(stopId, intended, token);

                if (!result.Queued)
                {
                    // Nothing has been changed: the conflict is still queued, still carrying
                    // the status the driver chose, and the screen still offers both ways out.
                    ErrorMessage = result.Message;
                    return;
                }

                // Only discard a *different* row. If queueing coalesced into the operation
                // being replaced - which the outbox does for anything still pending - then
                // the replacement and the original are the same row, and deleting it here
                // would throw away the change that was just accepted. The buttons that reach
                // this method are shown only for a conflicted or failed operation, neither of
                // which coalesces, so this is belt and braces rather than a live path; it is
                // here because the cost of being wrong is a lost delivery.
                if (result.OperationId != change.Id)
                {
                    await _repository.DiscardQueuedChangeAsync(change.Id, token);
                }

                await LoadFromDeviceAsync(stopId, token);
                _syncTrigger.RequestSync(SyncReason.ChangeQueued);
            },
            cancellationToken,
            showBusyIndicator: false);
    }

    private async Task LoadFromDeviceAsync(Guid stopId, CancellationToken cancellationToken)
    {
        CachedData<StopDetail> cached = await _repository.GetStopDetailAsync(stopId, cancellationToken);

        Stop = cached.Value.Stop;
        Customer = cached.Value.Customer;
        HasPendingLocalChange = cached.HasPendingLocalChange;

        Packages.Clear();

        foreach (PackageResponse package in cached.Value.Packages)
        {
            Packages.Add(package);
        }

        await LoadQueuedChangeAsync(stopId, cancellationToken);

        if (Stop is null)
        {
            ErrorMessage = "This stop isn't saved on this device yet. Connect to a network and pull down to refresh.";
        }
    }

    /// <summary>
    /// Finds this stop's outstanding change, if any, and turns it into the two notes the page
    /// shows. Read back from the queue every time rather than remembered, so the screen cannot
    /// keep claiming something is pending after the sync engine has settled it.
    /// </summary>
    private async Task LoadQueuedChangeAsync(Guid stopId, CancellationToken cancellationToken)
    {
        _unresolvedChange = null;
        PendingChangeNote = null;
        ConflictNote = null;

        IReadOnlyList<OutboxOperation> unresolved = await _repository.GetUnresolvedChangesAsync(cancellationToken);

        OutboxOperation? change = unresolved.FirstOrDefault(operation =>
            operation.EntityId == stopId &&
            string.Equals(operation.EntityType, nameof(OutboxEntityType.Stop), StringComparison.Ordinal));

        if (change is null)
        {
            return;
        }

        _unresolvedChange = change;

        string? intended = OfflineJson.TryDeserialize(change.PayloadJson)?.NewStatus;

        if (string.Equals(change.Status, nameof(OutboxOperationStatus.Conflict), StringComparison.Ordinal))
        {
            ConflictNote = change.ConflictDetail ?? "This change clashed with the server's copy.";
            return;
        }

        if (string.Equals(change.Status, nameof(OutboxOperationStatus.Failed), StringComparison.Ordinal))
        {
            ConflictNote = change.LastError ?? "This change couldn't be saved.";
            return;
        }

        PendingChangeNote = intended is null
            ? "A change to this stop is waiting to send."
            : $"Waiting to send: {intended}";
    }
}
