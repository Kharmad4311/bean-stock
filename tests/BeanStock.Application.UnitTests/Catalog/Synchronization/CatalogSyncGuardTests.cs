using BeanStock.Application.Abstractions.Catalog;
using BeanStock.Application.Catalog.Synchronization;

namespace BeanStock.Application.UnitTests.Catalog.Synchronization;

public sealed class CatalogSyncGuardTests
{
    [Fact]
    public void Empty_catalog_is_refused_even_when_the_local_catalog_is_empty()
    {
        var result = CatalogSyncGuard.Check(Snapshot(productCount: 0), activeProducts: 0, minimumCatalogRatio: 0.5);

        result.FirstError.Code.ShouldBe("CatalogSync.SuspiciousCatalog");
    }

    [Fact]
    public void First_synchronization_of_an_empty_local_catalog_is_accepted()
    {
        var result = CatalogSyncGuard.Check(Snapshot(productCount: 1), activeProducts: 0, minimumCatalogRatio: 0.5);

        result.IsError.ShouldBeFalse();
    }

    [Theory]
    [InlineData(5, true)]   // 5 offered >= ceil(10 * 0.5)
    [InlineData(4, false)]  // 4 offered < 5
    public void Catalog_is_compared_to_the_share_of_active_products(int offered, bool accepted)
    {
        var result = CatalogSyncGuard.Check(Snapshot(offered), activeProducts: 10, minimumCatalogRatio: 0.5);

        result.IsError.ShouldBe(!accepted);
    }

    [Fact]
    public void Ratio_zero_disables_the_share_check()
    {
        var result = CatalogSyncGuard.Check(Snapshot(productCount: 1), activeProducts: 100, minimumCatalogRatio: 0);

        result.IsError.ShouldBeFalse();
    }

    private static ProviderCatalogSnapshot Snapshot(int productCount) =>
        ProviderCatalogSnapshot.Create(
            Enumerable.Range(1, productCount)
                .Select(externalId => new ProviderProduct(externalId, "Product", "groceries", 1m, 1, "In Stock", null))
                .ToList(),
            new ProviderProductValidator());
}
