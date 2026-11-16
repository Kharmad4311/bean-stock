using System.Net;
using BeanStock.IntegrationTests.Infrastructure;

namespace BeanStock.IntegrationTests.Carts;

/// <summary>
/// Business rules of the cart through the HTTP API, on a real SQL Server.
/// </summary>
public sealed class CartTests(BeanStockApiFactory factory) : IClassFixture<BeanStockApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice";
    private const string Bob = "bob";

    public async ValueTask InitializeAsync() => await factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Request_without_user_header_is_rejected_with_401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/me/cart", TestContext.Current.CancellationToken);

        await CartClient.ShouldBeProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("alice smith")]
    [InlineData("alice,bob")]
    public async Task Request_with_an_invalid_user_header_is_rejected_with_401(string userId)
    {
        using var client = CartClient.For(factory, userId);

        await CartClient.ShouldBeProblemAsync(await client.GetAsync(), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_returns_201_with_an_empty_cart_then_200_with_the_same_cart()
    {
        using var alice = CartClient.For(factory, Alice);

        var first = await alice.CreateAsync();
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await CartClient.ReadCartAsync(first);

        var second = await alice.CreateAsync();
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var existing = await CartClient.ReadCartAsync(second);

        created.Version.ShouldBe(0);
        created.Lines.ShouldBeEmpty();
        created.Total.ShouldBe(0m);
        existing.CreatedAt.ShouldBe(created.CreatedAt);
        (await factory.CartCountAsync(Alice)).ShouldBe(1);
    }

    [Fact]
    public async Task Reading_or_changing_a_cart_that_does_not_exist_returns_404()
    {
        var product = await factory.AddProductAsync();
        using var alice = CartClient.For(factory, Alice);

        await CartClient.ShouldBeProblemAsync(await alice.GetAsync(), HttpStatusCode.NotFound, "Cart.NotFound");
        await CartClient.ShouldBeProblemAsync(await alice.AddLineAsync(product.Id, 1), HttpStatusCode.NotFound, "Cart.NotFound");
        (await factory.CartCountAsync(Alice)).ShouldBe(0);
    }

    [Fact]
    public async Task Adding_a_product_captures_its_name_and_price_and_increments_the_versions()
    {
        var arabica = await factory.AddProductAsync("Arabica", 12.50m);
        var robusta = await factory.AddProductAsync("Robusta", 8m);
        using var alice = CartClient.For(factory, Alice);
        await alice.CreateCartAsync();

        await alice.AddAsync(arabica.Id, 2);
        var cart = await alice.AddAsync(robusta.Id, 1);

        cart.Version.ShouldBe(2);
        cart.Total.ShouldBe(33m);
        cart.Lines.Count.ShouldBe(2);
        var line = cart.Lines[0];
        line.ProductId.ShouldBe(arabica.Id);
        line.ProductName.ShouldBe("Arabica");
        line.UnitPrice.ShouldBe(12.50m);
        line.Quantity.ShouldBe(2);
        line.Subtotal.ShouldBe(25m);
        line.Version.ShouldBe(0);
    }

    [Fact]
    public async Task Adding_a_product_already_in_the_cart_increases_the_quantity_of_its_line()
    {
        var product = await factory.AddProductAsync();
        using var alice = CartClient.For(factory, Alice);
        await alice.CreateCartAsync();
        await alice.AddAsync(product.Id, 2);

        var cart = await alice.AddAsync(product.Id, 3);

        var line = cart.Lines.ShouldHaveSingleItem();
        line.Quantity.ShouldBe(5);
        line.Version.ShouldBe(1);
        cart.Version.ShouldBe(2);
    }

    [Theory]
    [InlineData(0, "Cart.QuantityOutOfRange")]
    [InlineData(1000, "Cart.QuantityOutOfRange")]
    [InlineData(995, "Cart.QuantityLimitExceeded")]
    public async Task Rejected_addition_leaves_the_cart_unchanged(int quantity, string code)
    {
        var product = await factory.AddProductAsync();
        using var alice = CartClient.For(factory, Alice);
        await alice.CreateCartAsync();
        var before = await alice.AddAsync(product.Id, 5);

        await CartClient.ShouldBeProblemAsync(await alice.AddLineAsync(product.Id, quantity), HttpStatusCode.BadRequest, code);

        var after = await alice.CartAsync();
        after.ShouldBeEquivalentTo(before);
    }

    [Fact]
    public async Task Adding_an_unknown_product_is_rejected()
    {
        using var alice = CartClient.For(factory, Alice);
        await alice.CreateCartAsync();

        await CartClient.ShouldBeProblemAsync(
            await alice.AddLineAsync(Guid.NewGuid(), 1),
            HttpStatusCode.BadRequest,
            "Cart.ProductNotFound");

        (await alice.CartAsync()).Version.ShouldBe(0);
    }

    [Fact]
    public async Task Catalog_changes_do_not_alter_the_lines_already_in_the_cart()
    {
        var product = await factory.AddProductAsync("Arabica", 10m);
        using var alice = CartClient.For(factory, Alice);
        await alice.CreateCartAsync();
        await alice.AddAsync(product.Id, 2);

        await factory.UpdateProductAsync(product.Id, "Arabica Premium", 15m);
        var cart = await alice.AddAsync(product.Id, 1);

        var line = cart.Lines.ShouldHaveSingleItem();
        line.ProductName.ShouldBe("Arabica");
        line.UnitPrice.ShouldBe(10m);
        cart.Total.ShouldBe(30m);
    }

    [Fact]
    public async Task Product_removed_from_the_catalog_stays_readable_in_the_cart_but_cannot_be_added()
    {
        var product = await factory.AddProductAsync();
        using var alice = CartClient.For(factory, Alice);
        await alice.CreateCartAsync();
        await alice.AddAsync(product.Id, 2);

        await factory.DeactivateProductAsync(product.Id);

        var cart = await alice.CartAsync();
        cart.Lines.ShouldHaveSingleItem().Quantity.ShouldBe(2);
        await CartClient.ShouldBeProblemAsync(
            await alice.AddLineAsync(product.Id, 1),
            HttpStatusCode.BadRequest,
            "Cart.ProductUnavailable");
        (await alice.CartAsync()).ShouldBeEquivalentTo(cart);
    }

    [Fact]
    public async Task Changing_a_quantity_replaces_it_and_increments_the_versions()
    {
        var product = await factory.AddProductAsync();
        using var alice = CartClient.For(factory, Alice);
        await alice.CreateCartAsync();
        var line = (await alice.AddAsync(product.Id, 2)).Lines[0];

        var cart = await alice.ChangeAsync(line.LineId, 7, line.Version);

        var changedLine = cart.Lines.ShouldHaveSingleItem();
        changedLine.Quantity.ShouldBe(7);
        changedLine.Version.ShouldBe(line.Version + 1);
        cart.Version.ShouldBe(2);
    }

    [Fact]
    public async Task Changing_a_line_from_an_outdated_version_returns_409_and_keeps_the_other_change()
    {
        var product = await factory.AddProductAsync();
        using var alice = CartClient.For(factory, Alice);
        await alice.CreateCartAsync();
        var line = (await alice.AddAsync(product.Id, 2)).Lines[0];
        var afterFirstChange = await alice.ChangeAsync(line.LineId, 3, line.Version);

        var problem = await CartClient.ShouldBeProblemAsync(
            await alice.ChangeQuantityAsync(line.LineId, 9, line.Version),
            HttpStatusCode.Conflict,
            "Cart.LineVersionConflict");

        problem.GetProperty("expectedVersion").GetInt32().ShouldBe(0);
        problem.GetProperty("currentVersion").GetInt32().ShouldBe(1);
        (await alice.CartAsync()).ShouldBeEquivalentTo(afterFirstChange);
    }

    [Fact]
    public async Task Changing_or_removing_a_line_without_its_version_is_rejected()
    {
        var product = await factory.AddProductAsync();
        using var alice = CartClient.For(factory, Alice);
        await alice.CreateCartAsync();
        var line = (await alice.AddAsync(product.Id, 2)).Lines[0];

        await CartClient.ShouldBeProblemAsync(await alice.ChangeQuantityAsync(line.LineId, 3, version: null), HttpStatusCode.BadRequest);
        await CartClient.ShouldBeProblemAsync(await alice.RemoveLineAsync(line.LineId, version: null), HttpStatusCode.BadRequest);

        (await alice.CartAsync()).Version.ShouldBe(1);
    }

    [Fact]
    public async Task Removed_line_does_not_come_back_with_an_outdated_change()
    {
        var product = await factory.AddProductAsync();
        using var alice = CartClient.For(factory, Alice);
        await alice.CreateCartAsync();
        var line = (await alice.AddAsync(product.Id, 2)).Lines[0];

        var afterRemoval = await CartClient.ReadCartAsync(await alice.RemoveLineAsync(line.LineId, line.Version));
        afterRemoval.Lines.ShouldBeEmpty();
        afterRemoval.Version.ShouldBe(2);

        await CartClient.ShouldBeProblemAsync(
            await alice.ChangeQuantityAsync(line.LineId, 5, line.Version),
            HttpStatusCode.NotFound,
            "Cart.LineNotFound");
        (await alice.CartAsync()).ShouldBeEquivalentTo(afterRemoval);
    }

    [Fact]
    public async Task A_user_can_neither_read_nor_change_the_cart_of_another_user()
    {
        var product = await factory.AddProductAsync();
        using var alice = CartClient.For(factory, Alice);
        using var bob = CartClient.For(factory, Bob);
        await alice.CreateCartAsync();
        var aliceCart = await alice.AddAsync(product.Id, 2);
        var aliceLine = aliceCart.Lines[0];

        await CartClient.ShouldBeProblemAsync(await bob.GetAsync(), HttpStatusCode.NotFound, "Cart.NotFound");
        await bob.CreateCartAsync();
        await CartClient.ShouldBeProblemAsync(
            await bob.ChangeQuantityAsync(aliceLine.LineId, 9, aliceLine.Version),
            HttpStatusCode.NotFound,
            "Cart.LineNotFound");
        await CartClient.ShouldBeProblemAsync(
            await bob.RemoveLineAsync(aliceLine.LineId, aliceLine.Version),
            HttpStatusCode.NotFound,
            "Cart.LineNotFound");

        (await alice.CartAsync()).ShouldBeEquivalentTo(aliceCart);
        (await bob.CartAsync()).Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task Failure_while_saving_a_change_leaves_lines_and_versions_unchanged()
    {
        var product = await factory.AddProductAsync();
        using var alice = CartClient.For(factory, Alice);
        await alice.CreateCartAsync();
        var before = await alice.AddAsync(product.Id, 2);
        var line = before.Lines[0];

        factory.SaveChangesInterceptor.FailNextCartSave();
        var response = await alice.ChangeQuantityAsync(line.LineId, 5, line.Version);

        await CartClient.ShouldBeProblemAsync(response, HttpStatusCode.InternalServerError);
        (await alice.CartAsync()).ShouldBeEquivalentTo(before);

        // The failed request left nothing behind: the same change, retried, is accepted.
        var retried = await alice.ChangeAsync(line.LineId, 5, line.Version);
        retried.Version.ShouldBe(before.Version + 1);
    }
}
