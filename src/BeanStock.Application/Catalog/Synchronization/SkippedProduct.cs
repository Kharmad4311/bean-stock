namespace BeanStock.Application.Catalog.Synchronization;

/// <summary>
/// Product of the provider catalog that was not applied, and why.
/// </summary>
internal sealed record SkippedProduct(int ExternalId, string Reason);
