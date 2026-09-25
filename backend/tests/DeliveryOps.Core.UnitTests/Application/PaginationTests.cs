using DeliveryOps.BuildingBlocks.Application;

namespace DeliveryOps.Core.UnitTests.Application;

public sealed class PaginationTests
{
    [Theory]
    [InlineData(-10, 0, 1, 1)]
    [InlineData(0, 500, 1, 100)]
    [InlineData(3, 25, 3, 25)]
    public void Normalize_clamps_page_and_page_size(int page, int pageSize, int expectedPage,
        int expectedPageSize)
    {
        Assert.Equal((expectedPage, expectedPageSize), Pagination.Normalize(page, pageSize));
    }

    [Fact]
    public void FromItems_returns_requested_slice_and_metadata()
    {
        PagedResponse<int> result = Pagination.FromItems(Enumerable.Range(1, 43).ToArray(), 2, 20);

        Assert.Equal(Enumerable.Range(21, 20), result.Items);
        Assert.Equal(2, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(43, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
        Assert.True(result.HasPreviousPage);
        Assert.True(result.HasNextPage);
    }
}
