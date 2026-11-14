namespace BeanStock.Domain.Carts;

/// <summary>
/// Limits of the cart business rules.
/// </summary>
public static class CartRules
{
    public const int MinQuantity = 1;
    public const int MaxQuantity = 999;
    public const int UserIdMaxLength = 64;

    public static bool IsValidQuantity(int quantity) => quantity is >= MinQuantity and <= MaxQuantity;
}
