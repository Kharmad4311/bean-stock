using BeanStock.Application.Common.Paging;
using ErrorOr;
using Mediator;

namespace BeanStock.Application.Catalog.ListProducts;

/// <summary>
/// Lists the active products of the local catalog, one page at a time.
/// </summary>
/// <param name="Page">Page number, starting at 1.</param>
/// <param name="PageSize">Number of products per page.</param>
public sealed record ListProductsQuery(int Page, int PageSize) : IQuery<ErrorOr<PagedResult<ProductResponse>>>;
