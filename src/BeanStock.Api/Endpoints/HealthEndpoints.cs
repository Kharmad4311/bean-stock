using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using PersistenceRegistration = BeanStock.Infrastructure.Persistence.DependencyInjection;

namespace BeanStock.Api.Endpoints;

internal static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // Liveness: the process answers. No dependency is checked.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

        // Readiness: the instance can serve requests (database reachable).
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(PersistenceRegistration.HealthCheckTag),
        });

        return app;
    }
}
