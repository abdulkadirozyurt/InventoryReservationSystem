using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace InventoryService.Infrastructure.HealthChecks;

internal sealed class RedisHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return HealthCheckResult.Unhealthy("Redis connection string is missing.");
        }

        try
        {
            await using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
            await connection.GetDatabase().PingAsync();

            return HealthCheckResult.Healthy("Redis ping succeeded.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Redis ping failed.", exception);
        }
    }
}
