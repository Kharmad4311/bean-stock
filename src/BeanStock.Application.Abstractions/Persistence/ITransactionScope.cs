namespace BeanStock.Application.Abstractions.Persistence;

/// <summary>
/// An open database transaction. Disposing it without <see cref="CommitAsync"/> rolls everything back.
/// </summary>
public interface ITransactionScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}
