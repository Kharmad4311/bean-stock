using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BeanStock.Infrastructure.Persistence;

public static class DatabaseMigrator
{
    /// <summary>
    /// Applies pending EF Core migrations.
    /// </summary>
    /// <remarks>
    /// Meant for local development. EF Core takes a database lock while migrating, so several
    /// instances starting together do not apply the same migration twice.
    /// </remarks>
    public static async Task ApplyMigrationsAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BeanStockDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
