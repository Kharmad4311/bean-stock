using BeanStock.Application.Carts.Common;
using FluentValidation;

namespace BeanStock.Application.Carts.ChangeCartLineQuantity;

/// <remarks>The quantity range is checked by the cart itself.</remarks>
internal sealed class ChangeCartLineQuantityCommandValidator : AbstractValidator<ChangeCartLineQuantityCommand>
{
    public ChangeCartLineQuantityCommandValidator()
    {
        RuleFor(command => command.UserId).ValidUserId();
        RuleFor(command => command.LineId).NotEmpty();
        RuleFor(command => command.ExpectedLineVersion).NotNull().GreaterThanOrEqualTo(0);
    }
}
