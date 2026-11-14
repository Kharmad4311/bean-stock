using System.Net;
using System.Net.Http.Json;
using BeanStock.Application.Catalog.ListProducts;
using BeanStock.Application.Common.Paging;
using BeanStock.IntegrationTests.Infrastructure;

namespace BeanStock.IntegrationTests.Api;

public sealed class StartupTests(BeanStockApiFactory factory) : IClassFixture<BeanStockApiFactory>
{
    [Fact]
    public async Task Readiness_endpoint_reports_a_reachable_database()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Products_endpoint_returns_an_empty_first_page_on_a_fresh_database()
    {
        using var client = factory.CreateClient();

        var page = await client.GetFromJsonAsync<PagedResult<ProductResponse>>(
            "/products?page=1&pageSize=10",
            TestContext.Current.CancellationToken);

        page.ShouldNotBeNull();
        page.Items.ShouldBeEmpty();
        page.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Products_endpoint_rejects_an_invalid_page_size_with_a_problem_details()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/products?pageSize=0", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }
}
