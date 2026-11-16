using BeanStock.Application.Abstractions.Carts;
using BeanStock.Application.Carts.Common;
using ErrorOr;
using Mediator;

namespace BeanStock.Application.Carts.ChangeCartLineQuantity;

public sealed class ChangeCartLineQuantityCommandHandler(ICartRepository carts, TimeProvider timeProvider)
    : ICommandHandler<ChangeCartLineQuantityCommand, ErrorOr<CartResponse>>
{
    public async ValueTask<ErrorOr<CartResponse>> Handle(
        ChangeCartLineQuantityCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The validator guarantees a version; the guard documents it for any other caller.
        var expectedLineVersion = command.ExpectedLineVersion
            ?? throw new ArgumentException("The expected line version is required.", nameof(command));

        var result = await carts.ChangeAsync(
            command.UserId,
            (cart, _) => ValueTask.FromResult(cart
                .ChangeQuantity(command.LineId, expectedLineVersion, command.Quantity, timeProvider.GetUtcNow())
                .ToSuccess()),
            cancellationToken);

        return result.ToResponse();
    }
}
