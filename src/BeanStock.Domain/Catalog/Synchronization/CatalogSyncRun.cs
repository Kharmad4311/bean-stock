namespace BeanStock.Domain.Catalog.Synchronization;

/// <summary>
/// One execution of the catalog synchronization, whatever its trigger.
/// </summary>
/// <remarks>
/// The history of runs gives the date of the last successful synchronization, on which the shared schedule
/// of every worker instance relies, and makes failures visible after the fact.
/// </remarks>
public sealed class CatalogSyncRun
{
    public const int FailureReasonMaxLength = 1000;

    // Required by EF Core.
    private CatalogSyncRun()
    {
    }

    public Guid Id { get; private set; }

    public CatalogSyncTrigger Trigger { get; private set; }

    public CatalogSyncStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Number of products returned by the provider, valid or not.</summary>
    public int ProviderProductCount { get; private set; }

    public int AddedCount { get; private set; }

    public int UpdatedCount { get; private set; }

    public int ReactivatedCount { get; private set; }

    public int DeactivatedCount { get; private set; }

    public int UnchangedCount { get; private set; }

    /// <summary>Products ignored because their data was invalid or their identifier duplicated.</summary>
    public int SkippedCount { get; private set; }

    public string? FailureReason { get; private set; }

    public static CatalogSyncRun Start(CatalogSyncTrigger trigger, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        Trigger = trigger,
        Status = CatalogSyncStatus.Running,
        StartedAt = now,
    };

    public void Succeed(CatalogSyncCounts counts, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(counts);
        EnsureRunning();

        ProviderProductCount = counts.ProviderProducts;
        AddedCount = counts.Added;
        UpdatedCount = counts.Updated;
        ReactivatedCount = counts.Reactivated;
        DeactivatedCount = counts.Deactivated;
        UnchangedCount = counts.Unchanged;
        SkippedCount = counts.Skipped;
        Status = CatalogSyncStatus.Succeeded;
        CompletedAt = now;
    }

    public void Fail(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        EnsureRunning();

        FailureReason = reason.Length <= FailureReasonMaxLength ? reason : reason[..FailureReasonMaxLength];
        Status = CatalogSyncStatus.Failed;
        CompletedAt = now;
    }

    private void EnsureRunning()
    {
        if (Status != CatalogSyncStatus.Running)
        {
            throw new InvalidOperationException($"The synchronization run {Id} is already completed ({Status}).");
        }
    }
}
