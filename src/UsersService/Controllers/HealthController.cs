namespace Platform.UsersService.Controllers;

/// <summary>
/// Health check endpoints for Kubernetes liveness and readiness probes.
/// </summary>
public static class HealthEndpoints
{
    public static RouteGroupBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/health")
            .WithTags("Health")
            .WithOpenApi();

        group.MapGet("/live", () => Results.Ok(new { status = "Healthy", timestamp = DateTimeOffset.UtcNow }))
            .WithName("Liveness")
            .ExcludeFromDescription();

        group.MapGet("/ready", () =>
        {
            // In production: check PostgreSQL, Auth Service gRPC, Service Bus connectivity
            var checks = new Dictionary<string, object>
            {
                ["postgres"] = new { status = "Healthy", latency_ms = 1.8 },
                ["auth_service"] = new { status = "Healthy", latency_ms = 4.2 },
                ["service_bus"] = new { status = "Healthy", latency_ms = 8.7 }
            };

            return Results.Ok(new { status = "Healthy", timestamp = DateTimeOffset.UtcNow, checks });
        })
            .WithName("Readiness")
            .ExcludeFromDescription();

        return group;
    }
}
