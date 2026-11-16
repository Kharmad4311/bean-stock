using BeanStock.Application.Carts.Common;
using ErrorOr;
using Mediator;

namespace BeanStock.Application.Carts.AddCartLine;

/// <summary>
/// Adds a product of the local catalog to the cart of the user. If the product is already in the cart,
/// its quantity is increased.
/// </summary>
/// <param name="UserId">Current user, owner of the cart.</param>
/// <param name="ProductId">Local identifier of the product (<c>id</c> in the product listing).</param>
/// <param name="Quantity">Quantity to add, between 1 and 999; added to the quantity already in the cart, if any.</param>
public sealed record AddCartLineCommand(string UserId, Guid ProductId, int Quantity) : ICommand<ErrorOr<CartResponse>>;
