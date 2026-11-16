using BeanStock.Domain.Catalog.Synchronization;
using FluentValidation;

namespace BeanStock.Application.Catalog.Synchronization;

internal sealed class SynchronizeCatalogCommandValidator : AbstractValidator<SynchronizeCatalogCommand>
{
    public SynchronizeCatalogCommandValidator()
    {
        RuleFor(command => command.Trigger).IsInEnum();

        RuleFor(command => command.MinimumInterval)
            .NotNull()
            .GreaterThan(TimeSpan.Zero)
            .When(command => command.Trigger == CatalogSyncTrigger.Scheduled)
            .WithMessage("A scheduled synchronization requires a positive minimum interval.");

        RuleFor(command => command.MinimumInterval)
            .Null()
            .When(command => command.Trigger == CatalogSyncTrigger.Manual)
            .WithMessage("A manual synchronization always runs: no minimum interval is allowed.");
    }
}
