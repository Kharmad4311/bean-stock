using BeanStock.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace BeanStock.Infrastructure.Persistence;

/// <summary>
/// Transactions on the scoped <see cref="BeanStockDbContext"/>: every SaveChanges of the scope joins it.
/// </summary>
internal sealed class EfUnitOfWork(BeanStockDbContext dbContext) : IUnitOfWork
{
    public async Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new EfTransactionScope(await dbContext.Database.BeginTransactionAsync(cancellationToken));

    private sealed class EfTransactionScope(IDbContextTransaction transaction) : ITransactionScope
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        // Disposing an uncommitted EF Core transaction rolls it back.
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
