using BeanStock.Application;
using BeanStock.Application.Common.Behaviors;
using BeanStock.Infrastructure.DummyJson;
using BeanStock.Infrastructure.Persistence;
using BeanStock.Worker.Scheduling;
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

// The worker only holds triggers: the synchronization itself is the SynchronizeCatalogCommand use case,
// also sent by the API (POST /admin/catalog/sync).
builder.Services.AddCatalogSyncScheduling();

var host = builder.Build();
await host.RunAsync();
