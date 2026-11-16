using BeanStock.Application.Abstractions.Carts;
using BeanStock.Domain.Carts;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace BeanStock.Infrastructure.Persistence.Repositories;

/// <summary>
/// Cart writes, through the scoped <see cref="BeanStockDbContext"/>.
/// </summary>
/// <remarks>
/// Two mechanisms, see <c>documentation/panier.md</c>:
/// <list type="bullet">
/// <item>a pessimistic lock on the cart row (<c>UPDLOCK</c>) serializes the writers of one cart, on every instance;</item>
/// <item>the versions of the cart and of its lines are EF Core concurrency tokens: an update or delete only applies
/// to the version read, as a safety net if a writer ever bypassed the lock.</item>
/// </list>
/// </remarks>
internal sealed class EfCartRepository(BeanStockDbContext dbContext) : ICartRepository
{
    public async Task<bool> TryAddAsync(Cart cart, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cart);

        dbContext.Carts.Add(cart);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (SqlServerErrors.IsUniqueViolation(exception))
        {
            // The unique index on UserId rejected a second cart for this user: another request won the race.
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<ErrorOr<Cart>> ChangeAsync(
        string userId,
        Func<Cart, CancellationToken, ValueTask<ErrorOr<Success>>> change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        // Disposing the transaction without commit rolls it back and releases the lock.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var cart = await LockAndLoadAsync(userId, cancellationToken);
        if (cart is null)
        {
            return CartErrors.CartNotFound;
        }

        var result = await change(cart, cancellationToken);
        if (result.IsError)
        {
            // Rejected: nothing is saved. Forgetting the in-memory changes keeps them out of any later save of the scope.
            dbContext.ChangeTracker.Clear();
            return result.Errors;
        }

        if (!await TrySaveAsync(cancellationToken))
        {
            return CartErrors.ConcurrentUpdate;
        }

        await transaction.CommitAsync(cancellationToken);
        return cart;
    }

    /// <summary>Locks the cart row until the end of the transaction, then loads the cart with its lines.</summary>
    private async Task<Cart?> LockAndLoadAsync(string userId, CancellationToken cancellationToken)
    {
        // UPDLOCK: update locks are incompatible with each other, so a second writer of the same cart waits here
        // until the first transaction ends. Plain reads (snapshot under READ_COMMITTED_SNAPSHOT) are not blocked.
        // ROWLOCK: only this cart is locked, never its neighbors.
        var cartIds = await dbContext.Database
            .SqlQuery<Guid>($"SELECT [Id] AS [Value] FROM [Carts] WITH (UPDLOCK, ROWLOCK) WHERE [UserId] = {userId}")
            .ToListAsync(cancellationToken);

        if (cartIds.Count == 0)
        {
            return null;
        }

        // Loaded after the lock is granted: under READ_COMMITTED_SNAPSHOT, this statement sees every change
        // committed by the previous writer.
        return await dbContext.Carts
            .Include(cart => cart.Lines)
            .SingleAsync(cart => cart.Id == cartIds[0], cancellationToken);
    }

    /// <returns><see langword="false"/> if a row changed since it was read; the caller does not commit.</returns>
    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // An updated or deleted row no longer has the version read (concurrency token).
            dbContext.ChangeTracker.Clear();
            return false;
        }
        catch (DbUpdateException exception) when (SqlServerErrors.IsUniqueViolation(exception))
        {
            // The same product was added to this cart by another request.
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }
}
