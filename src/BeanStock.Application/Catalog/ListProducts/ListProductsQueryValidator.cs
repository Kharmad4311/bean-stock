using BeanStock.Application.Common.Paging;
using FluentValidation;

namespace BeanStock.Application.Catalog.ListProducts;

internal sealed class ListProductsQueryValidator : AbstractValidator<ListProductsQuery>
{
    public ListProductsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PagingRules.MaxPageSize);
    }
}
