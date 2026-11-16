using BeanStock.Application.Abstractions.Carts;
using BeanStock.Application.Carts.ChangeCartLineQuantity;
using BeanStock.Application.Carts.RemoveCartLine;
using BeanStock.Domain.Carts;
using BeanStock.Domain.Catalog;
using ErrorOr;
using Microsoft.Extensions.Time.Testing;

namespace BeanStock.Application.UnitTests.Carts;

/// <summary>
/// The line handlers translate a command into a change of the aggregate and return the changed cart.
/// Atomicity and concurrency belong to <see cref="ICartRepository"/> and are covered on SQL Server by the integration tests.
/// </summary>
public sealed class CartLineCommandHandlerTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(_now);
    private readonly FakeCartRepository _carts = new();
    private readonly CartLine _line;

    public CartLineCommandHandlerTests()
    {
        var cart = Cart.Create("alice", _now).Value;
        var product = Product.Create(1, "Arabica", "groceries", 12.50m, 10, "In Stock", null, _now);
        _line = cart.AddItem(product, 2, _now).Value;
        _carts.Cart = cart;
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ChangeQuantity_applies_the_change_to_the_cart_of_the_user_and_returns_the_new_versions()
    {
        var handler = new ChangeCartLineQuantityCommandHandler(_carts, _time);

        var result = await handler.Handle(new ChangeCartLineQuantityCommand("alice", _line.Id, 5, ExpectedLineVersion: 0), CancellationToken);

        _carts.ChangedUserId.ShouldBe("alice");
        var cart = result.Value;
        cart.Version.ShouldBe(2);
        cart.Total.ShouldBe(62.50m);
        var line = cart.Lines.ShouldHaveSingleItem();
        line.Quantity.ShouldBe(5);
        line.Version.ShouldBe(1);
    }

    [Fact]
    public async Task ChangeQuantity_from_an_outdated_version_returns_the_conflict_of_the_cart()
    {
        var handler = new ChangeCartLineQuantityCommandHandler(_carts, _time);
        await handler.Handle(new ChangeCartLineQuantityCommand("alice", _line.Id, 5, ExpectedLineVersion: 0), CancellationToken);

        var result = await handler.Handle(new ChangeCartLineQuantityCommand("alice", _line.Id, 8, ExpectedLineVersion: 0), CancellationToken);

        result.FirstError.Code.ShouldBe("Cart.LineVersionConflict");
        result.FirstError.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public async Task ChangeQuantity_without_cart_returns_not_found()
    {
        _carts.Cart = null;
        var handler = new ChangeCartLineQuantityCommandHandler(_carts, _time);

        var result = await handler.Handle(new ChangeCartLineQuantityCommand("alice", _line.Id, 5, ExpectedLineVersion: 0), CancellationToken);

        result.FirstError.ShouldBe(CartErrors.CartNotFound);
    }

    [Fact]
    public async Task RemoveLine_returns_the_cart_without_the_line()
    {
        var handler = new RemoveCartLineCommandHandler(_carts, _time);

        var result = await handler.Handle(new RemoveCartLineCommand("alice", _line.Id, ExpectedLineVersion: 0), CancellationToken);

        result.Value.Lines.ShouldBeEmpty();
        result.Value.Version.ShouldBe(2);
        result.Value.Total.ShouldBe(0m);
    }

    /// <summary>
    /// In-memory stand-in for the repository contract: applies the change to the stored cart and returns it,
    /// or returns the errors (the aggregate leaves itself unchanged when it rejects a change).
    /// </summary>
    private sealed class FakeCartRepository : ICartRepository
    {
        public Cart? Cart { get; set; }

        public string? ChangedUserId { get; private set; }

        public Task<bool> TryAddAsync(Cart cart, CancellationToken cancellationToken) => throw new NotSupportedException();

        public async Task<ErrorOr<Cart>> ChangeAsync(
            string userId,
            Func<Cart, CancellationToken, ValueTask<ErrorOr<Success>>> change,
            CancellationToken cancellationToken)
        {
            ChangedUserId = userId;
            if (Cart is null)
            {
                return CartErrors.CartNotFound;
            }

            var result = await change(Cart, cancellationToken);
            if (result.IsError)
            {
                return result.Errors;
            }

            return Cart;
        }
    }
}
