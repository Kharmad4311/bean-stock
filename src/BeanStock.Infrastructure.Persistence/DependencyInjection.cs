using BeanStock.Application.Abstractions.Persistence;
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

        services.AddHealthChecks()
            .AddDbContextCheck<BeanStockDbContext>(name: "sqlserver", tags: [HealthCheckTag]);

        return services;
    }
}
