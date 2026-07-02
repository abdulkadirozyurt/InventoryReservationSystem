using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace InventoryService.Infrastructure.HealthChecks;

internal sealed class MongoDbHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("MongoDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return HealthCheckResult.Unhealthy("MongoDb connection string is missing.");
        }

        try
        {
            var client = new MongoClient(connectionString);
            await client.GetDatabase("admin")
                .RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cancellationToken);

            return HealthCheckResult.Healthy("MongoDb ping succeeded.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("MongoDb ping failed.", exception);
        }
    }
}
