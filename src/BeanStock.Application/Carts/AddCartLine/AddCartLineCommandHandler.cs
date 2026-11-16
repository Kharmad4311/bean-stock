using BeanStock.Application.Abstractions.Carts;
using BeanStock.Application.Abstractions.Persistence;
using BeanStock.Application.Carts.Common;
using BeanStock.Domain.Carts;
using BeanStock.Domain.Catalog;
using ErrorOr;
using Mediator;

namespace BeanStock.Application.Carts.AddCartLine;

/// <remarks>
/// Parallel additions to the same cart are isolated by <see cref="ICartRepository.ChangeAsync"/>: each one reads the
/// quantity left by the previous one, so every accepted quantity is kept and the limit is checked on the real value.
/// </remarks>
public sealed class AddCartLineCommandHandler(
    ICartRepository carts,
    IReadRepository<Product> products,
    TimeProvider timeProvider)
    : ICommandHandler<AddCartLineCommand, ErrorOr<CartResponse>>
{
    public async ValueTask<ErrorOr<CartResponse>> Handle(AddCartLineCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await carts.ChangeAsync(
            command.UserId,
            async (cart, token) =>
            {
                // Read during the change: the product state is the one at the time of the addition.
                var product = await products.GetByIdAsync(command.ProductId, token);
                if (product is null)
                {
                    return CartErrors.ProductNotFound(command.ProductId);
                }

                return cart.AddItem(product, command.Quantity, timeProvider.GetUtcNow()).ToSuccess();
            },
            cancellationToken);

        return result.ToResponse();
    }
}
