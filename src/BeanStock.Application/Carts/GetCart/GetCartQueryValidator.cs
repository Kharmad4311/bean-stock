using BeanStock.Application.Carts.Common;
using FluentValidation;

namespace BeanStock.Application.Carts.GetCart;

internal sealed class GetCartQueryValidator : AbstractValidator<GetCartQuery>
{
    public GetCartQueryValidator()
    {
        RuleFor(query => query.UserId).ValidUserId();
    }
}
