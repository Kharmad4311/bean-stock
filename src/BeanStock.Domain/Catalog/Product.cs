namespace BeanStock.Domain.Catalog;

/// <summary>
/// Product of the local catalog, kept in sync with the external provider.
/// </summary>
/// <remarks>
/// Products are never deleted: a product that disappears from the provider is deactivated,
/// so carts that already contain it remain readable.
/// </remarks>
public sealed class Product
{
    public const int NameMaxLength = 200;
    public const int CategoryMaxLength = 100;
    public const int AvailabilityStatusMaxLength = 50;

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

    /// <summary>Date of the last synchronization that wrote this product.</summary>
    public DateTimeOffset LastSynchronizedAt { get; private set; }

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
        DateTimeOffset synchronizedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentOutOfRangeException.ThrowIfNegative(price);

        return new Product
        {
            Id = Guid.CreateVersion7(),
            ExternalId = externalId,
            Name = name,
            Category = category,
            Price = price,
            Stock = stock,
            AvailabilityStatus = availabilityStatus ?? string.Empty,
            ProviderUpdatedAt = providerUpdatedAt,
            LastSynchronizedAt = synchronizedAt,
            IsActive = true,
        };
    }

    public void Deactivate() => IsActive = false;
}
