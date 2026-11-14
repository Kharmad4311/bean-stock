namespace BeanStock.Application.Catalog.ListProducts;

public sealed record ProductResponse(
    Guid Id,
    int ExternalId,
    string Name,
    string Category,
    decimal Price,
    int Stock,
    string AvailabilityStatus,
    DateTimeOffset LastSynchronizedAt);
