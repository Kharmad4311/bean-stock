namespace BeanStock.Worker.Scheduling;

/// <summary>
/// Schedule of the automatic catalog synchronization (configuration section <c>CatalogSync:Schedule</c>).
/// </summary>
/// <remarks>
/// Read again at every check: a change in <c>appsettings*.json</c> applies without restarting the worker.
/// Durations use the <see cref="TimeSpan"/> format: <c>"1.00:00:00"</c> (1 day), <c>"00:05:00"</c> (5 minutes).
/// </remarks>
internal sealed class CatalogSyncScheduleOptions
{
    public const string SectionName = "CatalogSync:Schedule";

    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan MinimumCheckInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaximumCheckInterval = TimeSpan.FromHours(1);

    /// <summary><see langword="false"/> stops automatic synchronizations; the manual endpoint still works.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minimum time between two successful synchronizations, shared by every worker instance. Daily by default.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// How often each worker checks whether a synchronization is due. It bounds the delay between the due date
    /// and the actual start, and the time needed to notice a configuration change.
    /// </summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Wait after a failed synchronization (provider down, suspicious catalog…) before the next attempt, so an
    /// incident at the provider is not hammered every <see cref="CheckInterval"/>. Never shorter than it.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(15);
}
