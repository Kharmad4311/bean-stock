using BeanStock.Application.Abstractions.Catalog;
using BeanStock.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.MsSql;

namespace BeanStock.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the API in memory against a disposable SQL Server container, with migrations applied.
/// </summary>
/// <remarks>
/// The catalog provider, the clock and the database writes are under the control of the tests:
/// DummyJSON is never called, time only moves when a test advances it, and a write failure can be injected.
/// </remarks>
public sealed class BeanStockApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly DateTimeOffset InitialTime = new(2026, 11, 14, 3, 0, 0, TimeSpan.Zero);

    private readonly MsSqlContainer _sqlServer =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    /// <summary>Replaces DummyJSON.</summary>
    public FakeCatalogProvider CatalogProvider { get; } = new();

    public FakeTimeProvider Time { get; } = new(InitialTime);

    public FailingSaveChangesInterceptor SaveChangesInterceptor { get; } = new();

    public async ValueTask InitializeAsync() => await _sqlServer.StartAsync();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _sqlServer.DisposeAsync();
    }

    /// <summary>
    /// Empties the database and resets the test doubles, for tests sharing this factory.
    /// The clock is not reset (a fake clock cannot go back): tests reason relative to its current time.
    /// </summary>
    public async Task ResetAsync()
    {
        CatalogProvider.Reset();
        SaveChangesInterceptor.Reset();

        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BeanStockDbContext>();
        await dbContext.CartLines.ExecuteDeleteAsync();
        await dbContext.Carts.ExecuteDeleteAsync();
        await dbContext.Products.ExecuteDeleteAsync();
        await dbContext.CatalogSyncRuns.ExecuteDeleteAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // The container's connection string targets 'master': the application gets its own database,
        // created by the migrations, as in a real deployment.
        var connectionString = new SqlConnectionStringBuilder(_sqlServer.GetConnectionString())
        {
            InitialCatalog = "BeanStockTests",
        }.ConnectionString;
        builder.UseSetting("ConnectionStrings:BeanStock", connectionString);
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");

        // Small batches: every synchronization test goes through several batches.
        builder.UseSetting("CatalogSync:BatchSize", "2");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICatalogProvider>();
            services.AddSingleton<ICatalogProvider>(CatalogProvider);

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);

            services.ConfigureDbContext<BeanStockDbContext>(options => options.AddInterceptors(SaveChangesInterceptor));
        });
    }
}
