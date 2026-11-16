using BeanStock.Domain.Carts;
using ErrorOr;

namespace BeanStock.Application.Carts.Common;

internal static class CartChangeResults
{
    /// <summary>Keeps only the outcome of a change of the aggregate (accepted or the errors of the rejection).</summary>
    public static ErrorOr<Success> ToSuccess<T>(this ErrorOr<T> result)
    {
        if (result.IsError)
        {
            return result.Errors;
        }

        return Result.Success;
    }

    /// <summary>Response of a cart operation: the changed cart, or the errors.</summary>
    public static ErrorOr<CartResponse> ToResponse(this ErrorOr<Cart> result)
    {
        if (result.IsError)
        {
            return result.Errors;
        }

        return CartResponse.From(result.Value);
    }
}
