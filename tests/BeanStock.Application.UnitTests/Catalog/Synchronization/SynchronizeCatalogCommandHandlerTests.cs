using BeanStock.Application.Abstractions.Catalog;
using BeanStock.Application.Catalog.Synchronization;
using BeanStock.Application.UnitTests.Catalog.Synchronization.Fakes;
using BeanStock.Domain.Catalog;
using BeanStock.Domain.Catalog.Synchronization;
using ErrorOr;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BeanStock.Application.UnitTests.Catalog.Synchronization;

public sealed class SynchronizeCatalogCommandHandlerTests
{
    private static readonly DateTimeOffset _start = new(2026, 11, 14, 3, 0, 0, TimeSpan.Zero);

    private readonly FakeDistributedLock _lock = new();
    private readonly FakeCatalogProvider _provider = new();
    private readonly InMemoryCatalogSyncStore _catalog = new();
    private readonly FakeCatalogSyncRunStore _runs = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeTimeProvider _time = new(_start);
    private readonly CatalogSynchronizationOptions _options = new() { BatchSize = 2, MinimumCatalogRatio = 0.5 };

    [Fact]
    public async Task First_synchronization_adds_every_product_and_commits()
    {
        _provider.Products = [Provided(1), Provided(2), Provided(3)];

        var result = await HandleAsync(SynchronizeCatalogCommand.Manual());

        var report = CompletedReport(result);
        report.ProviderProducts.ShouldBe(3);
        report.Added.ShouldBe(3);
        _catalog.Products.Count.ShouldBe(3);
        _catalog.Products.ShouldAllBe(product => product.IsActive);
        _unitOfWork.Committed.ShouldBeTrue();
        _runs.Runs.ShouldHaveSingleItem().Status.ShouldBe(CatalogSyncStatus.Succeeded);
    }

    [Fact]
    public async Task Products_are_written_in_batches_of_the_configured_size()
    {
        _provider.Products = [Provided(1), Provided(2), Provided(3), Provided(4), Provided(5)];

        await HandleAsync(SynchronizeCatalogCommand.Manual());

        // Batch size 2: three batches for five products, no deactivation batch.
        _catalog.SaveCount.ShouldBe(3);
        _catalog.Products.Count.ShouldBe(5);
    }

