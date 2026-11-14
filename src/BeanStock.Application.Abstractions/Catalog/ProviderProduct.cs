namespace BeanStock.Application.Abstractions.Catalog;

/// <summary>
/// Product as described by the external provider.
/// </summary>
public sealed record ProviderProduct(
    int ExternalId,
    string Name,
    string Category,
    decimal Price,
    int Stock,
    string AvailabilityStatus,
    DateTimeOffset? UpdatedAt);
