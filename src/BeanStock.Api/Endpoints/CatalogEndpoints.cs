using BeanStock.Api.Http;
using BeanStock.Application.Catalog.ListProducts;
using BeanStock.Application.Common.Paging;
using Mediator;

namespace BeanStock.Api.Endpoints;

internal static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/products").WithTags("Catalog");

        group.MapGet("/", ListProductsAsync)
            .WithName("ListProducts")
            .WithSummary("Lists the active products of the local catalog, one page at a time.")
            .Produces<PagedResult<ProductResponse>>()
            .ProducesValidationProblem();

        return app;
    }

    private static async Task<IResult> ListProductsAsync(
        ISender sender,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = PagingRules.DefaultPageSize)
    {
        var result = await sender.Send(new ListProductsQuery(page, pageSize), cancellationToken);

        return result.Match<IResult>(
            products => TypedResults.Ok(products),
            errors => errors.ToProblem());
    }
}
