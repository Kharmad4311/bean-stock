using Ardalis.Specification.EntityFrameworkCore;
using BeanStock.Application.Abstractions.Persistence;

namespace BeanStock.Infrastructure.Persistence.Repositories;

/// <summary>
/// Specification-based read repository. Write methods inherited from the base class are not exposed
/// through <see cref="IReadRepository{T}"/>.
/// </summary>
internal sealed class EfReadRepository<T>(BeanStockDbContext dbContext)
    : RepositoryBase<T>(dbContext), IReadRepository<T>
    where T : class;
