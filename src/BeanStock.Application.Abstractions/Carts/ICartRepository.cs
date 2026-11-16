using BeanStock.Domain.Carts;
using ErrorOr;

namespace BeanStock.Application.Abstractions.Carts;

/// <summary>
/// Write access to the carts. Reads that change nothing go through <c>IReadRepository&lt;Cart&gt;</c>.
/// </summary>
/// <remarks>
/// Each method is a complete, atomic operation: callers cannot assemble the write protocol wrongly
/// (forget the transaction, the lock or the commit), because they never see its steps.
/// </remarks>
public interface ICartRepository
{
    /// <summary>
    /// Adds a new cart. Returns <see langword="false"/>, without error, if the user already has a cart
    /// (created by a parallel request): the caller then reads the existing one.
    /// </summary>
    /// <param name="cart">New cart, at version 0.</param>
    /// <param name="cancellationToken">Cancels the operation; nothing is written.</param>
    Task<bool> TryAddAsync(Cart cart, CancellationToken cancellationToken);

    /// <summary>
    /// Changes the cart of a user atomically, isolated from every other writer of the same cart on any instance.
    /// </summary>
    /// <param name="userId">Owner of the cart.</param>
    /// <param name="change">
    /// Change to apply to the cart, loaded with its lines in its last committed state. Returning an error rejects the
    /// change: nothing is written, neither the lines nor the versions move.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation; the transaction is rolled back and nothing is written.</param>
    /// <returns>
    /// The changed cart; <c>Cart.NotFound</c> if the user has no cart; the errors of a rejected change;
    /// <c>Cart.ConcurrentUpdate</c> if a concurrent write was detected while saving (nothing is written).
    /// </returns>
    Task<ErrorOr<Cart>> ChangeAsync(
        string userId,
        Func<Cart, CancellationToken, ValueTask<ErrorOr<Success>>> change,
        CancellationToken cancellationToken);
}
