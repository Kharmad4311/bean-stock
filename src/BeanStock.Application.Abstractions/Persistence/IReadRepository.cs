using Ardalis.Specification;

namespace BeanStock.Application.Abstractions.Persistence;

/// <summary>
/// Read access to an entity type, driven by specifications.
/// </summary>
/// <remarks>
/// Reserved to reads. Concurrent writes go through dedicated repository methods
/// with conditional SQL, never through a specification.
/// </remarks>
public interface IReadRepository<T> : IReadRepositoryBase<T>
    where T : class;
