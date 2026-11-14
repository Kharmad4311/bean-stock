using BeanStock.Domain.Carts;
using BeanStock.Domain.UnitTests.TestData;
using ErrorOr;

namespace BeanStock.Domain.UnitTests.Carts;

public sealed class CartLineChangeTests
{
    private readonly Cart _cart = Cart.Create("alice", TestProducts.Now).Value;

    [Fact]
    public void ChangeQuantity_with_current_line_version_replaces_quantity()
    {
        var line = _cart.AddItem(TestProducts.Active(), 2, TestProducts.Now).Value;

        var result = _cart.ChangeQuantity(line.Id, expectedLineVersion: 0, quantity: 5, TestProducts.Now);

        result.IsError.ShouldBeFalse();
        line.Quantity.ShouldBe(5);
        line.Version.ShouldBe(1);
        _cart.Version.ShouldBe(2);
    }

    [Fact]
    public void ChangeQuantity_with_stale_line_version_returns_conflict_without_change()
    {
        var line = _cart.AddItem(TestProducts.Active(), 2, TestProducts.Now).Value;
        _cart.ChangeQuantity(line.Id, expectedLineVersion: 0, quantity: 5, TestProducts.Now);

        var result = _cart.ChangeQuantity(line.Id, expectedLineVersion: 0, quantity: 8, TestProducts.Now);

        result.FirstError.Type.ShouldBe(ErrorType.Conflict);
        result.FirstError.Code.ShouldBe("Cart.LineVersionConflict");
        line.Quantity.ShouldBe(5);
        line.Version.ShouldBe(1);
        _cart.Version.ShouldBe(2);
    }

    [Fact]
    public void ChangeQuantity_on_two_different_lines_from_the_same_read_both_succeed()
    {
        // Example of the specification: two tabs read the cart once, then each changes a different line.
        var lineA = _cart.AddItem(TestProducts.Active("A"), 1, TestProducts.Now).Value;
        var lineB = _cart.AddItem(TestProducts.Active("B"), 1, TestProducts.Now).Value;
        var versionRead = _cart.Version;

        var resultA = _cart.ChangeQuantity(lineA.Id, expectedLineVersion: 0, quantity: 3, TestProducts.Now);
        var resultB = _cart.ChangeQuantity(lineB.Id, expectedLineVersion: 0, quantity: 4, TestProducts.Now);

        resultA.IsError.ShouldBeFalse();
        resultB.IsError.ShouldBeFalse();
        _cart.Version.ShouldBe(versionRead + 2);
    }

    [Fact]
    public void ChangeQuantity_to_the_current_quantity_is_accepted_without_version_change()
    {
        var line = _cart.AddItem(TestProducts.Active(), 2, TestProducts.Now).Value;

        var result = _cart.ChangeQuantity(line.Id, expectedLineVersion: 0, quantity: 2, TestProducts.Now);

        result.IsError.ShouldBeFalse();
        line.Version.ShouldBe(0);
        _cart.Version.ShouldBe(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public void ChangeQuantity_out_of_range_is_rejected_without_change(int quantity)
    {
        var line = _cart.AddItem(TestProducts.Active(), 2, TestProducts.Now).Value;

        var result = _cart.ChangeQuantity(line.Id, expectedLineVersion: 0, quantity, TestProducts.Now);

        result.FirstError.Code.ShouldBe("Cart.QuantityOutOfRange");
        line.Quantity.ShouldBe(2);
        _cart.Version.ShouldBe(1);
    }

    [Fact]
    public void ChangeQuantity_on_unknown_line_returns_not_found()
    {
        var result = _cart.ChangeQuantity(Guid.NewGuid(), expectedLineVersion: 0, quantity: 1, TestProducts.Now);

        result.FirstError.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public void RemoveLine_with_current_line_version_removes_it()
    {
        var line = _cart.AddItem(TestProducts.Active(), 2, TestProducts.Now).Value;

        var result = _cart.RemoveLine(line.Id, expectedLineVersion: 0, TestProducts.Now);

        result.IsError.ShouldBeFalse();
        _cart.Lines.ShouldBeEmpty();
        _cart.Version.ShouldBe(2);
    }

    [Fact]
    public void RemoveLine_with_stale_line_version_returns_conflict_without_change()
    {
        var line = _cart.AddItem(TestProducts.Active(), 2, TestProducts.Now).Value;
        _cart.ChangeQuantity(line.Id, expectedLineVersion: 0, quantity: 3, TestProducts.Now);

        var result = _cart.RemoveLine(line.Id, expectedLineVersion: 0, TestProducts.Now);

        result.FirstError.Code.ShouldBe("Cart.LineVersionConflict");
        _cart.Lines.ShouldHaveSingleItem();
        _cart.Version.ShouldBe(2);
    }

    [Fact]
    public void Stale_change_after_removal_does_not_resurrect_the_line()
    {
        var line = _cart.AddItem(TestProducts.Active(), 2, TestProducts.Now).Value;
        _cart.RemoveLine(line.Id, expectedLineVersion: 0, TestProducts.Now);

        var result = _cart.ChangeQuantity(line.Id, expectedLineVersion: 0, quantity: 5, TestProducts.Now);

        result.FirstError.Code.ShouldBe("Cart.LineNotFound");
        _cart.Lines.ShouldBeEmpty();
        _cart.Version.ShouldBe(2);
    }
}
