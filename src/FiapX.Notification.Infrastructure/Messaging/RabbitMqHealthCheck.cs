using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FiapX.Notification.Infrastructure.Messaging;

public sealed class RabbitMqHealthCheck(RabbitMqConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var conn = await connection.GetAsync(cancellationToken);
            return conn.IsOpen ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("connection closed");
        }
#pragma warning disable CA1031 // A health check reports failures instead of throwing.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return HealthCheckResult.Unhealthy("cannot connect to RabbitMQ", ex);
        }
    }
}
