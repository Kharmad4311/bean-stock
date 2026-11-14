using BeanStock.Application.Abstractions.Catalog;
using BeanStock.Domain.Catalog.Synchronization;
using Microsoft.EntityFrameworkCore;

namespace BeanStock.Infrastructure.Persistence.Repositories;

/// <summary>
/// History of synchronization runs, written with a dedicated short-lived DbContext.
/// </summary>
/// <remarks>
/// Deliberately not the scoped DbContext: that one carries the synchronization transaction and the distributed
/// lock. A separate connection records the run even when the synchronization is rolled back or its
/// connection is lost.
/// </remarks>
internal sealed class EfCatalogSyncRunStore(DbContextOptions<BeanStockDbContext> options) : ICatalogSyncRunStore
{
    public async Task AddAsync(CatalogSyncRun run, CancellationToken cancellationToken)
    {
        await using var dbContext = new BeanStockDbContext(options);
        dbContext.CatalogSyncRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(CatalogSyncRun run, CancellationToken cancellationToken)
    {
        await using var dbContext = new BeanStockDbContext(options);
        dbContext.CatalogSyncRuns.Update(run);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<DateTimeOffset?> GetLastSuccessfulCompletionAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = new BeanStockDbContext(options);
        return await dbContext.CatalogSyncRuns
            .Where(run => run.Status == CatalogSyncStatus.Succeeded)
            .MaxAsync(run => run.CompletedAt, cancellationToken);
    }
}
