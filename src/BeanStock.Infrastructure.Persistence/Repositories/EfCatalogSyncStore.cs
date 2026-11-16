using BeanStock.Application.Abstractions.Catalog;
using BeanStock.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace BeanStock.Infrastructure.Persistence.Repositories;

/// <summary>
/// Catalog writes of the synchronization, through the scoped <see cref="BeanStockDbContext"/>
/// (same connection as the distributed lock and the transaction).
/// </summary>
internal sealed class EfCatalogSyncStore(BeanStockDbContext dbContext) : ICatalogSyncStore
{
    public async Task<IReadOnlySet<int>> GetActiveExternalIdsAsync(CancellationToken cancellationToken)
    {
        var externalIds = await dbContext.Products
            .AsNoTracking()
            .Where(product => product.IsActive)
            .Select(product => product.ExternalId)
            .ToListAsync(cancellationToken);

        return externalIds.ToHashSet();
    }

    public async Task<IReadOnlyList<Product>> GetByExternalIdsAsync(
        IReadOnlyCollection<int> externalIds,
        CancellationToken cancellationToken) =>
        await dbContext.Products
            .Where(product => externalIds.Contains(product.ExternalId))
            .ToListAsync(cancellationToken);

    public void Add(Product product) => dbContext.Products.Add(product);

    public async Task SaveBatchAsync(CancellationToken cancellationToken)
    {
        await dbContext.SaveChangesAsync(cancellationToken);

        // Forget the batch: the change tracker stays small whatever the catalog size.
        dbContext.ChangeTracker.Clear();
    }
}
