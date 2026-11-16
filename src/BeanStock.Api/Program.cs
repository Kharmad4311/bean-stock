using System.Text.Json.Serialization;
using BeanStock.Api.Authentication;
using BeanStock.Api.Endpoints;
using BeanStock.Application;
using BeanStock.Application.Common.Behaviors;
using BeanStock.Infrastructure.DummyJson;
using BeanStock.Infrastructure.Persistence;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Every error response is a ProblemDetails (RFC 9457), including unhandled exceptions.
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Enums are exchanged as text ("Manual", "Scheduled"): readable and stable if values are reordered.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Current user taken from the X-User-Id header (authentication is out of scope, see DECISIONS.md).
builder.Services.AddAuthentication(UserIdAuthentication.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, UserIdAuthenticationHandler>(UserIdAuthentication.SchemeName, configureOptions: null);
builder.Services.AddAuthorization();

builder.Services.AddApplication();
builder.Services.AddPersistence(builder.Configuration);

// Composition only: the manual synchronization sends the same command as the worker, whose handler needs
// the provider. No endpoint calls the provider directly.
builder.Services.AddDummyJsonCatalogProvider(builder.Configuration);

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
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthEndpoints();
app.MapCatalogEndpoints();
app.MapCatalogSyncEndpoints();
app.MapCartEndpoints();

if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await app.Services.ApplyMigrationsAsync(app.Lifetime.ApplicationStopping);
}

await app.RunAsync();
