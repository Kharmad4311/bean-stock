using BeanStock.Application;
using BeanStock.Application.Common.Behaviors;
using BeanStock.Infrastructure.DummyJson;
using BeanStock.Infrastructure.Persistence;
using Mediator;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddDummyJsonCatalogProvider(builder.Configuration);
builder.Services.AddMediator((MediatorOptions options) =>
{
    options.Assemblies = [typeof(ApplicationAssembly)];
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.PipelineBehaviors = [typeof(ValidationBehavior<,>)];
});

// Synchronization triggers (BackgroundService) are added with the catalog synchronization use case.

var host = builder.Build();
await host.RunAsync();
