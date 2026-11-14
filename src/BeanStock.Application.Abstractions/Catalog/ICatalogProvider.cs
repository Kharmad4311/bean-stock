namespace BeanStock.Application.Abstractions.Catalog;

/// <summary>
/// External source of the product catalog.
/// </summary>
public interface ICatalogProvider
{
    /// <summary>
    /// Fetches the complete catalog. Either every product is returned, or an exception is thrown:
    /// a partial result is never returned.
    /// </summary>
    Task<IReadOnlyList<ProviderProduct>> GetAllProductsAsync(CancellationToken cancellationToken);
}
