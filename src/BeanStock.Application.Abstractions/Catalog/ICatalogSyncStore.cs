using BeanStock.Domain.Catalog;

namespace BeanStock.Application.Abstractions.Catalog;

/// <summary>
/// Write access to the local catalog, reserved to the synchronization.
/// </summary>
/// <remarks>
/// Designed for batches: the caller loads and saves a bounded number of products at a time, and
/// <see cref="SaveBatchAsync"/> forgets them afterwards, so memory does not grow with the catalog size.
/// </remarks>
public interface ICatalogSyncStore
{
    /// <summary>Provider identifiers of every active product.</summary>
    Task<IReadOnlySet<int>> GetActiveExternalIdsAsync(CancellationToken cancellationToken);

    /// <summary>Products (active or not) with the given provider identifiers. Changes are tracked.</summary>
    Task<IReadOnlyList<Product>> GetByExternalIdsAsync(
        IReadOnlyCollection<int> externalIds,
        CancellationToken cancellationToken);

    void Add(Product product);

    /// <summary>Writes the pending changes, then stops tracking the loaded products.</summary>
    Task SaveBatchAsync(CancellationToken cancellationToken);
}
