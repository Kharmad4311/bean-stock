using BeanStock.Application.Abstractions.Catalog;

namespace BeanStock.IntegrationTests.Infrastructure;

/// <summary>
/// Catalog provider controlled by the tests: returned products, failure, and a gate to hold a fetch
/// in progress (to test concurrent synchronizations).
/// </summary>
public sealed class FakeCatalogProvider : ICatalogProvider
{
    private FetchGate? _nextFetchGate;

    public IReadOnlyList<ProviderProduct> Products { get; set; } = [];

    public Exception? Failure { get; set; }

    public static ProviderProduct Product(int externalId, string name = "Product", decimal price = 10m) =>
        new(externalId, name, "groceries", price, Stock: 5, "In Stock", UpdatedAt: null);

    public static IReadOnlyList<ProviderProduct> Catalog(int count, string namePrefix = "Product") =>
        Enumerable.Range(1, count).Select(externalId => Product(externalId, $"{namePrefix} {externalId}")).ToList();

    /// <summary>The next fetch waits until the returned gate is released.</summary>
    public FetchGate BlockNextFetch()
    {
        var gate = new FetchGate();
        Volatile.Write(ref _nextFetchGate, gate);
        return gate;
    }

    public void Reset()
    {
        Products = [];
        Failure = null;
        Volatile.Write(ref _nextFetchGate, null);
    }

    public async Task<IReadOnlyList<ProviderProduct>> GetAllProductsAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _nextFetchGate, null) is { } gate)
        {
            gate.SignalEntered();
            await gate.WaitForReleaseAsync(cancellationToken);
        }

        return Failure is null ? Products : throw Failure;
    }

    public sealed class FetchGate
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes when a fetch is waiting at the gate (the synchronization holds the lock).</summary>
        public Task Entered => _entered.Task;

        public void Release() => _released.TrySetResult();

        internal void SignalEntered() => _entered.TrySetResult();

        internal Task WaitForReleaseAsync(CancellationToken cancellationToken) => _released.Task.WaitAsync(cancellationToken);
    }
}
