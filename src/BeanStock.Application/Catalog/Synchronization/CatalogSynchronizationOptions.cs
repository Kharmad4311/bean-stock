namespace BeanStock.Application.Catalog.Synchronization;

/// <summary>
/// Settings of the synchronization itself (configuration section <c>CatalogSync</c>), shared by the API
/// and the worker. The schedule is configured separately, in the worker (<c>CatalogSync:Schedule</c>).
/// </summary>
public sealed class CatalogSynchronizationOptions
{
    public const string SectionName = "CatalogSync";

    /// <summary>
    /// Upper bound of <see cref="BatchSize"/>: each batch is loaded with one <c>IN</c> clause, and SQL Server
    /// accepts at most 2100 parameters per query.
    /// </summary>
    public const int MaxBatchSize = 2000;

    /// <summary>Number of products loaded, changed and saved together.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// Safeguard against a truncated answer of the provider: the synchronization is refused when the provider
    /// offers fewer products than this share of the currently active ones (0.5 = half). 0 disables the check;
    /// an empty catalog is always refused.
    /// </summary>
    public double MinimumCatalogRatio { get; set; } = 0.5;
}
