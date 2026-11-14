namespace BeanStock.Application.Catalog.Synchronization;

/// <summary>
/// Successful answer to <see cref="SynchronizeCatalogCommand"/>. Failures are returned as errors.
/// </summary>
/// <param name="Outcome">Whether the synchronization ran.</param>
/// <param name="Report">Details of the run, when <see cref="CatalogSyncOutcome.Completed"/>.</param>
/// <param name="LastSucceededAt">Completion date of the last successful synchronization.</param>
public sealed record CatalogSyncResult(
    CatalogSyncOutcome Outcome,
    CatalogSyncReport? Report,
    DateTimeOffset? LastSucceededAt)
{
    public static CatalogSyncResult Completed(CatalogSyncReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new CatalogSyncResult(CatalogSyncOutcome.Completed, report, report.CompletedAt);
    }

    public static CatalogSyncResult NotDue(DateTimeOffset lastSucceededAt) =>
        new(CatalogSyncOutcome.NotDue, Report: null, lastSucceededAt);
}
