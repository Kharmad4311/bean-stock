using BeanStock.Application.Carts.AddCartLine;
using BeanStock.Application.Carts.ChangeCartLineQuantity;
using BeanStock.Application.Carts.CreateCart;
using BeanStock.Application.Carts.RemoveCartLine;

namespace BeanStock.Application.UnitTests.Carts;

public sealed class CartCommandValidatorTests
{
    private static readonly Guid _lineId = Guid.CreateVersion7();

    [Theory]
    [InlineData("")]
    [InlineData("a-user-identifier-that-is-much-longer-than-the-sixty-four-characters-allowed")]
    public void User_identifier_must_be_between_1_and_64_characters(string userId)
    {
        var result = new CreateCartCommandValidator().Validate(new CreateCartCommand(userId));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Adding_requires_a_product()
    {
        var result = new AddCartLineCommandValidator().Validate(new AddCartLineCommand("alice", Guid.Empty, 1));

        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe(nameof(AddCartLineCommand.ProductId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    public void Changing_a_quantity_requires_a_valid_line_version(int? version)
    {
        var result = new ChangeCartLineQuantityCommandValidator()
            .Validate(new ChangeCartLineQuantityCommand("alice", _lineId, 3, version));

        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe(nameof(ChangeCartLineQuantityCommand.ExpectedLineVersion));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    public void Removing_a_line_requires_a_valid_line_version(int? version)
    {
        var result = new RemoveCartLineCommandValidator().Validate(new RemoveCartLineCommand("alice", _lineId, version));

        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe(nameof(RemoveCartLineCommand.ExpectedLineVersion));
    }

    [Fact]
    public void Valid_line_commands_pass()
    {
        new ChangeCartLineQuantityCommandValidator()
            .Validate(new ChangeCartLineQuantityCommand("alice", _lineId, 3, 0)).IsValid.ShouldBeTrue();
        new RemoveCartLineCommandValidator()
            .Validate(new RemoveCartLineCommand("alice", _lineId, 0)).IsValid.ShouldBeTrue();
    }
}
