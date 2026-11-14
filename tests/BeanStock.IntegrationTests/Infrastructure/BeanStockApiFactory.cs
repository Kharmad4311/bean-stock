using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.MsSql;

namespace BeanStock.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the API in memory against a disposable SQL Server container, with migrations applied.
/// </summary>
public sealed class BeanStockApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public async ValueTask InitializeAsync() => await _sqlServer.StartAsync();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _sqlServer.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:BeanStock", _sqlServer.GetConnectionString());
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
    }
}
