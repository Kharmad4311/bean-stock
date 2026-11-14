using BeanStock.Domain.Catalog.Synchronization;

namespace BeanStock.Application.Abstractions.Catalog;

/// <summary>
/// History of catalog synchronization runs.
/// </summary>
/// <remarks>
/// Writes are independent from the synchronization transaction: a run is recorded, and its failure too,
/// even when the catalog changes are rolled back.
/// </remarks>
public interface ICatalogSyncRunStore
{
    Task AddAsync(CatalogSyncRun run, CancellationToken cancellationToken);

    Task UpdateAsync(CatalogSyncRun run, CancellationToken cancellationToken);

    /// <summary>Completion date of the last successful run, or <see langword="null"/> if none succeeded yet.</summary>
    Task<DateTimeOffset?> GetLastSuccessfulCompletionAsync(CancellationToken cancellationToken);
}
