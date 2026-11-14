using BeanStock.Application.Abstractions.Catalog;
using BeanStock.Application.Abstractions.Concurrency;
using BeanStock.Application.Abstractions.Persistence;
using BeanStock.Domain.Catalog;
using BeanStock.Domain.Catalog.Synchronization;

namespace BeanStock.Application.UnitTests.Catalog.Synchronization.Fakes;

/// <summary>In-process lock: held until the returned handle is disposed, or until the test simulates its loss.</summary>
internal sealed class FakeDistributedLock : IDistributedLock
{
    public bool IsHeld { get; set; }

    /// <summary>Simulates a lock lost during the run (connection cut): the handle reports it as no longer held.</summary>
    public bool LoseLockDuringRun { get; set; }

    public Task<IDistributedLockHandle?> TryAcquireAsync(string resource, CancellationToken cancellationToken)
    {
        if (IsHeld)
        {
            return Task.FromResult<IDistributedLockHandle?>(null);
        }

        IsHeld = true;
        return Task.FromResult<IDistributedLockHandle?>(new Handle(this));
    }

    private sealed class Handle(FakeDistributedLock owner) : IDistributedLockHandle
    {
        public Task<bool> IsStillHeldAsync(CancellationToken cancellationToken) =>
            Task.FromResult(owner.IsHeld && !owner.LoseLockDuringRun);

        public ValueTask DisposeAsync()
        {
            owner.IsHeld = false;
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class FakeCatalogProvider : ICatalogProvider
{
    public IReadOnlyList<ProviderProduct> Products { get; set; } = [];

    public Exception? Failure { get; set; }

    public int CallCount { get; private set; }

    public Task<IReadOnlyList<ProviderProduct>> GetAllProductsAsync(CancellationToken cancellationToken)
    {
        CallCount++;
        return Failure is null ? Task.FromResult(Products) : Task.FromException<IReadOnlyList<ProviderProduct>>(Failure);
    }
}

/// <summary>
/// Catalog kept in memory. Products are domain objects changed in place: rollback is not simulated here,
/// it is covered by the integration tests on SQL Server.
/// </summary>
internal sealed class InMemoryCatalogSyncStore : ICatalogSyncStore
{
    private readonly Dictionary<int, Product> _products = [];
    private readonly List<Product> _pending = [];

    public int SaveCount { get; private set; }

    public Exception? SaveFailure { get; set; }

    public Dictionary<int, Product>.ValueCollection Products => _products.Values;

    public Product Get(int externalId) => _products[externalId];

    public void Seed(params Product[] products)
    {
        foreach (var product in products)
        {
            _products[product.ExternalId] = product;
        }
    }

    public Task<IReadOnlySet<int>> GetActiveExternalIdsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<int>>(
            _products.Values.Where(product => product.IsActive).Select(product => product.ExternalId).ToHashSet());

    public Task<IReadOnlyList<Product>> GetByExternalIdsAsync(
        IReadOnlyCollection<int> externalIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Product>>(
            externalIds.Where(_products.ContainsKey).Select(externalId => _products[externalId]).ToList());

    public void Add(Product product) => _pending.Add(product);

    public Task SaveBatchAsync(CancellationToken cancellationToken)
    {
        if (SaveFailure is not null)
        {
            return Task.FromException(SaveFailure);
        }

        foreach (var product in _pending)
        {
            _products.Add(product.ExternalId, product);
        }

        _pending.Clear();
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeCatalogSyncRunStore : ICatalogSyncRunStore
{
    private readonly List<CatalogSyncRun> _runs = [];

    public List<CatalogSyncRun> Runs => _runs;

    public DateTimeOffset? LastSuccessfulCompletion { get; set; }

    public Task AddAsync(CatalogSyncRun run, CancellationToken cancellationToken)
    {
        _runs.Add(run);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(CatalogSyncRun run, CancellationToken cancellationToken)
    {
        if (run.Status == CatalogSyncStatus.Succeeded)
        {
            LastSuccessfulCompletion = run.CompletedAt;
        }

        return Task.CompletedTask;
    }

    public Task<DateTimeOffset?> GetLastSuccessfulCompletionAsync(CancellationToken cancellationToken) =>
        Task.FromResult(LastSuccessfulCompletion);
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int BeginCount { get; private set; }

    public bool Committed { get; private set; }

    public Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        BeginCount++;
        return Task.FromResult<ITransactionScope>(new Scope(this));
    }

    private sealed class Scope(FakeUnitOfWork owner) : ITransactionScope
    {
        public Task CommitAsync(CancellationToken cancellationToken)
        {
            owner.Committed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
