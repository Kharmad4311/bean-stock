namespace BeanStock.Domain.Catalog.Synchronization;

/// <summary>
/// Outcome of a successful synchronization, product by product.
/// </summary>
/// <param name="ProviderProducts">Products returned by the provider, valid or not.</param>
/// <param name="Added">New products created locally.</param>
/// <param name="Updated">Existing active products whose values changed.</param>
/// <param name="Reactivated">Inactive products offered again by the provider.</param>
/// <param name="Deactivated">Active products no longer offered by the provider.</param>
/// <param name="Unchanged">Existing products left untouched.</param>
/// <param name="Skipped">Products ignored because their data was invalid or their identifier duplicated.</param>
public sealed record CatalogSyncCounts(
    int ProviderProducts,
    int Added,
    int Updated,
    int Reactivated,
    int Deactivated,
    int Unchanged,
    int Skipped);
