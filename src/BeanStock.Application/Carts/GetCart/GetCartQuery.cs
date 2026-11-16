using BeanStock.Application.Carts.Common;
using ErrorOr;
using Mediator;

namespace BeanStock.Application.Carts.GetCart;

/// <summary>
/// Returns the cart of the user, with its lines, total and versions.
/// </summary>
/// <param name="UserId">Current user, owner of the cart.</param>
public sealed record GetCartQuery(string UserId) : IQuery<ErrorOr<CartResponse>>;
