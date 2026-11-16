namespace BeanStock.Application.Abstractions.Concurrency;

/// <summary>
/// A held distributed lock. Disposing it releases the lock.
/// </summary>
/// <remarks>
/// A lock can be lost without its holder noticing (connection cut, process paused). Before making its work
/// permanent, the holder checks <see cref="IsStillHeldAsync"/> as late as possible: this is the "fencing"
/// check that keeps a former holder from writing after another one took over.
/// </remarks>
public interface IDistributedLockHandle : IAsyncDisposable
{
    /// <summary>
    /// <see langword="true"/> if the lock is still held. Never throws for a lost connection: a lost connection
    /// means a lost lock.
    /// </summary>
    Task<bool> IsStillHeldAsync(CancellationToken cancellationToken);
}
