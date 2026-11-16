using BeanStock.Domain.Carts;
using BeanStock.Domain.Catalog;
using BeanStock.Domain.Catalog.Synchronization;
using Microsoft.EntityFrameworkCore;

namespace BeanStock.Infrastructure.Persistence;

/// <summary>
/// Single database of the service: catalog, synchronization history and carts.
/// </summary>
public sealed class BeanStockDbContext(DbContextOptions<BeanStockDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<CatalogSyncRun> CatalogSyncRuns => Set<CatalogSyncRun>();

    public DbSet<Cart> Carts => Set<Cart>();

    public DbSet<CartLine> CartLines => Set<CartLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BeanStockDbContext).Assembly);
    }
}
