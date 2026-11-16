using Ardalis.Specification;
using BeanStock.Domain.Carts;

namespace BeanStock.Application.Carts.Common;

/// <summary>
/// Cart of a user with its lines, read only (not tracked, no lock).
/// </summary>
internal sealed class CartByUserSpec : Specification<Cart>
{
    public CartByUserSpec(string userId)
    {
        Query.Where(cart => cart.UserId == userId).AsNoTracking();
        Query.Include(cart => cart.Lines);
    }
}
