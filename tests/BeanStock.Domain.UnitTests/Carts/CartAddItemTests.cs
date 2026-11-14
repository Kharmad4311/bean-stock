using BeanStock.Domain.Carts;
using BeanStock.Domain.UnitTests.TestData;

namespace BeanStock.Domain.UnitTests.Carts;

public sealed class CartAddItemTests
{
    private readonly Cart _cart = Cart.Create("alice", TestProducts.Now).Value;

    [Fact]
    public void AddItem_new_product_creates_line_with_captured_name_and_price()
    {
        var product = TestProducts.Active("Robusta Beans", 8.90m);

        var result = _cart.AddItem(product, 3, TestProducts.Now);

        result.IsError.ShouldBeFalse();
        var line = _cart.Lines.ShouldHaveSingleItem();
        line.ProductId.ShouldBe(product.Id);
        line.ProductName.ShouldBe("Robusta Beans");
        line.UnitPrice.ShouldBe(8.90m);
        line.Quantity.ShouldBe(3);
        line.Version.ShouldBe(0);
        _cart.Version.ShouldBe(1);
    }

    [Fact]
    public void AddItem_same_product_increases_existing_quantity()
    {
        var product = TestProducts.Active();
        _cart.AddItem(product, 2, TestProducts.Now);

        _cart.AddItem(product, 5, TestProducts.Now);

        var line = _cart.Lines.ShouldHaveSingleItem();
        line.Quantity.ShouldBe(7);
        line.Version.ShouldBe(1);
        _cart.Version.ShouldBe(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000)]
    public void AddItem_with_quantity_out_of_range_is_rejected_without_change(int quantity)
    {
        var result = _cart.AddItem(TestProducts.Active(), quantity, TestProducts.Now);

        result.FirstError.Code.ShouldBe("Cart.QuantityOutOfRange");
        _cart.Lines.ShouldBeEmpty();
        _cart.Version.ShouldBe(0);
    }

    [Fact]
    public void AddItem_exceeding_max_quantity_on_existing_line_is_rejected_without_change()
    {
        var product = TestProducts.Active();
        _cart.AddItem(product, 990, TestProducts.Now);

        var result = _cart.AddItem(product, 10, TestProducts.Now);

        result.FirstError.Code.ShouldBe("Cart.QuantityLimitExceeded");
        var line = _cart.Lines.ShouldHaveSingleItem();
        line.Quantity.ShouldBe(990);
        line.Version.ShouldBe(0);
        _cart.Version.ShouldBe(1);
    }

    [Fact]
    public void AddItem_up_to_max_quantity_is_accepted()
    {
        var product = TestProducts.Active();
        _cart.AddItem(product, 990, TestProducts.Now);

        var result = _cart.AddItem(product, 9, TestProducts.Now);

        result.IsError.ShouldBeFalse();
        _cart.Lines.ShouldHaveSingleItem().Quantity.ShouldBe(CartRules.MaxQuantity);
    }

    [Fact]
    public void AddItem_inactive_product_is_rejected_without_change()
    {
        var result = _cart.AddItem(TestProducts.Inactive(), 1, TestProducts.Now);

        result.FirstError.Code.ShouldBe("Cart.ProductUnavailable");
        _cart.Lines.ShouldBeEmpty();
        _cart.Version.ShouldBe(0);
    }

    [Fact]
    public void Total_is_computed_from_captured_prices()
    {
        _cart.AddItem(TestProducts.Active(price: 10.00m), 2, TestProducts.Now);
        _cart.AddItem(TestProducts.Active(price: 2.50m), 3, TestProducts.Now);

        _cart.Total.ShouldBe(27.50m);
    }
}
