using BeanStock.Application.Carts.Common;
using ErrorOr;
using Mediator;

namespace BeanStock.Application.Carts.ChangeCartLineQuantity;

/// <summary>
/// Replaces the quantity of a line of the cart of the user.
/// </summary>
/// <param name="UserId">Current user, owner of the cart.</param>
/// <param name="LineId">Line to change, in the cart of the user.</param>
/// <param name="Quantity">New quantity of the line, between 1 and 999. Replaces the current quantity.</param>
/// <param name="ExpectedLineVersion">
/// Version of the line when the client read it. Required: if the line changed since, the request is rejected
/// with a conflict instead of overwriting the other change.
/// </param>
public sealed record ChangeCartLineQuantityCommand(string UserId, Guid LineId, int Quantity, int? ExpectedLineVersion)
    : ICommand<ErrorOr<CartResponse>>;