    [Fact]
    public async Task Existing_products_are_updated_left_unchanged_or_reactivated()
    {
        var unchanged = Existing(1, "Same", 10m);
        var changed = Existing(2, "Old name", 10m);
        var inactive = Existing(3, "Back again", 10m);
        inactive.Deactivate(_start.AddDays(-1));
        _catalog.Seed(unchanged, changed, inactive);
        _provider.Products = [Provided(1, "Same", 10m), Provided(2, "New name", 12m), Provided(3, "Back again", 10m)];

        var report = CompletedReport(await HandleAsync(SynchronizeCatalogCommand.Manual()));

        report.Unchanged.ShouldBe(1);
        report.Updated.ShouldBe(1);
        report.Reactivated.ShouldBe(1);
        report.Added.ShouldBe(0);
        changed.Name.ShouldBe("New name");
        changed.Price.ShouldBe(12m);
        inactive.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Active_products_no_longer_offered_are_deactivated_not_removed()
    {
        _catalog.Seed(Existing(1), Existing(2), Existing(3));
        _provider.Products = [Provided(1), Provided(2)];

        var report = CompletedReport(await HandleAsync(SynchronizeCatalogCommand.Manual()));

        report.Deactivated.ShouldBe(1);
        _catalog.Products.Count.ShouldBe(3);
        _catalog.Get(3).IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Invalid_products_are_skipped_but_not_deactivated()
    {
        _catalog.Seed(Existing(1, "Kept name"), Existing(2));
        _provider.Products = [Provided(1, name: " "), Provided(2), Provided(3)];

        var report = CompletedReport(await HandleAsync(SynchronizeCatalogCommand.Manual()));

        report.Skipped.ShouldBe(1);
        report.Deactivated.ShouldBe(0);
        report.Added.ShouldBe(1);
        _catalog.Get(1).IsActive.ShouldBeTrue();
        _catalog.Get(1).Name.ShouldBe("Kept name");
    }

    [Fact]
    public async Task Another_synchronization_in_progress_returns_a_conflict_without_reading_the_provider()
    {
        _lock.IsHeld = true;
        _provider.Products = [Provided(1)];

        var result = await HandleAsync(SynchronizeCatalogCommand.Manual());

        result.FirstError.ShouldBe(CatalogSyncErrors.AlreadyRunning);
        result.FirstError.Type.ShouldBe(ErrorType.Conflict);
        _provider.CallCount.ShouldBe(0);
        _runs.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Provider_failure_writes_nothing_and_records_a_failed_run()
    {
        _catalog.Seed(Existing(1));
        _provider.Failure = new CatalogProviderException("DummyJSON answered 503.");

        var result = await HandleAsync(SynchronizeCatalogCommand.Manual());

        result.FirstError.Code.ShouldBe(CatalogSyncErrors.ProviderUnavailable.Code);
        _unitOfWork.BeginCount.ShouldBe(0);
        _catalog.Get(1).IsActive.ShouldBeTrue();
        var run = _runs.Runs.ShouldHaveSingleItem();
        run.Status.ShouldBe(CatalogSyncStatus.Failed);
        run.FailureReason.ShouldNotBeNull().ShouldContain("503");
    }

    [Fact]
    public async Task Empty_provider_catalog_is_refused_before_any_write()
    {
        _catalog.Seed(Existing(1), Existing(2));
        _provider.Products = [];

        var result = await HandleAsync(SynchronizeCatalogCommand.Manual());

        result.FirstError.Code.ShouldBe("CatalogSync.SuspiciousCatalog");
        _unitOfWork.BeginCount.ShouldBe(0);
        _catalog.Products.ShouldAllBe(product => product.IsActive);
        _runs.Runs.ShouldHaveSingleItem().Status.ShouldBe(CatalogSyncStatus.Failed);
    }

    [Fact]
    public async Task Provider_catalog_below_the_minimum_ratio_is_refused()
    {
        _catalog.Seed(Existing(1), Existing(2), Existing(3), Existing(4), Existing(5));
        _provider.Products = [Provided(1), Provided(2)];

        var result = await HandleAsync(SynchronizeCatalogCommand.Manual());

        result.FirstError.Code.ShouldBe("CatalogSync.SuspiciousCatalog");
        _catalog.Products.ShouldAllBe(product => product.IsActive);
    }

    [Fact]
    public async Task Unexpected_failure_while_writing_is_rethrown_without_commit_and_recorded()
    {
        _provider.Products = [Provided(1)];
        _catalog.SaveFailure = new InvalidOperationException("Database unreachable.");

        await Should.ThrowAsync<InvalidOperationException>(() => HandleAsync(SynchronizeCatalogCommand.Manual()).AsTask());

        _unitOfWork.Committed.ShouldBeFalse();
        _lock.IsHeld.ShouldBeFalse();
        var run = _runs.Runs.ShouldHaveSingleItem();
        run.Status.ShouldBe(CatalogSyncStatus.Failed);
        run.FailureReason.ShouldNotBeNull().ShouldContain("Database unreachable.");
    }

    [Fact]
    public async Task Lock_lost_during_the_run_prevents_the_commit()
    {
        _provider.Products = [Provided(1)];
        _lock.LoseLockDuringRun = true;

        var result = await HandleAsync(SynchronizeCatalogCommand.Manual());

        result.FirstError.ShouldBe(CatalogSyncErrors.LockLost);
        result.FirstError.Type.ShouldBe(ErrorType.Conflict);
        _unitOfWork.Committed.ShouldBeFalse();
        _runs.Runs.ShouldHaveSingleItem().Status.ShouldBe(CatalogSyncStatus.Failed);
    }

    [Fact]
    public async Task Lock_is_released_after_a_successful_synchronization()
    {
        _provider.Products = [Provided(1)];

        await HandleAsync(SynchronizeCatalogCommand.Manual());

        _lock.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task Scheduled_synchronization_is_skipped_when_the_last_success_is_recent()
    {
        _runs.LastSuccessfulCompletion = _start.AddHours(-23);
        _provider.Products = [Provided(1)];

        var result = await HandleAsync(SynchronizeCatalogCommand.Scheduled(TimeSpan.FromDays(1)));

        result.Value.Outcome.ShouldBe(CatalogSyncOutcome.NotDue);
        result.Value.LastSucceededAt.ShouldBe(_start.AddHours(-23));
        _provider.CallCount.ShouldBe(0);
        _runs.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Scheduled_synchronization_runs_once_the_interval_has_elapsed()
    {
        _runs.LastSuccessfulCompletion = _start.AddDays(-1);
        _provider.Products = [Provided(1)];

        var result = await HandleAsync(SynchronizeCatalogCommand.Scheduled(TimeSpan.FromDays(1)));

        result.Value.Outcome.ShouldBe(CatalogSyncOutcome.Completed);
        result.Value.Report.ShouldNotBeNull().Trigger.ShouldBe(CatalogSyncTrigger.Scheduled);
    }

    [Fact]
    public async Task Scheduled_synchronization_runs_when_no_synchronization_ever_succeeded()
    {
        _provider.Products = [Provided(1)];

        var result = await HandleAsync(SynchronizeCatalogCommand.Scheduled(TimeSpan.FromDays(1)));

        result.Value.Outcome.ShouldBe(CatalogSyncOutcome.Completed);
    }

    [Fact]
    public async Task Manual_synchronization_ignores_a_recent_success()
    {
        _runs.LastSuccessfulCompletion = _start.AddMinutes(-1);
        _provider.Products = [Provided(1)];

        var result = await HandleAsync(SynchronizeCatalogCommand.Manual());

        result.Value.Outcome.ShouldBe(CatalogSyncOutcome.Completed);
    }

    private ValueTask<ErrorOr<CatalogSyncResult>> HandleAsync(SynchronizeCatalogCommand command)
    {
        var handler = new SynchronizeCatalogCommandHandler(
            _lock,
            _provider,
            _catalog,
            _runs,
            _unitOfWork,
            new ProviderProductValidator(),
            Options.Create(_options),
            _time,
            NullLogger<SynchronizeCatalogCommandHandler>.Instance);

        return handler.Handle(command, TestContext.Current.CancellationToken);
    }

    private static CatalogSyncReport CompletedReport(ErrorOr<CatalogSyncResult> result)
    {
        result.IsError.ShouldBeFalse();
        result.Value.Outcome.ShouldBe(CatalogSyncOutcome.Completed);
        return result.Value.Report.ShouldNotBeNull();
    }

    private static ProviderProduct Provided(int externalId, string name = "Product", decimal price = 10m) =>
        new(externalId, name, "groceries", price, Stock: 5, "In Stock", UpdatedAt: null);

    private static Product Existing(int externalId, string name = "Product", decimal price = 10m) =>
        Product.Create(externalId, name, "groceries", price, 5, "In Stock", providerUpdatedAt: null, _start.AddDays(-1));
}
