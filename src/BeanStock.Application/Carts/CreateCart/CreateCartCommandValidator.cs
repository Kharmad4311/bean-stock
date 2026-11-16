using BeanStock.Application.Carts.Common;
using FluentValidation;

namespace BeanStock.Application.Carts.CreateCart;

internal sealed class CreateCartCommandValidator : AbstractValidator<CreateCartCommand>
{
    public CreateCartCommandValidator()
    {
        RuleFor(command => command.UserId).ValidUserId();
    }
}
