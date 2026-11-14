using BeanStock.Application.Catalog.Synchronization;
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

        services.AddOptions<CatalogSynchronizationOptions>()
            .BindConfiguration(CatalogSynchronizationOptions.SectionName)
            .Validate(
                options => options.BatchSize is > 0 and <= CatalogSynchronizationOptions.MaxBatchSize,
                $"CatalogSync:BatchSize must be between 1 and {CatalogSynchronizationOptions.MaxBatchSize}.")
            .Validate(
                options => options.MinimumCatalogRatio is >= 0 and <= 1,
                "CatalogSync:MinimumCatalogRatio must be between 0 and 1.")
            .ValidateOnStart();

        return services;
    }
}
