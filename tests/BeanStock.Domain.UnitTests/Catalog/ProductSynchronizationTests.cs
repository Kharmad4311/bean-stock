using BeanStock.Domain.Catalog;
using BeanStock.Domain.UnitTests.TestData;

namespace BeanStock.Domain.UnitTests.Catalog;

public sealed class ProductSynchronizationTests
{
    private static readonly DateTimeOffset _later = TestProducts.Now.AddDays(1);

    [Fact]
    public void Create_starts_active_with_the_creation_date_as_last_update()
    {
        var product = TestProducts.Active();

        product.IsActive.ShouldBeTrue();
        product.LastUpdatedAt.ShouldBe(TestProducts.Now);
    }

    [Fact]
    public void Create_rounds_the_price_to_the_stored_precision()
    {
        var product = TestProducts.Active(price: 9.995m);

        product.Price.ShouldBe(10.00m);
    }

    [Fact]
    public void ApplyProviderData_with_identical_values_leaves_the_product_untouched()
    {
        var product = TestProducts.Active("Arabica Beans", 12.50m);

        var change = ApplySameValues(product, _later);

        change.ShouldBe(ProductSyncChange.Unchanged);
        product.LastUpdatedAt.ShouldBe(TestProducts.Now);
    }

    [Fact]
    public void ApplyProviderData_with_a_price_differing_only_beyond_the_stored_precision_is_unchanged()
    {
        var product = TestProducts.Active(price: 12.50m);

        var change = product.ApplyProviderData(
            product.Name, product.Category, 12.501m, product.Stock, product.AvailabilityStatus,
            product.ProviderUpdatedAt, _later);

        change.ShouldBe(ProductSyncChange.Unchanged);
    }

    [Fact]
    public void ApplyProviderData_with_a_new_price_updates_values_and_last_update()
    {
        var product = TestProducts.Active(price: 12.50m);

        var change = product.ApplyProviderData(
            "Arabica Beans 1kg", product.Category, 14.90m, 3, "Low Stock", _later, _later);

        change.ShouldBe(ProductSyncChange.Updated);
        product.Name.ShouldBe("Arabica Beans 1kg");
        product.Price.ShouldBe(14.90m);
        product.Stock.ShouldBe(3);
        product.AvailabilityStatus.ShouldBe("Low Stock");
        product.ProviderUpdatedAt.ShouldBe(_later);
        product.LastUpdatedAt.ShouldBe(_later);
    }

    [Fact]
    public void ApplyProviderData_on_an_inactive_product_reactivates_it()
    {
        var product = TestProducts.Inactive();

        var change = ApplySameValues(product, _later);

        change.ShouldBe(ProductSyncChange.Reactivated);
        product.IsActive.ShouldBeTrue();
        product.LastUpdatedAt.ShouldBe(_later);
    }

    [Theory]
    [InlineData("", "groceries")]
    [InlineData("Name", " ")]
    public void ApplyProviderData_with_a_blank_name_or_category_throws_and_keeps_the_product(
        string name,
        string category)
    {
        var product = TestProducts.Active("Arabica Beans");

        Should.Throw<ArgumentException>(() => product.ApplyProviderData(
            name, category, 1m, 1, "In Stock", null, _later));

        product.Name.ShouldBe("Arabica Beans");
    }

    [Fact]
    public void ApplyProviderData_with_a_negative_price_throws_and_keeps_the_product()
    {
        var product = TestProducts.Active(price: 12.50m);

        Should.Throw<ArgumentOutOfRangeException>(() => product.ApplyProviderData(
            product.Name, product.Category, -1m, 1, "In Stock", null, _later));

        product.Price.ShouldBe(12.50m);
    }

    [Fact]
    public void Deactivate_an_active_product_marks_it_inactive()
    {
        var product = TestProducts.Active();

        var deactivated = product.Deactivate(_later);

        deactivated.ShouldBeTrue();
        product.IsActive.ShouldBeFalse();
        product.LastUpdatedAt.ShouldBe(_later);
    }

    [Fact]
    public void Deactivate_an_inactive_product_changes_nothing()
    {
        var product = TestProducts.Inactive();

        var deactivated = product.Deactivate(_later);

        deactivated.ShouldBeFalse();
        product.LastUpdatedAt.ShouldBe(TestProducts.Now);
    }

    private static ProductSyncChange ApplySameValues(Product product, DateTimeOffset now) =>
        product.ApplyProviderData(
            product.Name,
            product.Category,
            product.Price,
            product.Stock,
            product.AvailabilityStatus,
            product.ProviderUpdatedAt,
            now);
}
