using BeanStock.Domain.Catalog.Synchronization;
using ErrorOr;
using Mediator;

namespace BeanStock.Application.Catalog.Synchronization;

/// <summary>
/// Synchronizes the local catalog with the provider. Single use case shared by every trigger:
/// the worker schedule and the manual API endpoint send this same command.
/// </summary>
/// <param name="Trigger">What started the synchronization.</param>
/// <param name="MinimumInterval">
/// Scheduled synchronizations only: the synchronization is skipped (<see cref="CatalogSyncOutcome.NotDue"/>)
/// when the last successful one completed less than this interval ago. Checked under the distributed lock,
/// so several worker instances never synchronize twice for the same period.
/// </param>
public sealed record SynchronizeCatalogCommand(CatalogSyncTrigger Trigger, TimeSpan? MinimumInterval)
    : ICommand<ErrorOr<CatalogSyncResult>>
{
    /// <summary>On-demand synchronization: always runs (unless another one is in progress).</summary>
    public static SynchronizeCatalogCommand Manual() => new(CatalogSyncTrigger.Manual, MinimumInterval: null);

    /// <summary>Synchronization of the schedule: runs only if the last success is older than <paramref name="interval"/>.</summary>
    public static SynchronizeCatalogCommand Scheduled(TimeSpan interval) => new(CatalogSyncTrigger.Scheduled, interval);
}
