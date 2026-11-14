using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using BeanStock.Application.Abstractions.Catalog;
using Microsoft.Extensions.Options;

namespace BeanStock.Infrastructure.DummyJson.UnitTests;

public sealed class DummyJsonCatalogProviderTests
{
    [Fact]
    public async Task Fetches_every_page_until_the_announced_total()
    {
        using var handler = new StubDummyJsonHandler(total: 5);

        var products = await FetchAsync(handler, pageSize: 2);

        products.Count.ShouldBe(5);
        products.Select(product => product.ExternalId).ShouldBeUnique();
        string.Join(',', handler.RequestedSkips).ShouldBe("0,2,4");
    }

    [Fact]
    public async Task Maps_the_provider_fields()
    {
        using var handler = new StubDummyJsonHandler(total: 1);

        var product = (await FetchAsync(handler, pageSize: 10)).ShouldHaveSingleItem();

        product.ExternalId.ShouldBe(1);
        product.Name.ShouldBe("Product 1");
        product.Category.ShouldBe("groceries");
        product.Price.ShouldBe(9.99m);
        product.Stock.ShouldBe(7);
        product.AvailabilityStatus.ShouldBe("In Stock");
        product.UpdatedAt.ShouldBe(new DateTimeOffset(2024, 5, 23, 8, 56, 21, 618, TimeSpan.Zero));
    }

    [Fact]
    public async Task Error_response_is_reported_as_a_provider_failure()
    {
        using var handler = new StubDummyJsonHandler(total: 5) { FailingStatus = HttpStatusCode.ServiceUnavailable };

        await Should.ThrowAsync<CatalogProviderException>(() => FetchAsync(handler, pageSize: 2));
    }

    [Fact]
    public async Task Total_changing_during_the_fetch_is_reported_as_a_provider_failure()
    {
        using var handler = new StubDummyJsonHandler(total: 5) { TotalAfterFirstPage = 6 };

        await Should.ThrowAsync<CatalogProviderException>(() => FetchAsync(handler, pageSize: 2));
    }

    [Fact]
    public async Task Empty_page_before_the_total_is_reported_as_a_provider_failure()
    {
        using var handler = new StubDummyJsonHandler(total: 5) { EmptyFromSkip = 2 };

        await Should.ThrowAsync<CatalogProviderException>(() => FetchAsync(handler, pageSize: 2));
    }

    [Fact]
    public async Task Malformed_body_is_reported_as_a_provider_failure()
    {
        using var handler = new StubDummyJsonHandler(total: 5) { RawBody = "<html>maintenance</html>" };

        await Should.ThrowAsync<CatalogProviderException>(() => FetchAsync(handler, pageSize: 2));
    }

    [Fact]
    public async Task Cancellation_by_the_caller_is_not_reported_as_a_provider_failure()
    {
        using var handler = new StubDummyJsonHandler(total: 5);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // ThrowsAny: HttpClient may surface the cancellation as a TaskCanceledException (derived type).
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FetchAsync(handler, pageSize: 2, cancellation.Token));
    }

    private static async Task<IReadOnlyList<ProviderProduct>> FetchAsync(
        StubDummyJsonHandler handler,
        int pageSize,
        CancellationToken? cancellationToken = null)
    {
        using var httpClient = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://dummyjson.test/"),
        };
        var provider = new DummyJsonCatalogProvider(
            httpClient,
            Options.Create(new DummyJsonOptions { PageSize = pageSize }));

        return await provider.GetAllProductsAsync(cancellationToken ?? TestContext.Current.CancellationToken);
    }

    /// <summary>Simulates <c>GET /products?limit=…&amp;skip=…</c> for a catalog of <c>total</c> products.</summary>
    private sealed class StubDummyJsonHandler(int total) : HttpMessageHandler
    {
        private readonly List<int> _requestedSkips = [];

        public List<int> RequestedSkips => _requestedSkips;

        public HttpStatusCode? FailingStatus { get; init; }

        public int? TotalAfterFirstPage { get; init; }

        public int? EmptyFromSkip { get; init; }

        public string? RawBody { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (FailingStatus is { } status)
            {
                return Task.FromResult(new HttpResponseMessage(status));
            }

            if (RawBody is not null)
            {
                return Task.FromResult(Json(RawBody));
            }

            var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
            var limit = int.Parse(query["limit"]!, CultureInfo.InvariantCulture);
            var skip = int.Parse(query["skip"]!, CultureInfo.InvariantCulture);
            _requestedSkips.Add(skip);

            var announcedTotal = skip > 0 && TotalAfterFirstPage is { } changedTotal ? changedTotal : total;
            var count = EmptyFromSkip is { } emptyFrom && skip >= emptyFrom
                ? 0
                : Math.Max(0, Math.Min(limit, total - skip));

            var page = new
            {
                products = Enumerable.Range(skip + 1, count).Select(id => new
                {
                    id,
                    title = $"Product {id}",
                    category = "groceries",
                    price = 9.99m,
                    stock = 7,
                    availabilityStatus = "In Stock",
                    meta = new { updatedAt = "2024-05-23T08:56:21.618Z" },
                }),
                total = announcedTotal,
                skip,
                limit,
            };

            return Task.FromResult(Json(JsonSerializer.Serialize(page)));
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
