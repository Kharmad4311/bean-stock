namespace BeanStock.Domain.Catalog;

/// <summary>
/// Product of the local catalog, kept in sync with the external provider.
/// </summary>
/// <remarks>
/// Products are never deleted: a product that disappears from the provider is deactivated,
/// so carts that already contain it remain readable. It is reactivated if the provider offers it again.
/// </remarks>
public sealed class Product
{
    public const int NameMaxLength = 200;
    public const int CategoryMaxLength = 100;
    public const int AvailabilityStatusMaxLength = 50;

    /// <summary>
    /// Number of decimals kept for prices, aligned with the database precision. Rounding before comparing
    /// avoids reporting a change at every synchronization for a price stored with fewer decimals.
    /// </summary>
    public const int PriceDecimals = 2;

    // Required by EF Core.
    private Product()
    {
        Name = string.Empty;
        Category = string.Empty;
        AvailabilityStatus = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>Identifier of the product at the provider. Unique in the local catalog.</summary>
    public int ExternalId { get; private set; }

    public string Name { get; private set; }

    public string Category { get; private set; }

    public decimal Price { get; private set; }

    /// <summary>Indicative stock reported by the provider. No reservation is made.</summary>
    public int Stock { get; private set; }

    /// <summary>Indicative availability label reported by the provider (e.g. "In Stock").</summary>
    public string AvailabilityStatus { get; private set; }

    /// <summary>Last update date reported by the provider, when available.</summary>
    public DateTimeOffset? ProviderUpdatedAt { get; private set; }

    /// <summary>
    /// Date of the last local change: creation, change of a value, deactivation or reactivation.
    /// A synchronization that finds the product unchanged does not modify it.
    /// </summary>
    public DateTimeOffset LastUpdatedAt { get; private set; }

    /// <summary>False once the product is no longer offered by the provider.</summary>
    public bool IsActive { get; private set; }

    public static Product Create(
        int externalId,
        string name,
        string category,
        decimal price,
        int stock,
        string availabilityStatus,
        DateTimeOffset? providerUpdatedAt,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(externalId);
        EnsureValidValues(name, category, price, stock, availabilityStatus);

        return new Product
        {
            Id = Guid.CreateVersion7(),
            ExternalId = externalId,
            Name = name,
            Category = category,
            Price = NormalizePrice(price),
            Stock = stock,
            AvailabilityStatus = availabilityStatus ?? string.Empty,
            ProviderUpdatedAt = providerUpdatedAt,
            LastUpdatedAt = now,
            IsActive = true,
        };
    }

    /// <summary>
    /// Applies the values currently reported by the provider. The product becomes active again if it was not.
    /// </summary>
    /// <returns>What changed. <see cref="ProductSyncChange.Unchanged"/> leaves the product untouched.</returns>
    public ProductSyncChange ApplyProviderData(
        string name,
        string category,
        decimal price,
        int stock,
        string availabilityStatus,
        DateTimeOffset? providerUpdatedAt,
        DateTimeOffset now)
    {
        EnsureValidValues(name, category, price, stock, availabilityStatus);

        var normalizedPrice = NormalizePrice(price);
        var normalizedAvailability = availabilityStatus ?? string.Empty;
        var valuesChanged = Name != name
            || Category != category
            || Price != normalizedPrice
            || Stock != stock
            || AvailabilityStatus != normalizedAvailability
            || ProviderUpdatedAt != providerUpdatedAt;
        var reactivated = !IsActive;

        if (!valuesChanged && !reactivated)
        {
            return ProductSyncChange.Unchanged;
        }

        Name = name;
        Category = category;
        Price = normalizedPrice;
        Stock = stock;
        AvailabilityStatus = normalizedAvailability;
        ProviderUpdatedAt = providerUpdatedAt;
        IsActive = true;
        LastUpdatedAt = now;

        return reactivated ? ProductSyncChange.Reactivated : ProductSyncChange.Updated;
    }

    /// <summary>
    /// Marks the product as no longer offered by the provider. It stays readable in existing carts
    /// but can no longer be added.
    /// </summary>
    /// <returns><see langword="true"/> if the product was active.</returns>
    public bool Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return false;
        }

        IsActive = false;
        LastUpdatedAt = now;
        return true;
    }

    private static decimal NormalizePrice(decimal price) =>
        Math.Round(price, PriceDecimals, MidpointRounding.AwayFromZero);

    private static void EnsureValidValues(string name, string category, decimal price, int stock, string availabilityStatus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentOutOfRangeException.ThrowIfNegative(price);
        ArgumentOutOfRangeException.ThrowIfNegative(stock);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(name.Length, NameMaxLength, nameof(name));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(category.Length, CategoryMaxLength, nameof(category));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            availabilityStatus?.Length ?? 0,
            AvailabilityStatusMaxLength,
            nameof(availabilityStatus));
    }
}
