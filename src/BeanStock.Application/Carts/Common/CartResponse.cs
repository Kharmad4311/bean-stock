using BeanStock.Domain.Carts;

namespace BeanStock.Application.Carts.Common;

/// <summary>
/// Cart of the current user, as returned by every cart operation. The cart identifier is not exposed:
/// the cart is always reached through the current user.
/// </summary>
/// <param name="Version">Global version, starting at 0, incremented by exactly 1 on each accepted change.</param>
/// <param name="Total">Sum of the line subtotals, computed from the captured unit prices.</param>
/// <param name="Lines">Lines of the cart, in the order in which the products were added.</param>
/// <param name="CreatedAt">Creation date of the cart.</param>
/// <param name="UpdatedAt">Date of the last accepted change of the cart content.</param>
public sealed record CartResponse(
    int Version,
    decimal Total,
    IReadOnlyList<CartLineResponse> Lines,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Maps the aggregate to its API representation.</summary>
    /// <param name="cart">Cart to map, with its lines loaded.</param>
    public static CartResponse From(Cart cart)
    {
        ArgumentNullException.ThrowIfNull(cart);

        // Identifiers are version 7 GUIDs: ordering by identifier keeps the order in which lines were added.
        var lines = cart.Lines
            .OrderBy(line => line.Id)
            .Select(line => new CartLineResponse(
                line.Id,
                line.ProductId,
                line.ProductName,
                line.UnitPrice,
                line.Quantity,
                line.Subtotal,
                line.Version))
            .ToList();

        return new CartResponse(cart.Version, cart.Total, lines, cart.CreatedAt, cart.UpdatedAt);
    }
}

/// <summary>
/// Line of a cart. <c>ProductName</c> and <c>UnitPrice</c> are the values captured when the product was first added.
/// </summary>
/// <param name="LineId">Identifier of the line, used to change or remove it.</param>
/// <param name="ProductId">Local identifier of the product.</param>
/// <param name="ProductName">Name of the product when it was first added.</param>
/// <param name="UnitPrice">Unit price of the product when it was first added.</param>
/// <param name="Quantity">Quantity, between 1 and 999.</param>
/// <param name="Subtotal">Unit price multiplied by the quantity.</param>
/// <param name="Version">Line version, starting at 0: to send back to change or remove this line.</param>
public sealed record CartLineResponse(
    Guid LineId,
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal Subtotal,
    int Version);
