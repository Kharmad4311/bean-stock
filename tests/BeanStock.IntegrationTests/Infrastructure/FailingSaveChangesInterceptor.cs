using BeanStock.Domain.Catalog;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BeanStock.IntegrationTests.Infrastructure;

/// <summary>
/// Simulates a database failure in the middle of a synchronization: after a given number of successful
/// product batches, the next save of products throws.
/// </summary>
public sealed class FailingSaveChangesInterceptor : SaveChangesInterceptor
{
    private const int Disabled = -1;

    private int _remainingSuccessfulSaves = Disabled;

    public void FailAfterProductSaves(int successfulSaves) => Volatile.Write(ref _remainingSuccessfulSaves, successfulSaves);

    public void Reset() => Volatile.Write(ref _remainingSuccessfulSaves, Disabled);

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        var savesProducts = eventData.Context?.ChangeTracker.Entries<Product>().Any() ?? false;
        if (savesProducts && Volatile.Read(ref _remainingSuccessfulSaves) != Disabled)
        {
            // Reaching -1 throws once, then equals Disabled: later saves go through.
            if (Interlocked.Decrement(ref _remainingSuccessfulSaves) < 0)
            {
                throw new InvalidOperationException("Simulated database failure during the synchronization.");
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
