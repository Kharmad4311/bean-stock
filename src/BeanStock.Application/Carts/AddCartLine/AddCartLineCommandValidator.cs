using BeanStock.Application.Carts.Common;
using FluentValidation;

namespace BeanStock.Application.Carts.AddCartLine;

/// <remarks>The quantity is checked by the cart itself, which also knows the quantity already in the line.</remarks>
internal sealed class AddCartLineCommandValidator : AbstractValidator<AddCartLineCommand>
{
    public AddCartLineCommandValidator()
    {
        RuleFor(command => command.UserId).ValidUserId();
        RuleFor(command => command.ProductId).NotEmpty();
    }
}
