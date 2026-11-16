using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BeanStock.Application.Abstractions.Catalog;
using BeanStock.Application.Abstractions.Concurrency;
using BeanStock.Application.Catalog.ListProducts;
using BeanStock.Application.Catalog.Synchronization;
using BeanStock.Application.Common.Paging;
using BeanStock.Domain.Catalog;
using BeanStock.Domain.Catalog.Synchronization;
using BeanStock.Infrastructure.Persistence;
using BeanStock.IntegrationTests.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BeanStock.IntegrationTests.Catalog;

/// <summary>
/// Catalog synchronization on a real SQL Server: no duplicates, all-or-nothing, exclusive between instances.
/// </summary>
public sealed class CatalogSynchronizationTests(BeanStockApiFactory factory)
    : IClassFixture<BeanStockApiFactory>, IAsyncLifetime
{
    private const string SyncUrl = "/admin/catalog/sync";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Manual_synchronization_adds_every_product_and_makes_them_listable()
    {
        factory.CatalogProvider.Products = FakeCatalogProvider.Catalog(5);
        using var client = factory.CreateClient();

        var report = await SynchronizeAsync(client);

        report.Trigger.ShouldBe(CatalogSyncTrigger.Manual);
        report.ProviderProducts.ShouldBe(5);
        report.Added.ShouldBe(5);
        var page = await client.GetFromJsonAsync<PagedResult<ProductResponse>>("/products?pageSize=100", CancellationToken);
        page.ShouldNotBeNull().TotalCount.ShouldBe(5);
    }

    [Fact]
    public async Task Repeated_synchronization_creates_no_duplicate_and_leaves_unchanged_products_untouched()
    {
        factory.CatalogProvider.Products = FakeCatalogProvider.Catalog(5);
        using var client = factory.CreateClient();
        await SynchronizeAsync(client);
        var firstUpdate = (await ProductAsync(1)).LastUpdatedAt;
        factory.Time.Advance(TimeSpan.FromHours(1));

        var report = await SynchronizeAsync(client);

        report.Added.ShouldBe(0);
        report.Updated.ShouldBe(0);
        report.Unchanged.ShouldBe(5);
        (await ProductCountAsync()).ShouldBe(5);
        (await ProductAsync(1)).LastUpdatedAt.ShouldBe(firstUpdate);
    }

    [Fact]
    public async Task Changed_products_are_updated()
    {
        factory.CatalogProvider.Products = [FakeCatalogProvider.Product(1, "Arabica", 10m), FakeCatalogProvider.Product(2)];
        using var client = factory.CreateClient();
        await SynchronizeAsync(client);

        factory.CatalogProvider.Products = [FakeCatalogProvider.Product(1, "Arabica 1kg", 14.90m), FakeCatalogProvider.Product(2)];
        var report = await SynchronizeAsync(client);

        report.Updated.ShouldBe(1);
        report.Unchanged.ShouldBe(1);
        var product = await ProductAsync(1);
        product.Name.ShouldBe("Arabica 1kg");
        product.Price.ShouldBe(14.90m);
    }

    [Fact]
    public async Task Product_no_longer_offered_is_deactivated_kept_and_reactivated_when_offered_again()
    {
        factory.CatalogProvider.Products = FakeCatalogProvider.Catalog(3);
        using var client = factory.CreateClient();
        await SynchronizeAsync(client);

        factory.CatalogProvider.Products = FakeCatalogProvider.Catalog(2);
        var deactivation = await SynchronizeAsync(client);

        deactivation.Deactivated.ShouldBe(1);
        (await ProductAsync(3)).IsActive.ShouldBeFalse();
        (await ProductCountAsync()).ShouldBe(3);
        var page = await client.GetFromJsonAsync<PagedResult<ProductResponse>>("/products", CancellationToken);
        page.ShouldNotBeNull().TotalCount.ShouldBe(2);

        factory.CatalogProvider.Products = FakeCatalogProvider.Catalog(3);
        var reactivation = await SynchronizeAsync(client);

        reactivation.Reactivated.ShouldBe(1);
        (await ProductAsync(3)).IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Provider_failure_returns_502_and_keeps_the_catalog_available()
    {
        factory.CatalogProvider.Products = FakeCatalogProvider.Catalog(3);
        using var client = factory.CreateClient();
        await SynchronizeAsync(client);

        factory.CatalogProvider.Failure = new CatalogProviderException("DummyJSON answered 503.");
        var response = await client.PostAsync(SyncUrl, content: null, CancellationToken);

        await ShouldBeProblemAsync(response, HttpStatusCode.BadGateway, "CatalogSync.ProviderUnavailable");
        var page = await client.GetFromJsonAsync<PagedResult<ProductResponse>>("/products", CancellationToken);
        page.ShouldNotBeNull().TotalCount.ShouldBe(3);
        var failedRun = await FailedRunAsync();
        failedRun.FailureReason.ShouldNotBeNull().ShouldContain("503");
    }

    [Fact]
    public async Task Empty_provider_catalog_returns_502_and_deactivates_nothing()
    {
        factory.CatalogProvider.Products = FakeCatalogProvider.Catalog(3);
        using var client = factory.CreateClient();
        await SynchronizeAsync(client);

        factory.CatalogProvider.Products = [];
        var response = await client.PostAsync(SyncUrl, content: null, CancellationToken);

        await ShouldBeProblemAsync(response, HttpStatusCode.BadGateway, "CatalogSync.SuspiciousCatalog");
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BeanStockDbContext>();
        (await dbContext.Products.CountAsync(product => product.IsActive, CancellationToken)).ShouldBe(3);
    }

    [Fact]
    public async Task Failure_in_the_middle_of_the_writes_rolls_everything_back_and_the_next_run_recovers()
    {
        factory.CatalogProvider.Products = FakeCatalogProvider.Catalog(3, "Original");
        using var client = factory.CreateClient();
        await SynchronizeAsync(client);

        // Five products in batches of two: the first batch is written, the second one fails.
        factory.CatalogProvider.Products = FakeCatalogProvider.Catalog(5, "Renamed");
        factory.SaveChangesInterceptor.FailAfterProductSaves(1);
        var failed = await client.PostAsync(SyncUrl, content: null, CancellationToken);

        failed.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await ProductCountAsync()).ShouldBe(3);
        (await ProductAsync(1)).Name.ShouldBe("Original 1");
        (await FailedRunAsync()).FailureReason.ShouldNotBeNull().ShouldContain("Simulated database failure");

        factory.SaveChangesInterceptor.Reset();
        var report = await SynchronizeAsync(client);

        report.Added.ShouldBe(2);
        report.Updated.ShouldBe(3);
        (await ProductAsync(1)).Name.ShouldBe("Renamed 1");
    }

    [Fact]
    public async Task Concurrent_synchronization_is_rejected_while_one_is_running()
    {
        factory.CatalogProvider.Products = FakeCatalogProvider.Catalog(3);
        using var client = factory.CreateClient();
        var gate = factory.CatalogProvider.BlockNextFetch();

        var first = client.PostAsync(SyncUrl, content: null, CancellationToken);
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken);
        var second = await client.PostAsync(SyncUrl, content: null, CancellationToken);
        gate.Release();
        var firstResponse = await first;

        await ShouldBeProblemAsync(second, HttpStatusCode.Conflict, "CatalogSync.AlreadyRunning");
        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ProductCountAsync()).ShouldBe(3);
        (await RunsAsync()).ShouldHaveSingleItem().Status.ShouldBe(CatalogSyncStatus.Succeeded);
    }

    [Fact]
    public async Task Distributed_lock_is_exclusive_across_connections_and_released_on_dispose()
    {
        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        var firstLock = firstScope.ServiceProvider.GetRequiredService<IDistributedLock>();
        var secondLock = secondScope.ServiceProvider.GetRequiredService<IDistributedLock>();

        var held = await firstLock.TryAcquireAsync("tests.lock", CancellationToken);
        held.ShouldNotBeNull();
        (await secondLock.TryAcquireAsync("tests.lock", CancellationToken)).ShouldBeNull();

        await held.DisposeAsync();

        var reacquired = await secondLock.TryAcquireAsync("tests.lock", CancellationToken);
        reacquired.ShouldNotBeNull();
        await reacquired.DisposeAsync();
    }

    [Fact]
    public async Task Lost_lock_is_detected_when_its_session_is_killed()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var distributedLock = scope.ServiceProvider.GetRequiredService<IDistributedLock>();
        await using var handle = await distributedLock.TryAcquireAsync("tests.fencing", CancellationToken);
        handle.ShouldNotBeNull();
        (await handle.IsStillHeldAsync(CancellationToken)).ShouldBeTrue();

        // Simulates a connection cut: another session kills the one that owns the lock.
        await using var adminScope = factory.Services.CreateAsyncScope();
        var admin = adminScope.ServiceProvider.GetRequiredService<BeanStockDbContext>();
        var ownerSessionId = await admin.Database
            .SqlQuery<int>($"SELECT request_session_id AS Value FROM sys.dm_tran_locks WHERE resource_type = 'APPLICATION' AND request_mode = 'X'")
            .SingleAsync(CancellationToken);
        var killSession = "KILL " + ownerSessionId.ToString(CultureInfo.InvariantCulture); // KILL takes no parameter
        await admin.Database.ExecuteSqlRawAsync(killSession, CancellationToken);

        (await handle.IsStillHeldAsync(CancellationToken)).ShouldBeFalse();

        // The lock is free again for the other instances.
        await using var otherScope = factory.Services.CreateAsyncScope();
        await using var taken = await otherScope.ServiceProvider.GetRequiredService<IDistributedLock>()
            .TryAcquireAsync("tests.fencing", CancellationToken);
        taken.ShouldNotBeNull();
    }

    [Fact]
    public async Task Scheduled_synchronization_runs_only_once_per_interval()
    {
        factory.CatalogProvider.Products = FakeCatalogProvider.Catalog(2);
        var interval = TimeSpan.FromDays(1);

        var first = await SendAsync(SynchronizeCatalogCommand.Scheduled(interval));
        factory.Time.Advance(TimeSpan.FromHours(23));
        var tooEarly = await SendAsync(SynchronizeCatalogCommand.Scheduled(interval));
        factory.Time.Advance(TimeSpan.FromHours(1));
        var due = await SendAsync(SynchronizeCatalogCommand.Scheduled(interval));

        first.Outcome.ShouldBe(CatalogSyncOutcome.Completed);
        tooEarly.Outcome.ShouldBe(CatalogSyncOutcome.NotDue);
        due.Outcome.ShouldBe(CatalogSyncOutcome.Completed);
        (await RunsAsync()).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Catalog_reads_are_not_blocked_by_an_open_write_transaction()
    {
        factory.CatalogProvider.Products = [FakeCatalogProvider.Product(1, "Committed name")];
        using var client = factory.CreateClient();
        await SynchronizeAsync(client);

        // A write transaction left open on the product, as during a long synchronization.
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BeanStockDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(CancellationToken);
        await dbContext.Products
            .Where(product => product.ExternalId == 1)
            .ExecuteUpdateAsync(setters => setters.SetProperty(product => product.Name, "Uncommitted name"), CancellationToken);

        // READ_COMMITTED_SNAPSHOT: the reader gets the last committed version without waiting for the writer.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var page = await client.GetFromJsonAsync<PagedResult<ProductResponse>>("/products", timeout.Token);

        page.ShouldNotBeNull().Items.ShouldHaveSingleItem().Name.ShouldBe("Committed name");
        await transaction.RollbackAsync(CancellationToken);
    }

    private static async Task<CatalogSyncReport> SynchronizeAsync(HttpClient client)
    {
        var response = await client.PostAsync(SyncUrl, content: null, CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));
        return (await response.Content.ReadFromJsonAsync<CatalogSyncReport>(_json, CancellationToken)).ShouldNotBeNull();
    }

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        problem.RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    private async Task<CatalogSyncResult> SendAsync(SynchronizeCatalogCommand command)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, CancellationToken);
        result.IsError.ShouldBeFalse();
        return result.Value;
    }

    private async Task<Product> ProductAsync(int externalId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BeanStockDbContext>();
        return await dbContext.Products.AsNoTracking().SingleAsync(product => product.ExternalId == externalId, CancellationToken);
    }

    private async Task<int> ProductCountAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BeanStockDbContext>().Products.CountAsync(CancellationToken);
    }

    private async Task<List<CatalogSyncRun>> RunsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BeanStockDbContext>().CatalogSyncRuns
            .AsNoTracking()
            .ToListAsync(CancellationToken);
    }

    private async Task<CatalogSyncRun> FailedRunAsync() =>
        (await RunsAsync()).Where(run => run.Status == CatalogSyncStatus.Failed).ShouldHaveSingleItem();
}
