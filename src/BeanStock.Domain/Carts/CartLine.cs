using BeanStock.Domain.Catalog;

namespace BeanStock.Domain.Carts;

/// <summary>
/// Line of a cart. The product name and unit price are captured on first add and never refreshed.
/// </summary>
public sealed class CartLine
{
    // Required by EF Core.
    private CartLine()
    {
        ProductName = string.Empty;
    }

    internal CartLine(Guid cartId, Product product, int quantity)
    {
        Id = Guid.CreateVersion7();
        CartId = cartId;
        ProductId = product.Id;
        ProductName = product.Name;
        UnitPrice = product.Price;
        Quantity = quantity;
        Version = 0;
    }

    public Guid Id { get; private set; }

    public Guid CartId { get; private set; }

    public Guid ProductId { get; private set; }

    /// <summary>Product name captured when the line was created.</summary>
    public string ProductName { get; private set; }

    /// <summary>Unit price captured when the line was created.</summary>
    public decimal UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    /// <summary>Line version, incremented on each accepted change of this line.</summary>
    public int Version { get; private set; }

    public decimal Subtotal => UnitPrice * Quantity;

    internal void SetQuantity(int quantity)
    {
        Quantity = quantity;
        Version++;
    }
}
