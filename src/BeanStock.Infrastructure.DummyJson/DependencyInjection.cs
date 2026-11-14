using BeanStock.Application.Abstractions.Catalog;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BeanStock.Infrastructure.DummyJson;

public static class DependencyInjection
{
    public static IServiceCollection AddDummyJsonCatalogProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<DummyJsonOptions>()
            .Bind(configuration.GetSection(DummyJsonOptions.SectionName))
            .Validate(options => options.BaseAddress.IsAbsoluteUri, "DummyJson:BaseAddress must be an absolute URI.")
            .Validate(options => options.PageSize is > 0 and <= 100, "DummyJson:PageSize must be between 1 and 100.")
            .ValidateOnStart();

        services.AddHttpClient<ICatalogProvider, DummyJsonCatalogProvider>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<DummyJsonOptions>>().Value;
                client.BaseAddress = options.BaseAddress;
            })
            .AddStandardResilienceHandler();

        return services;
    }
}
