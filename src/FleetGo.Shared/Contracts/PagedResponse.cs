namespace FleetGo.Shared.Contracts;

/// <summary>
/// A single page of results, returned by every list/query endpoint in the API. The shape is
/// deliberately generic and reused across every resource type rather than hand-rolled per
/// endpoint, so a client only needs to learn it once.
/// </summary>
/// <typeparam name="T">The item type contained in this page.</typeparam>
/// <param name="Items">The items on this page, already filtered, sorted and paged.</param>
/// <param name="Page">The 1-based page number this page represents.</param>
/// <param name="PageSize">The maximum number of items per page that was used to produce this page.</param>
/// <param name="TotalCount">Total number of items matching the request's filters, ignoring paging.</param>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    /// <summary>
    /// Total number of pages implied by <see cref="TotalCount"/> and <see cref="PageSize"/>.
    /// Zero when there are no matching items.
    /// </summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
