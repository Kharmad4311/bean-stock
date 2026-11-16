using BeanStock.Application.Abstractions.Carts;
using BeanStock.Application.Abstractions.Persistence;
using BeanStock.Application.Carts.Common;
using BeanStock.Domain.Carts;
using ErrorOr;
using Mediator;

namespace BeanStock.Application.Carts.CreateCart;

/// <remarks>
/// Idempotence rests on the unique index on <c>Carts.UserId</c>, not on the first read: two parallel calls may
/// both find no cart, but only one insert succeeds. The other one reads and returns the cart of the winner.
/// </remarks>
public sealed class CreateCartCommandHandler(
    IReadRepository<Cart> readCarts,
    ICartRepository carts,
    TimeProvider timeProvider)
    : ICommandHandler<CreateCartCommand, ErrorOr<CartCreationResult>>
{
    public async ValueTask<ErrorOr<CartCreationResult>> Handle(
        CreateCartCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var specification = new CartByUserSpec(command.UserId);
        var existingCart = await readCarts.FirstOrDefaultAsync(specification, cancellationToken);
        if (existingCart is not null)
        {
            return new CartCreationResult(CartResponse.From(existingCart), Created: false);
        }

        var newCart = Cart.Create(command.UserId, timeProvider.GetUtcNow());
        if (newCart.IsError)
        {
            return newCart.Errors;
        }

        if (await carts.TryAddAsync(newCart.Value, cancellationToken))
        {
            return new CartCreationResult(CartResponse.From(newCart.Value), Created: true);
        }

        // A parallel request created the cart between the read and the insert.
        var winnerCart = await readCarts.FirstOrDefaultAsync(specification, cancellationToken)
            ?? throw new InvalidOperationException(
                "The cart insert was rejected as a duplicate, but no cart exists for this user.");

        return new CartCreationResult(CartResponse.From(winnerCart), Created: false);
    }
}
