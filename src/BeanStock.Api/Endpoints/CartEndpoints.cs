using System.Security.Claims;
using BeanStock.Api.Authentication;
using BeanStock.Api.Http;
using BeanStock.Application.Carts.AddCartLine;
using BeanStock.Application.Carts.ChangeCartLineQuantity;
using BeanStock.Application.Carts.Common;
using BeanStock.Application.Carts.CreateCart;
using BeanStock.Application.Carts.GetCart;
using BeanStock.Application.Carts.RemoveCartLine;
using ErrorOr;
using Mediator;

namespace BeanStock.Api.Endpoints;

/// <summary>
/// Personal cart of the current user (<c>X-User-Id</c> header). Routes never carry a cart identifier:
/// a user can only reach their own cart.
/// </summary>
/// <remarks>Every successful operation returns the whole cart, with its new versions.</remarks>
internal static class CartEndpoints
{
    private const string CartUrl = "/me/cart";

    public static IEndpointRouteBuilder MapCartEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(CartUrl).WithTags("Cart").RequireAuthorization();

        group.MapPost("/", CreateCartAsync)
            .WithName("CreateCart")
            .WithSummary("Creates the cart of the current user, or returns it if it already exists.")
            .WithDescription("Idempotent, even under parallel calls: 201 when this call created the cart, 200 otherwise.")
            .Produces<CartResponse>(StatusCodes.Status201Created)
            .Produces<CartResponse>();

        group.MapGet("/", GetCartAsync)
            .WithName("GetCart")
            .WithSummary("Returns the cart of the current user: lines, quantities, prices, total and versions.")
            .Produces<CartResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/lines", AddCartLineAsync)
            .WithName("AddCartLine")
            .WithSummary("Adds an active product of the local catalog, or increases its quantity if already in the cart.")
            .Produces<CartResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/lines/{lineId:guid}", ChangeCartLineQuantityAsync)
            .WithName("ChangeCartLineQuantity")
            .WithSummary("Replaces the quantity of a line, provided it has not changed since the version sent.")
            .WithDescription("409 Cart.LineVersionConflict: the line changed since it was read; read the cart again.")
            .Produces<CartResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/lines/{lineId:guid}", RemoveCartLineAsync)
            .WithName("RemoveCartLine")
            .WithSummary("Removes a line, provided it has not changed since the version sent (query parameter).")
            .Produces<CartResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> CreateCartAsync(ClaimsPrincipal user, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateCartCommand(user.GetUserId()), cancellationToken);

        return result.Match<IResult>(
            creation => creation.Created
                ? TypedResults.Created(CartUrl, creation.Cart)
                : TypedResults.Ok(creation.Cart),
            errors => errors.ToProblem());
    }

    private static async Task<IResult> GetCartAsync(ClaimsPrincipal user, ISender sender, CancellationToken cancellationToken) =>
        ToResult(await sender.Send(new GetCartQuery(user.GetUserId()), cancellationToken));

    private static async Task<IResult> AddCartLineAsync(
        AddCartLineRequest request,
        ClaimsPrincipal user,
        ISender sender,
        CancellationToken cancellationToken) =>
        ToResult(await sender.Send(
            new AddCartLineCommand(user.GetUserId(), request.ProductId, request.Quantity),
            cancellationToken));

    private static async Task<IResult> ChangeCartLineQuantityAsync(
        Guid lineId,
        ChangeCartLineQuantityRequest request,
        ClaimsPrincipal user,
        ISender sender,
        CancellationToken cancellationToken) =>
        ToResult(await sender.Send(
            new ChangeCartLineQuantityCommand(user.GetUserId(), lineId, request.Quantity, request.Version),
            cancellationToken));

    private static async Task<IResult> RemoveCartLineAsync(
        Guid lineId,
        int? version,
        ClaimsPrincipal user,
        ISender sender,
        CancellationToken cancellationToken) =>
        ToResult(await sender.Send(new RemoveCartLineCommand(user.GetUserId(), lineId, version), cancellationToken));

    private static IResult ToResult(ErrorOr<CartResponse> result) =>
        result.Match<IResult>(cart => TypedResults.Ok(cart), errors => errors.ToProblem());
}

/// <summary>
/// Body of <c>POST /me/cart/lines</c>.
/// </summary>
/// <param name="ProductId">Local identifier of the product (<c>id</c> in <c>GET /products</c>).</param>
/// <param name="Quantity">Quantity to add, between 1 and 999; added to the quantity already in the cart, if any.</param>
public sealed record AddCartLineRequest(Guid ProductId, int Quantity);

/// <summary>
/// Body of <c>PUT /me/cart/lines/{lineId}</c>.
/// </summary>
/// <param name="Quantity">New quantity of the line (replaces the current one).</param>
/// <param name="Version">Version of the line when it was read. Required.</param>
public sealed record ChangeCartLineQuantityRequest(int Quantity, int? Version);
