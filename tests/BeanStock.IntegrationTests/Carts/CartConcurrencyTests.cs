using System.Net;
using BeanStock.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BeanStock.IntegrationTests.Carts;

/// <summary>
/// Parallel requests on the same cart, spread over two application instances that share the database.
/// </summary>
/// <remarks>
/// <see cref="_secondInstance"/> is a second, independent host (own services, DbContexts and connections) built
/// from the same configuration: the guarantees under test only rely on the database, as in a real deployment.
/// </remarks>
public sealed class CartConcurrencyTests(BeanStockApiFactory factory) : IClassFixture<BeanStockApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice";

    private WebApplicationFactory<Program> _secondInstance = null!;

    public async ValueTask InitializeAsync()
    {
        await factory.ResetAsync();
        _secondInstance = factory.WithWebHostBuilder(_ => { });
    }

    public async ValueTask DisposeAsync() => await _secondInstance.DisposeAsync();

    [Fact]
    public async Task Parallel_creations_create_a_single_cart()
    {
        using var instance1 = CartClient.For(factory, Alice);
        using var instance2 = CartClient.For(_secondInstance, Alice);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(index => index % 2 == 0 ? instance1.CreateAsync() : instance2.CreateAsync()));

        responses.Count(response => response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(19);
        var carts = await Task.WhenAll(responses.Select(CartClient.ReadCartAsync));
        carts.Select(cart => cart.CreatedAt).Distinct().ShouldHaveSingleItem();
        (await factory.CartCountAsync(Alice)).ShouldBe(1);
    }

    [Fact]
    public async Task Parallel_additions_of_the_same_product_keep_every_accepted_quantity()
    {
        var product = await factory.AddProductAsync();
        using var instance1 = CartClient.For(factory, Alice);
        using var instance2 = CartClient.For(_secondInstance, Alice);
        await instance1.CreateCartAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(index => index % 2 == 0 ? instance1.AddLineAsync(product.Id, 2) : instance2.AddLineAsync(product.Id, 2)));

        responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.OK);
        var cart = await instance1.CartAsync();
        cart.Lines.ShouldHaveSingleItem().Quantity.ShouldBe(20);
        cart.Version.ShouldBe(10);
    }

    [Fact]
    public async Task Parallel_additions_beyond_the_limit_only_accept_what_fits()
    {
        var product = await factory.AddProductAsync();
        using var instance1 = CartClient.For(factory, Alice);
        using var instance2 = CartClient.For(_secondInstance, Alice);
        await instance1.CreateCartAsync();
        await instance1.AddAsync(product.Id, 990);

        // 990 + 5 fits; any second addition of 5 would exceed 999.
        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(index => index % 2 == 0 ? instance1.AddLineAsync(product.Id, 5) : instance2.AddLineAsync(product.Id, 5)));

        responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.BadRequest).ShouldBe(5);
        var cart = await instance1.CartAsync();
        cart.Lines.ShouldHaveSingleItem().Quantity.ShouldBe(995);
        cart.Version.ShouldBe(2);
    }

    [Fact]
    public async Task Changes_of_two_different_lines_from_the_same_read_both_succeed()
    {
        // Example of the specification: Alice's cart is at version 4 with lines A and B; she changes A in one tab
        // and B in another, from the same read, each tab being served by a different instance.
        var productA = await factory.AddProductAsync("A");
        var productB = await factory.AddProductAsync("B");
        using var tab1 = CartClient.For(factory, Alice);
        using var tab2 = CartClient.For(_secondInstance, Alice);
        await tab1.CreateCartAsync();
        await tab1.AddAsync(productA.Id, 1);
        var setup = await tab1.AddAsync(productB.Id, 1);
        await tab1.ChangeAsync(setup.Lines[0].LineId, 2, setup.Lines[0].Version);
        await tab1.ChangeAsync(setup.Lines[1].LineId, 2, setup.Lines[1].Version);

        var read = await tab1.CartAsync();
        read.Version.ShouldBe(4);
        var (lineA, lineB) = (read.Lines[0], read.Lines[1]);

        var responses = await Task.WhenAll(
            tab1.ChangeQuantityAsync(lineA.LineId, 5, lineA.Version),
            tab2.ChangeQuantityAsync(lineB.LineId, 7, lineB.Version));

        responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.OK);
        var cart = await tab1.CartAsync();
        cart.Version.ShouldBe(6);
        cart.Lines[0].Quantity.ShouldBe(5);
        cart.Lines[1].Quantity.ShouldBe(7);
    }

    [Fact]
    public async Task Parallel_changes_of_the_same_line_from_the_same_version_accept_only_one()
    {
        var product = await factory.AddProductAsync();
        using var instance1 = CartClient.For(factory, Alice);
        using var instance2 = CartClient.For(_secondInstance, Alice);
        await instance1.CreateCartAsync();
        var line = (await instance1.AddAsync(product.Id, 1)).Lines[0];

        var responses = await Task.WhenAll(Enumerable.Range(2, 10)
            .Select(quantity => quantity % 2 == 0
                ? instance1.ChangeQuantityAsync(line.LineId, quantity, line.Version)
                : instance2.ChangeQuantityAsync(line.LineId, quantity, line.Version)));

        responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.Conflict).ShouldBe(9);
        var cart = await instance1.CartAsync();
        cart.Version.ShouldBe(2);
        cart.Lines.ShouldHaveSingleItem().Version.ShouldBe(line.Version + 1);
    }

    [Fact]
    public async Task Parallel_removal_and_change_of_a_line_never_both_succeed_nor_resurrect_it()
    {
        var product = await factory.AddProductAsync();
        using var instance1 = CartClient.For(factory, Alice);
        using var instance2 = CartClient.For(_secondInstance, Alice);
        await instance1.CreateCartAsync();
        var line = (await instance1.AddAsync(product.Id, 1)).Lines[0];

        var responses = await Task.WhenAll(
            instance1.RemoveLineAsync(line.LineId, line.Version),
            instance2.ChangeQuantityAsync(line.LineId, 5, line.Version));
        var (removal, change) = (responses[0], responses[1]);

        var cart = await instance1.CartAsync();
        cart.Version.ShouldBe(2);
        if (removal.StatusCode == HttpStatusCode.OK)
        {
            // Removal first: the change finds no line and cannot recreate it.
            change.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            cart.Lines.ShouldBeEmpty();
        }
        else
        {
            // Change first: the removal was based on an outdated version.
            removal.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            change.StatusCode.ShouldBe(HttpStatusCode.OK);
            cart.Lines.ShouldHaveSingleItem().Quantity.ShouldBe(5);
        }

        removal.Dispose();
        change.Dispose();
    }
}
