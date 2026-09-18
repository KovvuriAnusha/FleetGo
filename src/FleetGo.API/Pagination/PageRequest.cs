using System.Diagnostics.CodeAnalysis;

namespace FleetGo.API.Pagination;

/// <summary>
/// A validated page/pageSize pair for a list endpoint. Constructed via <see cref="TryCreate"/>
/// so every list endpoint validates paging input the same way and returns a single,
/// consistent error message instead of each one hand-rolling range checks.
/// </summary>
internal sealed class PageRequest
{
    /// <summary>Page size used when the caller does not specify one.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>
    /// Largest page size a caller may request. Bounds how much a single query can pull back,
    /// regardless of how a filter is combined with paging.
    /// </summary>
    public const int MaxPageSize = 100;

    public int Page { get; }

    public int PageSize { get; }

    private PageRequest(int page, int pageSize)
    {
        Page = page;
        PageSize = pageSize;
    }

    /// <summary>
    /// Validates and, if valid, builds a <see cref="PageRequest"/> from raw query-string
    /// values. <paramref name="page"/> and <paramref name="pageSize"/> default to page 1 and
    /// <see cref="DefaultPageSize"/> respectively when omitted.
    /// </summary>
    /// <returns>True when the input was valid; false with <paramref name="error"/> set otherwise.</returns>
    public static bool TryCreate(int? page, int? pageSize, [NotNullWhen(true)] out PageRequest? request, [NotNullWhen(false)] out string? error)
    {
        int resolvedPage = page ?? 1;
        int resolvedPageSize = pageSize ?? DefaultPageSize;

        if (resolvedPage < 1)
        {
            request = null;
            error = "page must be 1 or greater.";
            return false;
        }

        if (resolvedPageSize < 1 || resolvedPageSize > MaxPageSize)
        {
            request = null;
            error = $"pageSize must be between 1 and {MaxPageSize}.";
            return false;
        }

        request = new PageRequest(resolvedPage, resolvedPageSize);
        error = null;
        return true;
    }
}
