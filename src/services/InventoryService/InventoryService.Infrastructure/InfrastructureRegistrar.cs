using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Infrastructure.CollectionInitializers;
using InventoryService.Infrastructure.HealthChecks;
using InventoryService.Infrastructure.Mongo;
using InventoryService.Infrastructure.Redis;
using InventoryService.Infrastructure.Repositories.Inventory;
using InventoryService.Infrastructure.Repositories.Reservations;
using InventoryService.Infrastructure.Repositories.Checkpoints;
using InventoryService.Infrastructure.Repositories.DeadLetterQueue;
using InventoryService.Infrastructure.BackgroundJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using StackExchange.Redis;

namespace InventoryService.Infrastructure;

public static class InfrastructureRegistrar
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MongoDbOptions>(configuration.GetSection(nameof(MongoDbOptions)));
        services.Configure<RedisOptions>(configuration.GetSection(nameof(RedisOptions)));
        services.Configure<ExpiryWorkerOptions>(configuration.GetSection(nameof(ExpiryWorkerOptions)));
        services.Configure<ReconciliationWorkerOptions>(configuration.GetSection(nameof(ReconciliationWorkerOptions)));
        services.AddHostedService<ReservationExpiryBackgroundService>();
        services.AddHostedService<InventoryReconciliationBackgroundService>();

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
        services.AddSingleton<CheckpointsCollectionInitializer>();
        services.AddSingleton<DeadLetterQueueCollectionInitializer>();
        services.AddSingleton<MongoCollectionInitializer>();

        services.AddSingleton<IDistributedLockService, RedisDistributedLockService>();
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RedisOptions>>().Value;

            if (string.IsNullOrWhiteSpace(options.ConnectionString))
                throw new InvalidOperationException("Redis connection string is missing.");

            return ConnectionMultiplexer.Connect(options.ConnectionString);
        });

        services.AddScoped<IInventoryItemRepository, InventoryItemRepository>();
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<IInventoryTransactionRepository, InventoryTransactionRepository>();
        services.AddScoped<ICheckpointRepository, CheckpointRepository>();
        services.AddScoped<IDeadLetterQueueRepository, DeadLetterQueueRepository>();
        services.AddScoped<IInventoryUnitOfWork, InventoryUnitOfWork>();
        services.AddScoped<IMongoSessionProvider, MongoSessionProvider>();

        services.AddHealthChecks()
            .AddCheck<MongoDbHealthCheck>("mongodb")
            .AddCheck<RedisHealthCheck>("redis");

        return services;
    }
}
