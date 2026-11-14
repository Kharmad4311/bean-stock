using BeanStock.Application.Common.Paging;

namespace BeanStock.Application.UnitTests.Common;

public sealed class PagedResultTests
{
    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(1, 20, 1)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    public void TotalPages_rounds_up(int totalCount, int pageSize, int expectedTotalPages)
    {
        var page = new PagedResult<int>([], Page: 1, pageSize, totalCount);

        page.TotalPages.ShouldBe(expectedTotalPages);
    }
}
