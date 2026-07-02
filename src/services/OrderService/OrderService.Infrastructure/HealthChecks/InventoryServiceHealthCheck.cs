using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OrderService.Infrastructure;

internal sealed class InventoryServiceHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var address = configuration["InventoryService:Address"];
        if (string.IsNullOrWhiteSpace(address))
        {
            return HealthCheckResult.Unhealthy("InventoryService address is missing.");
        }

        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(3)
            };

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{address.TrimEnd('/')}/health")
            {
                Version = new Version(2, 0),
                VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
            };

            using var response = await client.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("InventoryService health endpoint is reachable.")
                : HealthCheckResult.Unhealthy($"InventoryService health endpoint returned {(int)response.StatusCode}.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("InventoryService health endpoint is unreachable.", exception);
        }
    }
}
