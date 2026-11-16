using BeanStock.Domain.Catalog;
using BeanStock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BeanStock.IntegrationTests.Infrastructure;

/// <summary>
/// Direct database access for the cart tests: catalog products are written without a synchronization.
/// </summary>
internal static class TestDatabase
{
    private static int _nextExternalId;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static async Task<Product> AddProductAsync(
        this BeanStockApiFactory factory,
        string name = "Arabica Beans",
        decimal price = 12.50m)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var product = Product.Create(
            externalId: Interlocked.Increment(ref _nextExternalId),
            name: name,
            category: "groceries",
            price: price,
            stock: 10,
            availabilityStatus: "In Stock",
            providerUpdatedAt: null,
            now: factory.Time.GetUtcNow());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BeanStockDbContext>();
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(CancellationToken);

        return product;
    }

    /// <summary>Applies new provider values to a product, as a synchronization would.</summary>
    public static async Task UpdateProductAsync(this BeanStockApiFactory factory, Guid productId, string name, decimal price)
    {
        ArgumentNullException.ThrowIfNull(factory);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BeanStockDbContext>();
        var product = await dbContext.Products.SingleAsync(candidate => candidate.Id == productId, CancellationToken);
        product.ApplyProviderData(
            name,
            product.Category,
            price,
            product.Stock,
            product.AvailabilityStatus,
            product.ProviderUpdatedAt,
            factory.Time.GetUtcNow());
        await dbContext.SaveChangesAsync(CancellationToken);
    }

    /// <summary>Deactivates a product, as a synchronization does when the provider no longer offers it.</summary>
    public static async Task DeactivateProductAsync(this BeanStockApiFactory factory, Guid productId)
    {
        ArgumentNullException.ThrowIfNull(factory);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BeanStockDbContext>();
        var product = await dbContext.Products.SingleAsync(candidate => candidate.Id == productId, CancellationToken);
        product.Deactivate(factory.Time.GetUtcNow());
        await dbContext.SaveChangesAsync(CancellationToken);
    }

    public static async Task<int> CartCountAsync(this BeanStockApiFactory factory, string userId)
    {
        ArgumentNullException.ThrowIfNull(factory);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BeanStockDbContext>();
        return await dbContext.Carts.CountAsync(cart => cart.UserId == userId, CancellationToken);
    }
}
