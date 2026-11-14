using BeanStock.Api.Endpoints;
using BeanStock.Application;
using BeanStock.Application.Common.Behaviors;
using BeanStock.Infrastructure.Persistence;
using Mediator;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Every error response is a ProblemDetails (RFC 9457), including unhandled exceptions.
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.AddApplication();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddMediator((MediatorOptions options) =>
{
    options.Assemblies = [typeof(ApplicationAssembly)];
    // Scoped: handlers use the request-scoped DbContext.
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.PipelineBehaviors = [typeof(ValidationBehavior<,>)];
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthEndpoints();
app.MapCatalogEndpoints();

if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await app.Services.ApplyMigrationsAsync(app.Lifetime.ApplicationStopping);
}

await app.RunAsync();
