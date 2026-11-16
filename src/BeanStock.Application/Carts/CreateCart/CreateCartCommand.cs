using BeanStock.Application.Carts.Common;
using ErrorOr;
using Mediator;

namespace BeanStock.Application.Carts.CreateCart;

/// <summary>
/// Creates the cart of the user, or returns it if it already exists (idempotent, even under parallel calls).
/// </summary>
/// <param name="UserId">Current user, owner of the cart.</param>
public sealed record CreateCartCommand(string UserId) : ICommand<ErrorOr<CartCreationResult>>;

/// <summary>
/// Outcome of <see cref="CreateCartCommand"/>.
/// </summary>
/// <param name="Cart">The cart of the user, created or existing.</param>
/// <param name="Created"><see langword="true"/> if this call created the cart, <see langword="false"/> if it already existed.</param>
public sealed record CartCreationResult(CartResponse Cart, bool Created);
