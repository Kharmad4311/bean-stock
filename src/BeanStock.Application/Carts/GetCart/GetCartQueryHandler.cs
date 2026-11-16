using BeanStock.Application.Abstractions.Persistence;
using BeanStock.Application.Carts.Common;
using BeanStock.Domain.Carts;
using ErrorOr;
using Mediator;

namespace BeanStock.Application.Carts.GetCart;

/// <remarks>
/// A plain read: no transaction, no lock. Under READ_COMMITTED_SNAPSHOT it sees the last committed state of the
/// cart and its lines in one consistent snapshot, without waiting for a writer.
/// </remarks>
public sealed class GetCartQueryHandler(IReadRepository<Cart> carts)
    : IQueryHandler<GetCartQuery, ErrorOr<CartResponse>>
{
    public async ValueTask<ErrorOr<CartResponse>> Handle(GetCartQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var cart = await carts.FirstOrDefaultAsync(new CartByUserSpec(query.UserId), cancellationToken);
        if (cart is null)
        {
            return CartErrors.CartNotFound;
        }

        return CartResponse.From(cart);
    }
}
