namespace BeanStock.Infrastructure.DummyJson;

/// <summary>
/// Settings of the DummyJSON provider (configuration section <c>DummyJson</c>).
/// </summary>
public sealed class DummyJsonOptions
{
    public const string SectionName = "DummyJson";

    public Uri BaseAddress { get; set; } = new("https://dummyjson.com/");

    /// <summary>Number of products requested per call while fetching the catalog.</summary>
    public int PageSize { get; set; } = 100;
}
