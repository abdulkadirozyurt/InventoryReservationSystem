using InventoryReservationSystem.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace InventoryReservationSystem.IntegrationTests;

public sealed class HealthCheckTests : IntegrationTestBase
{
    public HealthCheckTests(InventoryServiceFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task ReadinessCheck_MongoDbRunning_ShouldBeHealthy()
    {
        var healthCheckService = Fixture.ServiceProvider.GetRequiredService<HealthCheckService>();

        var report = await healthCheckService.CheckHealthAsync();

        var entry = report.Entries["mongodb"];
        Assert.Equal(HealthStatus.Healthy, entry.Status);
    }

    [Fact]
    public async Task ReadinessCheck_RedisRunning_ShouldBeHealthy()
    {
        var healthCheckService = Fixture.ServiceProvider.GetRequiredService<HealthCheckService>();

        var report = await healthCheckService.CheckHealthAsync();

        var entry = report.Entries["redis"];
        Assert.Equal(HealthStatus.Healthy, entry.Status);
    }
}
