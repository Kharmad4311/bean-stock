using BeanStock.Application.Abstractions.Carts;
using BeanStock.Application.Carts.Common;
using ErrorOr;
using Mediator;

namespace BeanStock.Application.Carts.RemoveCartLine;

/// <remarks>
/// The line is deleted, not flagged: a later change sent with an old version finds no line and is rejected,
/// so a removed line never comes back.
/// </remarks>
public sealed class RemoveCartLineCommandHandler(ICartRepository carts, TimeProvider timeProvider)
    : ICommandHandler<RemoveCartLineCommand, ErrorOr<CartResponse>>
{
    public async ValueTask<ErrorOr<CartResponse>> Handle(RemoveCartLineCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var expectedLineVersion = command.ExpectedLineVersion
            ?? throw new ArgumentException("The expected line version is required.", nameof(command));

        var result = await carts.ChangeAsync(
            command.UserId,
            (cart, _) => ValueTask.FromResult(cart
                .RemoveLine(command.LineId, expectedLineVersion, timeProvider.GetUtcNow())
                .ToSuccess()),
            cancellationToken);

        return result.ToResponse();
    }
}
