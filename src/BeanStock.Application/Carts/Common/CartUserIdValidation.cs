using BeanStock.Domain.Carts;
using FluentValidation;

namespace BeanStock.Application.Carts.Common;

internal static class CartUserIdValidation
{
    /// <summary>The API always provides the current user; this guards the use cases against any other caller.</summary>
    public static IRuleBuilderOptions<T, string> ValidUserId<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(CartRules.UserIdMaxLength);
}
