using BeanStock.Application.Catalog.ListProducts;
using BeanStock.Application.Common.Paging;

namespace BeanStock.Application.UnitTests.Catalog;

public sealed class ListProductsQueryValidatorTests
{
    private readonly ListProductsQueryValidator _validator = new();

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, PagingRules.MaxPageSize)]
    public void Valid_paging_is_accepted(int page, int pageSize)
    {
        _validator.Validate(new ListProductsQuery(page, pageSize)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, 20, nameof(ListProductsQuery.Page))]
    [InlineData(1, 0, nameof(ListProductsQuery.PageSize))]
    [InlineData(1, PagingRules.MaxPageSize + 1, nameof(ListProductsQuery.PageSize))]
    public void Invalid_paging_is_rejected(int page, int pageSize, string invalidProperty)
    {
        var result = _validator.Validate(new ListProductsQuery(page, pageSize));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe(invalidProperty);
    }
}
