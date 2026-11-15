using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CommunitySos.Application.Common;

/// <summary>Base for list queries bound from the query string (?page=1&amp;pageSize=20).</summary>
public abstract class PagedQuery
{
    public const int MaxPageSize = 100;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class PagingExtensions
{
    /// <summary>Counts, then fetches one page. The query must already be ordered deterministically.</summary>
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, PagedQuery paging, CancellationToken ct)
    {
        var page = Math.Max(1, paging.Page);
        var size = Math.Clamp(paging.PageSize, 1, PagedQuery.MaxPageSize);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<T>(items, page, size, total);
    }

    public static PagedResult<TOut> Map<TIn, TOut>(this PagedResult<TIn> page, Func<TIn, TOut> map) =>
        new(page.Items.Select(map).ToList(), page.Page, page.PageSize, page.TotalCount);

    /// <summary>Shared rules for every paged query validator.</summary>
    public static void AddPagingRules<T>(this AbstractValidator<T> validator)
        where T : PagedQuery
    {
        validator.RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        validator.RuleFor(x => x.PageSize).InclusiveBetween(1, PagedQuery.MaxPageSize);
    }
}
