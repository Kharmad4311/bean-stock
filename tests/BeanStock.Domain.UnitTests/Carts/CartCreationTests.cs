using BeanStock.Domain.Carts;
using BeanStock.Domain.UnitTests.TestData;

namespace BeanStock.Domain.UnitTests.Carts;

public sealed class CartCreationTests
{
    [Fact]
    public void Create_with_valid_user_returns_empty_cart_at_version_zero()
    {
        var result = Cart.Create("alice", TestProducts.Now);

        result.IsError.ShouldBeFalse();
        result.Value.UserId.ShouldBe("alice");
        result.Value.Version.ShouldBe(0);
        result.Value.Lines.ShouldBeEmpty();
        result.Value.Total.ShouldBe(0m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_with_blank_user_is_rejected(string userId)
    {
        var result = Cart.Create(userId, TestProducts.Now);

        result.FirstError.Code.ShouldBe("Cart.InvalidUserId");
    }

    [Fact]
    public void Create_with_too_long_user_is_rejected()
    {
        var result = Cart.Create(new string('a', CartRules.UserIdMaxLength + 1), TestProducts.Now);

        result.FirstError.Code.ShouldBe("Cart.InvalidUserId");
    }
}
