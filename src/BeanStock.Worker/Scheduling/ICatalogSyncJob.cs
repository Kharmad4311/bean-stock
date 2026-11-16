namespace BeanStock.Worker.Scheduling;

/// <summary>
/// One check of the schedule: asks for a scheduled synchronization, which runs only if it is due.
/// </summary>
internal interface ICatalogSyncJob
{
    /// <returns><see langword="false"/> if a due synchronization failed and should be retried later.</returns>
    Task<bool> RunAsync(TimeSpan interval, CancellationToken cancellationToken);
}
