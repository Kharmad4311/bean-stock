namespace BeanStock.Application.Abstractions.Persistence;

/// <summary>
/// Explicit database transactions, for use cases that must be all-or-nothing.
/// </summary>
public interface IUnitOfWork
{
    Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken);
}
