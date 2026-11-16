using BeanStock.Application.Carts.Common;
using FluentValidation;

namespace BeanStock.Application.Carts.RemoveCartLine;

internal sealed class RemoveCartLineCommandValidator : AbstractValidator<RemoveCartLineCommand>
{
    public RemoveCartLineCommandValidator()
    {
        RuleFor(command => command.UserId).ValidUserId();
        RuleFor(command => command.LineId).NotEmpty();
        RuleFor(command => command.ExpectedLineVersion).NotNull().GreaterThanOrEqualTo(0);
    }
}
