using BeanStock.Application.Carts.Common;
using ErrorOr;
using Mediator;

namespace BeanStock.Application.Carts.RemoveCartLine;

/// <summary>
/// Removes a line from the cart of the user.
/// </summary>
/// <param name="UserId">Current user, owner of the cart.</param>
/// <param name="LineId">Line to remove, in the cart of the user.</param>
/// <param name="ExpectedLineVersion">
/// Version of the line when the client read it. Required: if the line changed since, the request is rejected
/// with a conflict.
/// </param>
public sealed record RemoveCartLineCommand(string UserId, Guid LineId, int? ExpectedLineVersion)
    : ICommand<ErrorOr<CartResponse>>;
