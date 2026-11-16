using BeanStock.Application.Catalog.Synchronization;
using ErrorOr;
using Mediator;

namespace BeanStock.Worker.Scheduling;

/// <summary>
/// Sends <see cref="SynchronizeCatalogCommand"/> in its own scope, like an HTTP request would.
/// </summary>
/// <remarks>
/// The worker only triggers: whether a synchronization is due, already running elsewhere or failing is decided
/// by the use case. The details of a run are logged by the handler; this class only logs the outcome of a check.
/// </remarks>
internal sealed partial class CatalogSyncJob(IServiceScopeFactory scopeFactory, ILogger<CatalogSyncJob> logger)
    : ICatalogSyncJob
{
    public async Task<bool> RunAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.Send(SynchronizeCatalogCommand.Scheduled(interval), cancellationToken);

        return HandleResult(result, interval, logger);
    }

    /// <summary>
    /// Turns the answer of the use case into the outcome of the check, and logs it.
    /// </summary>
    /// <returns><see langword="false"/> if a due synchronization failed and must be retried later.</returns>
    internal static bool HandleResult(ErrorOr<CatalogSyncResult> result, TimeSpan interval, ILogger logger)
    {
        if (result.IsError)
        {
            var error = result.FirstError;

            // Another instance is synchronizing: nothing to retry, the next check will find it done.
            if (error.Code == CatalogSyncErrors.AlreadyRunning.Code)
            {
                return true;
            }

            LogCheckFailed(logger, error.Code, error.Description);
            return false;
        }

        if (result.Value is { Outcome: CatalogSyncOutcome.NotDue, LastSucceededAt: { } lastSucceededAt })
        {
            LogNotDue(logger, lastSucceededAt, lastSucceededAt + interval);
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Catalog synchronization not due: last success at {LastSucceededAt}, next one from {NextDueAt}.")]
    private static partial void LogNotDue(ILogger logger, DateTimeOffset lastSucceededAt, DateTimeOffset nextDueAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Scheduled catalog synchronization failed ({Code}): {Description}")]
    private static partial void LogCheckFailed(ILogger logger, string code, string description);
}
