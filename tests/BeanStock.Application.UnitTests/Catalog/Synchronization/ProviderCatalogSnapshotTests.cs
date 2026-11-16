using BeanStock.Application.Abstractions.Catalog;
using BeanStock.Application.Catalog.Synchronization;
using BeanStock.Domain.Catalog;

namespace BeanStock.Application.UnitTests.Catalog.Synchronization;

public sealed class ProviderCatalogSnapshotTests
{
    private readonly ProviderProductValidator _validator = new();

    [Fact]
    public void Valid_products_are_kept_and_offered()
    {
        var snapshot = ProviderCatalogSnapshot.Create([Provided(1), Provided(2)], _validator);

        snapshot.ReceivedCount.ShouldBe(2);
        snapshot.ValidProducts.Count.ShouldBe(2);
        snapshot.OfferedExternalIds.Count.ShouldBe(2);
        snapshot.OfferedExternalIds.ShouldContain(1);
        snapshot.OfferedExternalIds.ShouldContain(2);
        snapshot.SkippedProducts.ShouldBeEmpty();
    }

    [Fact]
    public void Invalid_product_is_skipped_but_still_offered()
    {
        var snapshot = ProviderCatalogSnapshot.Create([Provided(1, name: ""), Provided(2)], _validator);

        snapshot.ValidProducts.ShouldHaveSingleItem().ExternalId.ShouldBe(2);
        snapshot.OfferedExternalIds.ShouldContain(1);
        var skipped = snapshot.SkippedProducts.ShouldHaveSingleItem();
        skipped.ExternalId.ShouldBe(1);
        skipped.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Every_entry_of_a_duplicated_identifier_is_skipped()
    {
        var snapshot = ProviderCatalogSnapshot.Create([Provided(1, "A"), Provided(1, "B"), Provided(2)], _validator);

        snapshot.ValidProducts.ShouldHaveSingleItem().ExternalId.ShouldBe(2);
        snapshot.SkippedProducts.Count.ShouldBe(2);
        snapshot.OfferedExternalIds.ShouldContain(1);
    }

    [Fact]
    public void Product_without_usable_identifier_is_skipped_and_not_offered()
    {
        var snapshot = ProviderCatalogSnapshot.Create([Provided(0), Provided(2)], _validator);

        snapshot.SkippedProducts.ShouldHaveSingleItem().ExternalId.ShouldBe(0);
        snapshot.OfferedExternalIds.ShouldNotContain(0);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    public void Negative_price_or_stock_is_invalid(int price, int stock)
    {
        var product = new ProviderProduct(1, "Name", "groceries", price, stock, "In Stock", null);

        _validator.Validate(product).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Too_long_name_is_invalid()
    {
        var product = Provided(1, name: new string('a', Product.NameMaxLength + 1));

        _validator.Validate(product).IsValid.ShouldBeFalse();
    }

    private static ProviderProduct Provided(int externalId, string name = "Product") =>
        new(externalId, name, "groceries", 10m, 5, "In Stock", null);
}
