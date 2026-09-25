using DeliveryOps.BuildingBlocks.Application;
using Microsoft.EntityFrameworkCore;

namespace DeliveryOps.Core.Handlers.Common;

internal static class PaginationExtensions
{
    public static async Task<PagedResponse<T>> ToPagedAsync<T>(
        this IQueryable<T> query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        (page, pageSize) = Pagination.Normalize(page, pageSize);
        int totalCount = await query.CountAsync(cancellationToken);
        List<T> items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResponse<T>(items, page, pageSize, totalCount);
    }
}
