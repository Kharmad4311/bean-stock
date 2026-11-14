using System.Globalization;
using System.Net.Http.Json;
using BeanStock.Application.Abstractions.Catalog;
using BeanStock.Infrastructure.DummyJson.Contracts;
using Microsoft.Extensions.Options;

namespace BeanStock.Infrastructure.DummyJson;

/// <summary>
/// Reads the full DummyJSON catalog, page by page.
/// </summary>
/// <remarks>
/// Any HTTP error or inconsistent response throws: callers never receive a partial catalog.
/// Transient failures are retried by the resilience handler configured on the HttpClient.
/// </remarks>
internal sealed class DummyJsonCatalogProvider(HttpClient httpClient, IOptions<DummyJsonOptions> options)
    : ICatalogProvider
{
    private const string SelectedFields = "title,category,price,stock,availabilityStatus,meta";

    public async Task<IReadOnlyList<ProviderProduct>> GetAllProductsAsync(CancellationToken cancellationToken)
    {
        var pageSize = options.Value.PageSize;
        var products = new List<ProviderProduct>();
        int? expectedTotal = null;

        while (expectedTotal is null || products.Count < expectedTotal)
        {
            var page = await GetPageAsync(skip: products.Count, pageSize, cancellationToken);

            expectedTotal ??= page.Total;
            if (page.Total != expectedTotal)
            {
                throw new InvalidOperationException(
                    $"DummyJSON total changed during the fetch ({expectedTotal} then {page.Total}).");
            }

            if (page.Products.Count == 0 && products.Count < expectedTotal)
            {
                throw new InvalidOperationException(
                    $"DummyJSON returned an empty page after {products.Count} of {expectedTotal} products.");
            }

            products.AddRange(page.Products.Select(ToProviderProduct));
        }

        return products;
    }

    private async Task<DummyJsonProductsPage> GetPageAsync(int skip, int limit, CancellationToken cancellationToken)
    {
        var requestUri = string.Create(
            CultureInfo.InvariantCulture,
            $"products?limit={limit}&skip={skip}&select={SelectedFields}");

        return await httpClient.GetFromJsonAsync<DummyJsonProductsPage>(requestUri, cancellationToken)
            ?? throw new InvalidOperationException("DummyJSON returned an empty response body.");
    }

    private static ProviderProduct ToProviderProduct(DummyJsonProduct product) => new(
        ExternalId: product.Id,
        Name: product.Title,
        Category: product.Category,
        Price: product.Price,
        Stock: product.Stock,
        AvailabilityStatus: product.AvailabilityStatus ?? string.Empty,
        UpdatedAt: product.Meta?.UpdatedAt);
}
