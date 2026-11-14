using ErrorOr;

namespace BeanStock.Domain.Carts;

/// <summary>
/// Expected business errors of the cart. Codes are stable: clients may rely on them.
/// </summary>
public static class CartErrors
{
    public static Error InvalidUserId => Error.Validation(
        code: "Cart.InvalidUserId",
        description: $"The user identifier must be between 1 and {CartRules.UserIdMaxLength} characters.");

    public static Error QuantityOutOfRange(int quantity) => Error.Validation(
        code: "Cart.QuantityOutOfRange",
        description: $"The quantity must be between {CartRules.MinQuantity} and {CartRules.MaxQuantity}.",
        metadata: new Dictionary<string, object>
        {
            ["quantity"] = quantity,
            ["min"] = CartRules.MinQuantity,
            ["max"] = CartRules.MaxQuantity,
        });

    public static Error QuantityLimitExceeded(int currentQuantity, int requestedQuantity) => Error.Validation(
        code: "Cart.QuantityLimitExceeded",
        description: $"Adding this quantity would exceed the limit of {CartRules.MaxQuantity} for this line.",
        metadata: new Dictionary<string, object>
        {
            ["currentQuantity"] = currentQuantity,
            ["requestedQuantity"] = requestedQuantity,
            ["max"] = CartRules.MaxQuantity,
        });

    public static Error ProductUnavailable(Guid productId) => Error.Validation(
        code: "Cart.ProductUnavailable",
        description: "The product is no longer available in the catalog.",
        metadata: new Dictionary<string, object> { ["productId"] = productId });

    public static Error LineNotFound(Guid lineId) => Error.NotFound(
        code: "Cart.LineNotFound",
        description: "The cart line does not exist.",
        metadata: new Dictionary<string, object> { ["lineId"] = lineId });

    public static Error LineVersionConflict(Guid lineId, int expectedVersion, int currentVersion) => Error.Conflict(
        code: "Cart.LineVersionConflict",
        description: "The cart line has been modified since it was read.",
        metadata: new Dictionary<string, object>
        {
            ["lineId"] = lineId,
            ["expectedVersion"] = expectedVersion,
            ["currentVersion"] = currentVersion,
        });
}
