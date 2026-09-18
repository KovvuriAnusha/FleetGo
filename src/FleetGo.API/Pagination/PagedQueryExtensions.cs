using FleetGo.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FleetGo.API.Pagination;

/// <summary>
/// Applies paging to an already-filtered-and-sorted <see cref="IQueryable{T}"/> and shapes
/// the result into the wire-level <see cref="PagedResponse{T}"/>.
/// <para>
/// Both the count and the page of rows are produced by the database - the caller's query
/// must reach this method unmaterialized (no <c>ToList</c>/<c>ToArray</c> beforehand) so
/// <c>Skip</c>/<c>Take</c> translate to SQL instead of paging an in-memory collection.
/// </para>
/// </summary>
internal static class PagedQueryExtensions
{
    public static async Task<PagedResponse<T>> ToPagedResponseAsync<T>(
        this IQueryable<T> query,
        PageRequest pageRequest,
        CancellationToken cancellationToken)
    {
        int totalCount = await query.CountAsync(cancellationToken);

        List<T> items = totalCount == 0
            ? []
            : await query
                .Skip((pageRequest.Page - 1) * pageRequest.PageSize)
                .Take(pageRequest.PageSize)
                .ToListAsync(cancellationToken);

        return new PagedResponse<T>(items, pageRequest.Page, pageRequest.PageSize, totalCount);
    }
}
