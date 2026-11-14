namespace BeanStock.Infrastructure.DummyJson.Contracts;

/// <summary>
/// Response of <c>GET /products</c>. Only the fields used by BeanStock are mapped.
/// </summary>
internal sealed record DummyJsonProductsPage(
    IReadOnlyList<DummyJsonProduct> Products,
    int Total,
    int Skip,
    int Limit);

internal sealed record DummyJsonProduct(
    int Id,
    string Title,
    string Category,
    decimal Price,
    int Stock,
    string? AvailabilityStatus,
    DummyJsonProductMeta? Meta);

internal sealed record DummyJsonProductMeta(DateTimeOffset? UpdatedAt);
