using BeanStock.Domain.Catalog.Synchronization;
using BeanStock.Domain.UnitTests.TestData;

namespace BeanStock.Domain.UnitTests.Catalog;

public sealed class CatalogSyncRunTests
{
    private static readonly DateTimeOffset _completedAt = TestProducts.Now.AddMinutes(1);

    [Fact]
    public void Start_creates_a_running_run()
    {
        var run = CatalogSyncRun.Start(CatalogSyncTrigger.Manual, TestProducts.Now);

        run.Status.ShouldBe(CatalogSyncStatus.Running);
        run.Trigger.ShouldBe(CatalogSyncTrigger.Manual);
        run.StartedAt.ShouldBe(TestProducts.Now);
        run.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public void Succeed_records_the_counts_and_the_completion_date()
    {
        var run = CatalogSyncRun.Start(CatalogSyncTrigger.Scheduled, TestProducts.Now);

        run.Succeed(new CatalogSyncCounts(10, 2, 3, 1, 1, 3, 1), _completedAt);

        run.Status.ShouldBe(CatalogSyncStatus.Succeeded);
        run.CompletedAt.ShouldBe(_completedAt);
        run.ProviderProductCount.ShouldBe(10);
        run.AddedCount.ShouldBe(2);
        run.UpdatedCount.ShouldBe(3);
        run.ReactivatedCount.ShouldBe(1);
        run.DeactivatedCount.ShouldBe(1);
        run.UnchangedCount.ShouldBe(3);
        run.SkippedCount.ShouldBe(1);
    }

    [Fact]
    public void Fail_keeps_a_reason_truncated_to_the_stored_length()
    {
        var run = CatalogSyncRun.Start(CatalogSyncTrigger.Scheduled, TestProducts.Now);

        run.Fail(new string('x', CatalogSyncRun.FailureReasonMaxLength + 10), _completedAt);

        run.Status.ShouldBe(CatalogSyncStatus.Failed);
        run.CompletedAt.ShouldBe(_completedAt);
        run.FailureReason.ShouldNotBeNull().Length.ShouldBe(CatalogSyncRun.FailureReasonMaxLength);
    }

    [Fact]
    public void A_completed_run_cannot_be_completed_again()
    {
        var run = CatalogSyncRun.Start(CatalogSyncTrigger.Scheduled, TestProducts.Now);
        run.Fail("Provider unavailable", _completedAt);

        Should.Throw<InvalidOperationException>(() => run.Succeed(new CatalogSyncCounts(0, 0, 0, 0, 0, 0, 0), _completedAt));
    }
}
