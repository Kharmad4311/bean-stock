using Ardalis.Specification;
using BeanStock.Domain.Catalog;

namespace BeanStock.Application.Catalog.ListProducts;

/// <summary>
/// One page of active products, in a stable order, projected to <see cref="ProductResponse"/>.
/// </summary>
/// <remarks>Counting with this specification ignores paging and projection (criteria only).</remarks>
internal sealed class ActiveProductsPageSpec : Specification<Product, ProductResponse>
{
    public ActiveProductsPageSpec(int page, int pageSize)
    {
        Query
            .Where(product => product.IsActive)
            .OrderBy(product => product.ExternalId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking();

        Query.Select(product => new ProductResponse(
            product.Id,
            product.ExternalId,
            product.Name,
            product.Category,
            product.Price,
            product.Stock,
            product.AvailabilityStatus,
            product.LastUpdatedAt));
    }
}
