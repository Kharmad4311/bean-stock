namespace BeanStock.Domain.Catalog.Synchronization;

/// <summary>
/// What started a catalog synchronization.
/// </summary>
public enum CatalogSyncTrigger
{
    /// <summary>Automatic synchronization started by the worker schedule.</summary>
    Scheduled,

    /// <summary>Synchronization requested on demand through the API.</summary>
    Manual,
}
