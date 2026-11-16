using BeanStock.Domain.Catalog;

namespace BeanStock.Domain.UnitTests.TestData;

internal static class TestProducts
{
    private static int _nextExternalId;

    public static readonly DateTimeOffset Now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    public static Product Active(string name = "Arabica Beans", decimal price = 12.50m) =>
        Product.Create(
            externalId: Interlocked.Increment(ref _nextExternalId),
            name: name,
            category: "groceries",
            price: price,
            stock: 10,
            availabilityStatus: "In Stock",
            providerUpdatedAt: Now,
            now: Now);

    public static Product Inactive()
    {
        var product = Active();
        product.Deactivate(Now);
        return product;
    }
}
