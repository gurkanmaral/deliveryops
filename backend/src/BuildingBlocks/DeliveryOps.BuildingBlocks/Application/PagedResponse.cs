namespace DeliveryOps.BuildingBlocks.Application;

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}

public static class Pagination
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(DefaultPage, page), Math.Clamp(pageSize, 1, MaxPageSize));

    public static PagedResponse<T> FromItems<T>(IReadOnlyList<T> items, int page, int pageSize)
    {
        (page, pageSize) = Normalize(page, pageSize);
        return new PagedResponse<T>(items.Skip((page - 1) * pageSize).Take(pageSize).ToArray(),
            page, pageSize, items.Count);
    }
}
