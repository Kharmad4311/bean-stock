using BeanStock.Application.Common.Errors;
using ErrorOr;

namespace BeanStock.Application.Catalog.Synchronization;

/// <summary>
/// Expected failures of the catalog synchronization. In every case the local catalog is left unchanged.
/// Codes are stable: clients may rely on them.
/// </summary>
public static class CatalogSyncErrors
{
    public static Error AlreadyRunning => Error.Conflict(
        code: "CatalogSync.AlreadyRunning",
        description: "A catalog synchronization is already running on an instance of the service.");

    /// <summary>
    /// The exclusive lock was lost during the run (connection cut): another instance may have taken over,
    /// so nothing is committed.
    /// </summary>
    public static Error LockLost => Error.Conflict(
        code: "CatalogSync.LockLost",
        description: "The synchronization lost its exclusive lock before completing. The local catalog is unchanged.");

    public static Error ProviderUnavailable => Error.Custom(
        type: ApplicationErrorTypes.Upstream,
        code: "CatalogSync.ProviderUnavailable",
        description: "The catalog provider could not be read. The local catalog is unchanged.");

    public static Error SuspiciousCatalog(int validProducts, int offeredProducts, int activeProducts, int minimumExpected) =>
        Error.Custom(
            type: ApplicationErrorTypes.Upstream,
            code: "CatalogSync.SuspiciousCatalog",
            description: "The provider returned an empty or abnormally small catalog. The local catalog is unchanged.",
            metadata: new Dictionary<string, object>
            {
                ["validProducts"] = validProducts,
                ["offeredProducts"] = offeredProducts,
                ["activeProducts"] = activeProducts,
                ["minimumExpected"] = minimumExpected,
            });
}
