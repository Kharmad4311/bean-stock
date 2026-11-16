namespace BeanStock.Domain.Catalog.Synchronization;

/// <summary>
/// State of a catalog synchronization run.
/// </summary>
public enum CatalogSyncStatus
{
    /// <summary>
    /// In progress. A run that stays in this state was interrupted by a crash; it has no effect on the
    /// catalog (the transaction was rolled back) nor on the schedule (only successful runs count).
    /// </summary>
    Running,

    Succeeded,

    /// <summary>Nothing was written to the catalog. The reason is kept in the run.</summary>
    Failed,
}
