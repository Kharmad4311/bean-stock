using BeanStock.Domain.Catalog.Synchronization;

namespace BeanStock.Application.Catalog.Synchronization;

/// <summary>
/// Report of a completed synchronization, as returned by the manual endpoint.
/// </summary>
public sealed record CatalogSyncReport(
    Guid RunId,
    CatalogSyncTrigger Trigger,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int ProviderProducts,
    int Added,
    int Updated,
    int Reactivated,
    int Deactivated,
    int Unchanged,
    int Skipped)
{
    internal static CatalogSyncReport From(CatalogSyncRun run) => new(
        run.Id,
        run.Trigger,
        run.StartedAt,
        run.CompletedAt ?? throw new InvalidOperationException("The synchronization run is not completed."),
        run.ProviderProductCount,
        run.AddedCount,
        run.UpdatedCount,
        run.ReactivatedCount,
        run.DeactivatedCount,
        run.UnchangedCount,
        run.SkippedCount);
}
