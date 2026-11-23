using System.Text.Json;
using FiapX.Notification.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FiapX.Notification.Api.Observability;

public static class HealthEndpoints
{
    /// <summary>
    /// /health/live: the process answers (no dependencies, used by the liveness probe).
    /// /health/ready: Postgres and RabbitMQ reachable (readiness probe takes the pod out of the Service).
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteAsync,
        }).AllowAnonymous();

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(DependencyInjection.ReadyTag),
            ResponseWriter = WriteAsync,
        }).AllowAnonymous();

        return app;
    }

    internal static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), context.RequestAborted);
    }
}
