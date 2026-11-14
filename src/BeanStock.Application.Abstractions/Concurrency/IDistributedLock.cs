namespace BeanStock.Application.Abstractions.Concurrency;

/// <summary>
/// Mutual exclusion shared by every instance of the service (API and worker alike).
/// </summary>
public interface IDistributedLock
{
    /// <summary>
    /// Tries to acquire the lock on <paramref name="resource"/> without waiting.
    /// </summary>
    /// <returns>
    /// A handle that releases the lock when disposed, or <see langword="null"/> if another holder has it.
    /// </returns>
    Task<IDistributedLockHandle?> TryAcquireAsync(string resource, CancellationToken cancellationToken);
}
