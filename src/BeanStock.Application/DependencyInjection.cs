using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BeanStock.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the application services.
    /// </summary>
    /// <remarks>
    /// Mediator itself is registered by each host (Api, Worker), because its source generator
    /// runs in the project that calls <c>AddMediator</c>.
    /// </remarks>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(ApplicationAssembly).Assembly, includeInternalTypes: true);
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
