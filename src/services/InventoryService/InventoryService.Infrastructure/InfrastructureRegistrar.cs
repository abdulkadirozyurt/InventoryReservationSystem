using InventoryService.Infrastructure.HealthChecks;
using InventoryService.Infrastructure.Mongo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace InventoryService.Infrastructure;

public static class InfrastructureRegistrar
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MongoDbOptions>(configuration.GetSection(nameof(MongoDbOptions)));

        services.AddSingleton<IMongoClient>(_ =>
        {
            var connectionString = configuration.GetConnectionString("MongoDb");

            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("MongoDb connection string is missing.");

            return new MongoClient(connectionString);
        });

        services.AddSingleton(sp =>
        {
            var client = sp.GetRequiredService<IMongoClient>();
            var options = sp.GetRequiredService<IOptions<MongoDbOptions>>().Value;

            return client.GetDatabase(options.DatabaseName);
        });

        services.AddSingleton<InventoryItemsCollectionInitializer>();
        services.AddSingleton<ReservationsCollectionInitializer>();
        services.AddSingleton<InventoryTransactionsCollectionInitializer>();
        services.AddSingleton<MongoCollectionInitializer>();

        services.AddHealthChecks()
            .AddCheck<MongoDbHealthCheck>("mongodb")
            .AddCheck<RedisHealthCheck>("redis");

        return services;
    }
}
