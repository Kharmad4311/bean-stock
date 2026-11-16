using BeanStock.Domain.Carts;
using BeanStock.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BeanStock.IntegrationTests.Infrastructure;

/// <summary>
/// Simulates database failures:
/// <list type="bullet">
/// <item>in the middle of a synchronization: after a given number of successful product batches, the next save
/// of products throws;</item>
/// <item>while saving a change of a cart: the next save that writes a cart or a cart line throws.</item>
/// </list>
/// </summary>
public sealed class FailingSaveChangesInterceptor : SaveChangesInterceptor
{
    private const int Disabled = -1;

    private int _remainingSuccessfulSaves = Disabled;
    private int _failNextCartSave;

    public void FailAfterProductSaves(int successfulSaves) => Volatile.Write(ref _remainingSuccessfulSaves, successfulSaves);

    public void FailNextCartSave() => Volatile.Write(ref _failNextCartSave, 1);

    public void Reset()
    {
        Volatile.Write(ref _remainingSuccessfulSaves, Disabled);
        Volatile.Write(ref _failNextCartSave, 0);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        var changeTracker = eventData.Context?.ChangeTracker;

        var savesProducts = changeTracker?.Entries<Product>().Any() ?? false;
        if (savesProducts && Volatile.Read(ref _remainingSuccessfulSaves) != Disabled)
        {
            // Reaching -1 throws once, then equals Disabled: later saves go through.
            if (Interlocked.Decrement(ref _remainingSuccessfulSaves) < 0)
            {
                throw new InvalidOperationException("Simulated database failure during the synchronization.");
            }
        }

        var savesCart = changeTracker is not null
            && (changeTracker.Entries<Cart>().Any(entry => entry.State != EntityState.Unchanged)
                || changeTracker.Entries<CartLine>().Any(entry => entry.State != EntityState.Unchanged));
        if (savesCart && Interlocked.Exchange(ref _failNextCartSave, 0) == 1)
        {
            throw new InvalidOperationException("Simulated database failure while saving a cart.");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
