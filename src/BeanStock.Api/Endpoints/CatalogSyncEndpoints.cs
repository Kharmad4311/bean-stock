using BeanStock.Api.Http;
using BeanStock.Application.Catalog.Synchronization;
using Mediator;

namespace BeanStock.Api.Endpoints;

internal static class CatalogSyncEndpoints
{
    public static IEndpointRouteBuilder MapCatalogSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/catalog").WithTags("Administration");

        group.MapPost("/sync", SynchronizeAsync)
            .WithName("SynchronizeCatalog")
            .WithSummary("Synchronizes the local catalog with the provider now and returns the report.")
            .WithDescription(
                "Sends the same use case as the daily synchronization of the worker and waits for its end. " +
                "409: a synchronization is already running on an instance. " +
                "502: the provider is unavailable or returned a suspicious catalog. " +
                "In both cases the local catalog is unchanged.")
            .Produces<CatalogSyncReport>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        return app;
    }

    /// <remarks>
    /// The request token is passed on: if the caller disconnects, the synchronization is canceled and rolled back,
    /// which leaves the catalog as it was.
    /// </remarks>
    private static async Task<IResult> SynchronizeAsync(ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(SynchronizeCatalogCommand.Manual(), cancellationToken);

        return result.Match<IResult>(
            sync => TypedResults.Ok(sync.Report),
            errors => errors.ToProblem());
    }
}
