namespace BeanStock.Application.Catalog.ListProducts;

/// <summary>
/// Product of the local catalog, as listed by the API. <c>LastUpdatedAt</c> is the date of the last local change
/// of the product (see <c>Product.LastUpdatedAt</c>).
/// </summary>
public sealed record ProductResponse(
    Guid Id,
    int ExternalId,
    string Name,
    string Category,
    decimal Price,
    int Stock,
    string AvailabilityStatus,
    DateTimeOffset LastUpdatedAt);
