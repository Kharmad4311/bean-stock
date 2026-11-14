namespace BeanStock.Application.Catalog.Synchronization;

public enum CatalogSyncOutcome
{
    /// <summary>The synchronization ran and was committed.</summary>
    Completed,

    /// <summary>Scheduled synchronization skipped: the last success is more recent than the interval.</summary>
    NotDue,
}
