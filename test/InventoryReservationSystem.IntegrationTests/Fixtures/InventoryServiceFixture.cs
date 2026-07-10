using InventoryService.Application;
using InventoryService.Infrastructure;
using InventoryService.Infrastructure.CollectionInitializers;
using InventoryService.Infrastructure.Mongo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Testcontainers.Redis;
using Xunit;

namespace InventoryReservationSystem.IntegrationTests.Fixtures;

public sealed class InventoryServiceFixture : IAsyncLifetime
{
    private MongoDbContainer? _mongoContainer;
    private RedisContainer? _redisContainer;
    private ServiceProvider? _serviceProvider;

    public IServiceProvider ServiceProvider =>
        _serviceProvider ?? throw new InvalidOperationException("ServiceProvider not initialized. Call InitializeAsync first.");

    public IMongoDatabase Database
    {
        get
        {
            var client = ServiceProvider.GetRequiredService<IMongoClient>();
            var options = ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MongoDbOptions>>().Value;
            return client.GetDatabase(options.DatabaseName);
        }
    }

    public async Task InitializeAsync()
    {
        _mongoContainer = new MongoDbBuilder("mongo:8.2.11")
            .WithReplicaSet()
            .Build();

        _redisContainer = new RedisBuilder("redis:8.8.0-alpine")
            .Build();

        await Task.WhenAll(
            _mongoContainer.StartAsync(),
            _redisContainer.StartAsync());

        var mongoConnectionString = _mongoContainer.GetConnectionString();
        var redisConnectionString = _redisContainer.GetConnectionString();

        var configValues = new Dictionary<string, string?>
        {
            ["ConnectionStrings:MongoDb"] = mongoConnectionString,
            ["MongoDbOptions:DatabaseName"] = "inventory-service-test",
            ["MongoDbOptions:InventoryItemsCollectionName"] = "inventoryItems",
            ["MongoDbOptions:ReservationsCollectionName"] = "reservations",
            ["MongoDbOptions:InventoryTransactionsCollectionName"] = "inventoryTransactions",
            ["MongoDbOptions:CheckpointsCollectionName"] = "checkpoints",
            ["MongoDbOptions:DeadLetterQueueCollectionName"] = "dead-letter-queue",
            ["MongoDbOptions:InventorySnapshotsCollectionName"] = "inventory-snapshots",
            ["RedisOptions:ConnectionString"] = redisConnectionString,
            ["SeedData:Enabled"] = "true",
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        services.AddInfrastructureServices(configuration);
        services.AddApplicationServices();

        _serviceProvider = services.BuildServiceProvider();

        var collectionInitializer = _serviceProvider.GetRequiredService<MongoCollectionInitializer>();
        await collectionInitializer.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
        }

        if (_mongoContainer is not null)
        {
            await _mongoContainer.DisposeAsync();
        }

        if (_redisContainer is not null)
        {
            await _redisContainer.DisposeAsync();
        }
    }

    public IServiceScope CreateScope()
    {
        return ServiceProvider.CreateScope();
    }

    public T Resolve<T>() where T : notnull
    {
        var scope = ServiceProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<T>();
    }
}
