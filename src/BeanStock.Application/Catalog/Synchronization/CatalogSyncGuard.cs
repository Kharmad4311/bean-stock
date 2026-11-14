using ErrorOr;

namespace BeanStock.Application.Catalog.Synchronization;

/// <summary>
/// Refuses to apply a provider catalog that looks truncated, before anything is written.
/// </summary>
/// <remarks>
/// Without this safeguard, an empty or partial answer of the provider would deactivate most of the catalog.
/// The products would come back at the next correct synchronization, but customers could not add them meanwhile.
/// </remarks>
internal static class CatalogSyncGuard
{
    public static ErrorOr<Success> Check(ProviderCatalogSnapshot snapshot, int activeProducts, double minimumCatalogRatio)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var minimumExpected = (int)Math.Ceiling(activeProducts * minimumCatalogRatio);
        var offeredProducts = snapshot.OfferedExternalIds.Count;

        if (snapshot.ValidProducts.Count == 0 || offeredProducts < minimumExpected)
        {
            return CatalogSyncErrors.SuspiciousCatalog(
                snapshot.ValidProducts.Count,
                offeredProducts,
                activeProducts,
                minimumExpected);
        }

        return Result.Success;
    }
}
