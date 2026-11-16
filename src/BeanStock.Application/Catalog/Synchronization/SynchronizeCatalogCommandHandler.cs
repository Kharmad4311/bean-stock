using BeanStock.Application.Abstractions.Catalog;
using BeanStock.Application.Abstractions.Concurrency;
using BeanStock.Application.Abstractions.Persistence;
using BeanStock.Domain.Catalog;
using BeanStock.Domain.Catalog.Synchronization;
using ErrorOr;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BeanStock.Application.Catalog.Synchronization;

/// <summary>
/// Runs a catalog synchronization: exclusive across instances, all-or-nothing, written in batches.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>Acquire the distributed lock without waiting; if another instance holds it, answer
/// <see cref="CatalogSyncErrors.AlreadyRunning"/>. The lock covers the whole run, fetch included, so an older
/// snapshot of the provider can never overwrite a more recent one.</item>
/// <item>Scheduled runs only: skip if the last success is more recent than the requested interval.</item>
/// <item>Record the run, fetch the full provider catalog, sort out invalid products, check it is plausible.
/// Any failure here writes nothing to the catalog.</item>
/// <item>In one transaction: apply the products batch by batch, then deactivate the active products that
/// the provider no longer offers, batch by batch. Check the lock is still held (fencing), then commit.</item>
/// <item>Record the outcome of the run, success or failure.</item>
/// </list>
/// </remarks>
public sealed partial class SynchronizeCatalogCommandHandler(
    IDistributedLock distributedLock,
    ICatalogProvider provider,
    ICatalogSyncStore catalog,
    ICatalogSyncRunStore runs,
    IUnitOfWork unitOfWork,
    IValidator<ProviderProduct> productValidator,
    IOptions<CatalogSynchronizationOptions> options,
    TimeProvider timeProvider,
    ILogger<SynchronizeCatalogCommandHandler> logger)
    : ICommandHandler<SynchronizeCatalogCommand, ErrorOr<CatalogSyncResult>>
{
    /// <summary>Name of the distributed lock: one synchronization at a time across every instance.</summary>
    public const string LockResource = "BeanStock.CatalogSynchronization";

    public async ValueTask<ErrorOr<CatalogSyncResult>> Handle(
        SynchronizeCatalogCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var synchronizationLock = await distributedLock.TryAcquireAsync(LockResource, cancellationToken);
        if (synchronizationLock is null)
        {
            LogAlreadyRunning(logger, command.Trigger);
            return CatalogSyncErrors.AlreadyRunning;
        }

        if (command.MinimumInterval is { } interval)
        {
            var lastSuccess = await runs.GetLastSuccessfulCompletionAsync(cancellationToken);
            if (lastSuccess is { } lastSucceededAt && timeProvider.GetUtcNow() - lastSucceededAt < interval)
            {
                return CatalogSyncResult.NotDue(lastSucceededAt);
            }
        }

        var run = CatalogSyncRun.Start(command.Trigger, timeProvider.GetUtcNow());
        await runs.AddAsync(run, cancellationToken);
        LogStarted(logger, run.Id, command.Trigger);

        ErrorOr<CatalogSyncCounts> outcome;
        try
        {
            outcome = await SynchronizeAsync(synchronizationLock, cancellationToken);
        }
        catch (CatalogProviderException exception)
        {
            LogProviderUnavailable(logger, exception, run.Id);
            await FailAsync(run, $"{CatalogSyncErrors.ProviderUnavailable.Code}: {exception.Message}");
            return CatalogSyncErrors.ProviderUnavailable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogCanceled(logger, run.Id);
            await FailAsync(run, "Canceled before completion. Nothing was written to the catalog.");
            throw;
        }
        catch (Exception exception)
        {
            LogFailed(logger, exception, run.Id);
            await FailAsync(run, $"Unexpected error: {exception.Message}");
            throw;
        }

        if (outcome.IsError)
        {
            var error = outcome.FirstError;
            LogRejected(logger, run.Id, error.Code, error.Description);
            await FailAsync(run, $"{error.Code}: {error.Description}");
            return outcome.Errors;
        }

        run.Succeed(outcome.Value, timeProvider.GetUtcNow());
        await runs.UpdateAsync(run, CancellationToken.None);

        var report = CatalogSyncReport.From(run);
        LogCompleted(
            logger,
            run.Id,
            report.ProviderProducts,
            report.Added,
            report.Updated,
            report.Reactivated,
            report.Deactivated,
            report.Unchanged,
            report.Skipped);

        return CatalogSyncResult.Completed(report);
    }

    private async Task<ErrorOr<CatalogSyncCounts>> SynchronizeAsync(
        IDistributedLockHandle synchronizationLock,
        CancellationToken cancellationToken)
    {
        // 1. Read and check everything before writing anything.
        var providerProducts = await provider.GetAllProductsAsync(cancellationToken);
        var snapshot = ProviderCatalogSnapshot.Create(providerProducts, productValidator);
        foreach (var skipped in snapshot.SkippedProducts)
        {
            LogProductSkipped(logger, skipped.ExternalId, skipped.Reason);
        }

        var activeExternalIds = await catalog.GetActiveExternalIdsAsync(cancellationToken);
        var settings = options.Value;
        var guard = CatalogSyncGuard.Check(snapshot, activeExternalIds.Count, settings.MinimumCatalogRatio);
        if (guard.IsError)
        {
            return guard.Errors;
        }

        // 2. Apply in batches, in a single transaction: a failure at any point rolls everything back.
        var now = timeProvider.GetUtcNow();
        int added = 0, updated = 0, reactivated = 0, deactivated = 0, unchanged = 0;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        foreach (var batch in snapshot.ValidProducts.Chunk(settings.BatchSize))
        {
            var externalIds = Array.ConvertAll(batch, product => product.ExternalId);
            var existing = (await catalog.GetByExternalIdsAsync(externalIds, cancellationToken))
                .ToDictionary(product => product.ExternalId);

            foreach (var item in batch)
            {
                if (!existing.TryGetValue(item.ExternalId, out var product))
                {
                    catalog.Add(Product.Create(
                        item.ExternalId,
                        item.Name,
                        item.Category,
                        item.Price,
                        item.Stock,
                        item.AvailabilityStatus,
                        item.UpdatedAt,
                        now));
                    added++;
                    continue;
                }

                var change = product.ApplyProviderData(
                    item.Name,
                    item.Category,
                    item.Price,
                    item.Stock,
                    item.AvailabilityStatus,
                    item.UpdatedAt,
                    now);

                switch (change)
                {
                    case ProductSyncChange.Updated:
                        updated++;
                        break;
                    case ProductSyncChange.Reactivated:
                        reactivated++;
                        break;
                    default:
                        unchanged++;
                        break;
                }
            }

            await catalog.SaveBatchAsync(cancellationToken);
        }

        // 3. Products no longer offered are deactivated, never deleted: carts that contain them stay readable.
        var missingExternalIds = activeExternalIds
            .Where(externalId => !snapshot.OfferedExternalIds.Contains(externalId))
            .ToArray();

        foreach (var batch in missingExternalIds.Chunk(settings.BatchSize))
        {
            foreach (var product in await catalog.GetByExternalIdsAsync(batch, cancellationToken))
            {
                if (product.Deactivate(now))
                {
                    deactivated++;
                }
            }

            await catalog.SaveBatchAsync(cancellationToken);
        }

        // 4. Fencing: if the lock was lost meanwhile (connection cut), another instance may be synchronizing.
        // Checked inside the transaction, on the session that holds the lock, right before committing.
        if (!await synchronizationLock.IsStillHeldAsync(cancellationToken))
        {
            return CatalogSyncErrors.LockLost;
        }

        await transaction.CommitAsync(cancellationToken);

        return new CatalogSyncCounts(
            snapshot.ReceivedCount,
            added,
            updated,
            reactivated,
            deactivated,
            unchanged,
            snapshot.SkippedProducts.Count);
    }

    private async Task FailAsync(CatalogSyncRun run, string reason)
    {
        run.Fail(reason, timeProvider.GetUtcNow());

        // Not canceled with the request: the failure must be recorded even when the caller has given up.
        await runs.UpdateAsync(run, CancellationToken.None);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalog synchronization ({Trigger}) skipped: another one is running.")]
    private static partial void LogAlreadyRunning(ILogger logger, CatalogSyncTrigger trigger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalog synchronization {RunId} started ({Trigger}).")]
    private static partial void LogStarted(ILogger logger, Guid runId, CatalogSyncTrigger trigger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Catalog synchronization {RunId} completed: {ProviderProducts} received, {Added} added, {Updated} updated, "
            + "{Reactivated} reactivated, {Deactivated} deactivated, {Unchanged} unchanged, {Skipped} skipped.")]
    private static partial void LogCompleted(
        ILogger logger,
        Guid runId,
        int providerProducts,
        int added,
        int updated,
        int reactivated,
        int deactivated,
        int unchanged,
        int skipped);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Provider product {ExternalId} skipped: {Reason}")]
    private static partial void LogProductSkipped(ILogger logger, int externalId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Catalog synchronization {RunId} failed: the provider is unavailable. The local catalog is unchanged.")]
    private static partial void LogProviderUnavailable(ILogger logger, Exception exception, Guid runId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Catalog synchronization {RunId} refused ({Code}): {Description}")]
    private static partial void LogRejected(ILogger logger, Guid runId, string code, string description);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Catalog synchronization {RunId} canceled. The local catalog is unchanged.")]
    private static partial void LogCanceled(ILogger logger, Guid runId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Catalog synchronization {RunId} failed unexpectedly. The local catalog is unchanged.")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid runId);
}
