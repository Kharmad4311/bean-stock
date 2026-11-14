using BeanStock.Application.Abstractions.Persistence;
using BeanStock.Application.Common.Paging;
using BeanStock.Domain.Catalog;
using ErrorOr;
using Mediator;

namespace BeanStock.Application.Catalog.ListProducts;

public sealed class ListProductsQueryHandler(IReadRepository<Product> products)
    : IQueryHandler<ListProductsQuery, ErrorOr<PagedResult<ProductResponse>>>
{
    public async ValueTask<ErrorOr<PagedResult<ProductResponse>>> Handle(
        ListProductsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var specification = new ActiveProductsPageSpec(query.Page, query.PageSize);
        var totalCount = await products.CountAsync(specification, cancellationToken);
        var items = await products.ListAsync(specification, cancellationToken);

        return new PagedResult<ProductResponse>(items, query.Page, query.PageSize, totalCount);
    }
}
