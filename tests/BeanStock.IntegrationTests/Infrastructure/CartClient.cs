using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BeanStock.Application.Carts.Common;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BeanStock.IntegrationTests.Infrastructure;

/// <summary>
/// HTTP client of the cart API for one user (<c>X-User-Id</c> header), on one application instance.
/// </summary>
/// <remarks>
/// Two clients created from two different factories (see <c>WithWebHostBuilder</c>) talk to two distinct
/// application instances sharing the same database: this is how the tests reproduce a multi-instance deployment.
/// </remarks>
internal sealed class CartClient : IDisposable
{
    private readonly HttpClient _http;

    private CartClient(HttpClient http) => _http = http;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static CartClient For<TEntryPoint>(WebApplicationFactory<TEntryPoint> instance, string userId)
        where TEntryPoint : class
    {
        var http = instance.CreateClient();
        http.DefaultRequestHeaders.Add("X-User-Id", userId);
        return new CartClient(http);
    }

    public Task<HttpResponseMessage> CreateAsync() => _http.PostAsync("/me/cart", content: null, CancellationToken);

    public Task<HttpResponseMessage> GetAsync() => _http.GetAsync("/me/cart", CancellationToken);

    public Task<HttpResponseMessage> AddLineAsync(Guid productId, int quantity) =>
        _http.PostAsJsonAsync("/me/cart/lines", new { productId, quantity }, CancellationToken);

    public Task<HttpResponseMessage> ChangeQuantityAsync(Guid lineId, int quantity, int? version) =>
        _http.PutAsJsonAsync($"/me/cart/lines/{lineId}", new { quantity, version }, CancellationToken);

    public Task<HttpResponseMessage> RemoveLineAsync(Guid lineId, int? version) =>
        _http.DeleteAsync(
            version is null ? $"/me/cart/lines/{lineId}" : $"/me/cart/lines/{lineId}?version={version}",
            CancellationToken);

    /// <summary>Creates the cart (or gets it) and returns it.</summary>
    public async Task<CartResponse> CreateCartAsync() => await ReadCartAsync(await CreateAsync());

    /// <summary>Reads the cart, which must exist.</summary>
    public async Task<CartResponse> CartAsync() => await ReadCartAsync(await GetAsync());

    /// <summary>Adds a line, which must be accepted, and returns the cart.</summary>
    public async Task<CartResponse> AddAsync(Guid productId, int quantity) =>
        await ReadCartAsync(await AddLineAsync(productId, quantity));

    /// <summary>Changes a line, which must be accepted, and returns the cart.</summary>
    public async Task<CartResponse> ChangeAsync(Guid lineId, int quantity, int version) =>
        await ReadCartAsync(await ChangeQuantityAsync(lineId, quantity, version));

    public static async Task<CartResponse> ReadCartAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        using (response)
        {
            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Created))
            {
                var body = await response.Content.ReadAsStringAsync(CancellationToken);
                throw new InvalidOperationException($"Expected a cart, got {(int)response.StatusCode}: {body}");
            }

            var cart = await response.Content.ReadFromJsonAsync<CartResponse>(CancellationToken);
            return cart.ShouldNotBeNull();
        }
    }

    /// <summary>Checks the status and the stable error code of a ProblemDetails response, and returns its body.</summary>
    public static async Task<JsonElement> ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string? code = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        using (response)
        {
            response.StatusCode.ShouldBe(status);
            response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
            var problem = document.RootElement.Clone();
            if (code is not null)
            {
                problem.GetProperty("code").GetString().ShouldBe(code);
            }

            return problem;
        }
    }

    public void Dispose() => _http.Dispose();
}
