using Microsoft.Extensions.Options;

namespace BeanStock.Worker.Scheduling;

/// <summary>
/// Trigger of the automatic catalog synchronization: checks regularly whether a synchronization is due.
/// </summary>
/// <remarks>
/// <para>
/// The schedule is shared by every worker instance instead of being a timer per instance: each check sends a
/// scheduled command, and the use case decides, under the distributed lock, whether the last successful
/// synchronization is older than <see cref="CatalogSyncScheduleOptions.Interval"/>. Hence one synchronization
/// per interval whatever the number of instances, no extra run when a worker restarts, and a catch-up run as soon
/// as a worker starts after a long stop.
/// </para>
/// <para>
/// Options are read at every check, so a change in configuration applies without restart. An invalid change is
/// ignored (the last valid options are kept) instead of stopping the worker.
/// </para>
/// </remarks>
internal sealed partial class CatalogSyncScheduler(
    ICatalogSyncJob job,
    IOptionsMonitor<CatalogSyncScheduleOptions> options,
    TimeProvider timeProvider,
    ILogger<CatalogSyncScheduler> logger) : BackgroundService
{
    // The first read cannot fail: options are validated at startup (ValidateOnStart).
    private CatalogSyncScheduleOptions? _lastValidOptions;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initial = ReadOptions();
        LogStarted(logger, initial.Enabled, initial.Interval, initial.CheckInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            var current = ReadOptions();
            var succeeded = !current.Enabled || await CheckAsync(current.Interval, stoppingToken);

            var delay = succeeded ? current.CheckInterval : Max(current.RetryDelay, current.CheckInterval);
            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task<bool> CheckAsync(TimeSpan interval, CancellationToken stoppingToken)
    {
        try
        {
            return await job.RunAsync(interval, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return true;
        }
#pragma warning disable CA1031 // The schedule must survive any failure (database down…): the next check retries.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogCheckFailed(logger, exception);
            return false;
        }
    }

    private CatalogSyncScheduleOptions ReadOptions()
    {
        try
        {
            var current = options.CurrentValue;
            _lastValidOptions = current;
            return current;
        }
        catch (OptionsValidationException exception) when (_lastValidOptions is { } previous)
        {
            LogInvalidOptions(logger, exception.Message);
            return previous;
        }
    }

    private static TimeSpan Max(TimeSpan first, TimeSpan second) => first > second ? first : second;

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalog synchronization schedule started (enabled: {Enabled}, interval: {Interval}, check every {CheckInterval}).")]
    private static partial void LogStarted(ILogger logger, bool enabled, TimeSpan interval, TimeSpan checkInterval);

    [LoggerMessage(Level = LogLevel.Error, Message = "Catalog synchronization check failed. It will be retried.")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Invalid catalog synchronization schedule ignored, previous settings kept: {Reason}")]
    private static partial void LogInvalidOptions(ILogger logger, string reason);
}
