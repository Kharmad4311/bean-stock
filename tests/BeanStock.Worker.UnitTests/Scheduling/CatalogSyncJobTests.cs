using BeanStock.Application.Catalog.Synchronization;
using BeanStock.Domain.Catalog.Synchronization;
using BeanStock.Worker.Scheduling;
using ErrorOr;
using Microsoft.Extensions.Logging.Abstractions;

namespace BeanStock.Worker.UnitTests.Scheduling;

public sealed class CatalogSyncJobTests
{
    private static readonly TimeSpan _interval = TimeSpan.FromDays(1);
    private static readonly DateTimeOffset _now = new(2026, 11, 14, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Completed_synchronization_is_a_successful_check()
    {
        var report = new CatalogSyncReport(Guid.NewGuid(), CatalogSyncTrigger.Scheduled, _now, _now, 1, 1, 0, 0, 0, 0, 0);

        Handle(CatalogSyncResult.Completed(report)).ShouldBeTrue();
    }

    [Fact]
    public void Synchronization_not_due_is_a_successful_check()
    {
        Handle(CatalogSyncResult.NotDue(_now.AddHours(-1))).ShouldBeTrue();
    }

    [Fact]
    public void Synchronization_running_on_another_instance_is_not_a_failure()
    {
        Handle(CatalogSyncErrors.AlreadyRunning).ShouldBeTrue();
    }

    [Fact]
    public void Failed_synchronization_asks_for_a_retry()
    {
        Handle(CatalogSyncErrors.ProviderUnavailable).ShouldBeFalse();
    }

    private static bool Handle(ErrorOr<CatalogSyncResult> result) =>
        CatalogSyncJob.HandleResult(result, _interval, NullLogger.Instance);
}
