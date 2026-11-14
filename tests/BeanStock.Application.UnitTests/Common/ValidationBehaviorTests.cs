using BeanStock.Application.Catalog.ListProducts;
using BeanStock.Application.Common.Behaviors;
using BeanStock.Application.Common.Paging;
using ErrorOr;

namespace BeanStock.Application.UnitTests.Common;

public sealed class ValidationBehaviorTests
{
    private readonly ValidationBehavior<ListProductsQuery, ErrorOr<PagedResult<ProductResponse>>> _behavior =
        new([new ListProductsQueryValidator()]);

    [Fact]
    public async Task Invalid_message_returns_validation_errors_without_calling_the_handler()
    {
        var handlerCalled = false;

        var result = await _behavior.Handle(
            new ListProductsQuery(Page: 0, PageSize: 20),
            (_, _) =>
            {
                handlerCalled = true;
                return ValueTask.FromResult<ErrorOr<PagedResult<ProductResponse>>>(EmptyPage());
            },
            TestContext.Current.CancellationToken);

        handlerCalled.ShouldBeFalse();
        result.IsError.ShouldBeTrue();
        result.FirstError.Type.ShouldBe(ErrorType.Validation);
        result.FirstError.Code.ShouldBe(nameof(ListProductsQuery.Page));
    }

    [Fact]
    public async Task Valid_message_is_passed_to_the_handler()
    {
        var result = await _behavior.Handle(
            new ListProductsQuery(Page: 1, PageSize: 20),
            (_, _) => ValueTask.FromResult<ErrorOr<PagedResult<ProductResponse>>>(EmptyPage()),
            TestContext.Current.CancellationToken);

        result.IsError.ShouldBeFalse();
        result.Value.Items.ShouldBeEmpty();
    }

    private static PagedResult<ProductResponse> EmptyPage() => new([], Page: 1, PageSize: 20, TotalCount: 0);
}
