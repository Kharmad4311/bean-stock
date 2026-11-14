using BeanStock.Domain.Catalog;
using ErrorOr;

namespace BeanStock.Domain.Carts;

/// <summary>
/// Personal cart of a user (aggregate root).
/// </summary>
/// <remarks>
/// Every accepted change of the content increments <see cref="Version"/> by exactly 1.
/// A rejected request returns an error and leaves the cart untouched.
/// Concurrency between instances is handled by the persistence layer; this class
/// guarantees the rules for one consistent, serialized view of the cart.
/// </remarks>
public sealed class Cart
{
    private readonly List<CartLine> _lines = [];

    // Required by EF Core.
    private Cart()
    {
        UserId = string.Empty;
    }

    public Guid Id { get; private set; }

    public string UserId { get; private set; }

    /// <summary>Global version of the cart, starting at 0.</summary>
    public int Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<CartLine> Lines => _lines.AsReadOnly();

    /// <summary>Total computed from the captured unit prices.</summary>
    public decimal Total => _lines.Sum(line => line.Subtotal);

    public static ErrorOr<Cart> Create(string userId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > CartRules.UserIdMaxLength)
        {
            return CartErrors.InvalidUserId;
        }

        return new Cart
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Version = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Adds a product, or increases the quantity of the existing line for this product.
    /// </summary>
    public ErrorOr<CartLine> AddItem(Product product, int quantity, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (!product.IsActive)
        {
            return CartErrors.ProductUnavailable(product.Id);
        }

        if (!CartRules.IsValidQuantity(quantity))
        {
            return CartErrors.QuantityOutOfRange(quantity);
        }

        var existingLine = _lines.Find(line => line.ProductId == product.Id);
        if (existingLine is null)
        {
            var line = new CartLine(Id, product, quantity);
            _lines.Add(line);
            Touch(now);
            return line;
        }

        var newQuantity = existingLine.Quantity + quantity;
        if (newQuantity > CartRules.MaxQuantity)
        {
            return CartErrors.QuantityLimitExceeded(existingLine.Quantity, quantity);
        }

        existingLine.SetQuantity(newQuantity);
        Touch(now);
        return existingLine;
    }

    /// <summary>
    /// Replaces the quantity of a line, provided the line has not changed since <paramref name="expectedLineVersion"/>.
    /// </summary>
    /// <remarks>Setting the current quantity again is accepted but is not a change: no version moves.</remarks>
    public ErrorOr<CartLine> ChangeQuantity(Guid lineId, int expectedLineVersion, int quantity, DateTimeOffset now)
    {
        if (!CartRules.IsValidQuantity(quantity))
        {
            return CartErrors.QuantityOutOfRange(quantity);
        }

        var lineResult = FindLine(lineId, expectedLineVersion);
        if (lineResult.IsError)
        {
            return lineResult.Errors;
        }

        var line = lineResult.Value;
        if (line.Quantity == quantity)
        {
            return line;
        }

        line.SetQuantity(quantity);
        Touch(now);
        return line;
    }

    /// <summary>
    /// Removes a line, provided it has not changed since <paramref name="expectedLineVersion"/>.
    /// </summary>
    public ErrorOr<Deleted> RemoveLine(Guid lineId, int expectedLineVersion, DateTimeOffset now)
    {
        var lineResult = FindLine(lineId, expectedLineVersion);
        if (lineResult.IsError)
        {
            return lineResult.Errors;
        }

        _lines.Remove(lineResult.Value);
        Touch(now);
        return Result.Deleted;
    }

    private ErrorOr<CartLine> FindLine(Guid lineId, int expectedLineVersion)
    {
        var line = _lines.Find(candidate => candidate.Id == lineId);
        if (line is null)
        {
            return CartErrors.LineNotFound(lineId);
        }

        if (line.Version != expectedLineVersion)
        {
            return CartErrors.LineVersionConflict(lineId, expectedLineVersion, line.Version);
        }

        return line;
    }

    private void Touch(DateTimeOffset now)
    {
        Version++;
        UpdatedAt = now;
    }
}
