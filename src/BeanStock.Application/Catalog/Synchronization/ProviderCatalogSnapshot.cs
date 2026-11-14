using BeanStock.Application.Abstractions.Catalog;
using FluentValidation;

namespace BeanStock.Application.Catalog.Synchronization;

/// <summary>
/// Catalog received from the provider, sorted into what can be applied and what must be skipped.
/// </summary>
/// <remarks>
/// A skipped product with a usable identifier is still considered offered by the provider: it is neither
/// updated nor deactivated, so a single corrupted entry cannot hide a product from the catalog.
/// When an identifier appears several times, there is no way to tell which entry is right: all of them are skipped.
/// </remarks>
internal sealed class ProviderCatalogSnapshot
{
    private ProviderCatalogSnapshot(
        int receivedCount,
        IReadOnlyList<ProviderProduct> validProducts,
        IReadOnlySet<int> offeredExternalIds,
        IReadOnlyList<SkippedProduct> skippedProducts)
    {
        ReceivedCount = receivedCount;
        ValidProducts = validProducts;
        OfferedExternalIds = offeredExternalIds;
        SkippedProducts = skippedProducts;
    }

    /// <summary>Number of products returned by the provider.</summary>
    public int ReceivedCount { get; }

    /// <summary>Products to apply, with unique identifiers.</summary>
    public IReadOnlyList<ProviderProduct> ValidProducts { get; }

    /// <summary>Identifiers offered by the provider, valid or not: these products must not be deactivated.</summary>
    public IReadOnlySet<int> OfferedExternalIds { get; }

    public IReadOnlyList<SkippedProduct> SkippedProducts { get; }

    public static ProviderCatalogSnapshot Create(
        IReadOnlyList<ProviderProduct> products,
        IValidator<ProviderProduct> validator)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(validator);

        var valid = new List<ProviderProduct>(products.Count);
        var skipped = new List<SkippedProduct>();
        var offered = new HashSet<int>();

        foreach (var group in products.GroupBy(product => product.ExternalId))
        {
            if (group.Key > 0)
            {
                offered.Add(group.Key);
            }

            if (group.Skip(1).Any())
            {
                skipped.AddRange(group.Select(_ => new SkippedProduct(group.Key, "Duplicate identifier in the provider catalog.")));
                continue;
            }

            var product = group.First();
            var validation = validator.Validate(product);
            if (validation.IsValid)
            {
                valid.Add(product);
            }
            else
            {
                skipped.Add(new SkippedProduct(product.ExternalId, validation.ToString("; ")));
            }
        }

        return new ProviderCatalogSnapshot(products.Count, valid, offered, skipped);
    }
}
