using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BeanStock.Infrastructure.Persistence;

/// <summary>
/// Creates the <see cref="BeanStockDbContext"/> for the EF Core design-time tools
/// (<c>dotnet ef migrations add</c>, <c>dotnet ef migrations bundle</c>) without building an application host.
/// </summary>
/// <remarks>
/// <para>
/// Without this factory, the tools build the API host, whose persistence registration requires the runtime
/// connection string: available through user secrets on a workstation, absent from a Docker build.
/// Design-time tooling must not depend on the configuration of a host.
/// </para>
/// <para>
/// Generating a migration or a bundle never opens a connection. When the <c>ConnectionStrings__BeanStock</c>
/// environment variable is absent, a placeholder is used; commands that do reach the database take
/// <c>--connection</c> (the migrations bundle, <c>dotnet ef database update</c>).
/// </para>
/// </remarks>
[ExcludeFromCodeCoverage(Justification = "Design-time tooling only, exercised by dotnet ef and the migrator image build.")]
public sealed class BeanStockDesignTimeDbContextFactory : IDesignTimeDbContextFactory<BeanStockDbContext>
{
    private const string ConnectionStringVariable = "ConnectionStrings__" + DependencyInjection.ConnectionStringName;

    // Never opened: only used to select the SQL Server provider while generating migrations or a bundle.
    private const string PlaceholderConnectionString = "Server=design-time-placeholder;Database=BeanStock";

    /// <inheritdoc />
    public BeanStockDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = PlaceholderConnectionString;
        }

        var options = new DbContextOptionsBuilder<BeanStockDbContext>();
        DependencyInjection.ConfigureSqlServer(options, connectionString);

        return new BeanStockDbContext(options.Options);
    }
}
