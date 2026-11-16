using BeanStock.Application.Abstractions.Carts;
using BeanStock.Application.Abstractions.Catalog;
using BeanStock.Application.Abstractions.Concurrency;
using BeanStock.Application.Abstractions.Persistence;
using BeanStock.Infrastructure.Persistence.Concurrency;
using BeanStock.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BeanStock.Infrastructure.Persistence;

public static class DependencyInjection
{
    public const string ConnectionStringName = "BeanStock";
    public const string HealthCheckTag = "ready";

    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is missing. " +
                "Set it with user secrets or the ConnectionStrings__BeanStock environment variable (see README).");
        }

        services.AddDbContext<BeanStockDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped(typeof(IReadRepository<>), typeof(EfReadRepository<>));

        // Scoped: the lock, the transaction and the catalog writes share the DbContext (and connection) of the scope.
        services.AddScoped<IDistributedLock, SqlServerDistributedLock>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<ICatalogSyncStore, EfCatalogSyncStore>();
        services.AddScoped<ICatalogSyncRunStore, EfCatalogSyncRunStore>();
        services.AddScoped<ICartRepository, EfCartRepository>();

        services.AddHealthChecks()
            .AddDbContextCheck<BeanStockDbContext>(name: "sqlserver", tags: [HealthCheckTag]);

        return services;
    }
}
