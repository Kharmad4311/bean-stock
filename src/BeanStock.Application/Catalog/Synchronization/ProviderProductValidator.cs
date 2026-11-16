using BeanStock.Application.Abstractions.Catalog;
using BeanStock.Domain.Catalog;
using FluentValidation;

namespace BeanStock.Application.Catalog.Synchronization;

/// <summary>
/// Checks a product received from the provider before it is applied to the local catalog.
/// </summary>
/// <remarks>
/// Mirrors the invariants of <see cref="Product"/>: an invalid product is skipped instead of
/// failing the whole synchronization.
/// </remarks>
internal sealed class ProviderProductValidator : AbstractValidator<ProviderProduct>
{
    public ProviderProductValidator()
    {
        RuleFor(product => product.ExternalId).GreaterThan(0);
        RuleFor(product => product.Name).NotEmpty().MaximumLength(Product.NameMaxLength);
        RuleFor(product => product.Category).NotEmpty().MaximumLength(Product.CategoryMaxLength);
        RuleFor(product => product.Price).GreaterThanOrEqualTo(0);
        RuleFor(product => product.Stock).GreaterThanOrEqualTo(0);
        RuleFor(product => product.AvailabilityStatus).MaximumLength(Product.AvailabilityStatusMaxLength);
    }
}
