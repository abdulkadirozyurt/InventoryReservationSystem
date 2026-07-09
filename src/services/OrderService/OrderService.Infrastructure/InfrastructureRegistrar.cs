using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using InventoryReservationSystem.Contracts.Inventory;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using OrderService.Application.Orders.Abstractions;
using OrderService.Infrastructure.CollectionInitializers;
using OrderService.Infrastructure.HealthChecks;
using OrderService.Infrastructure.Mongo;
using OrderService.Infrastructure.Repositories.Orders;
using OrderService.Infrastructure.Services;
using OrderService.Infrastructure.Idempotency;
using StackExchange.Redis;

namespace OrderService.Infrastructure;

public static class InfrastructureRegistrar
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        MongoOrderMappings.Register();

        services.Configure<MongoDbOptions>(configuration.GetSection(nameof(MongoDbOptions)));
        services.Configure<IdempotencyOptions>(configuration.GetSection(IdempotencyOptions.SectionName));

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

        services.AddSingleton<OrdersCollectionInitializer>();
        services.AddSingleton<OrderHistoryCollectionInitializer>();
        services.AddSingleton<MongoCollectionInitializer>();

        services.AddScoped<IMongoSessionProvider, MongoSessionProvider>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderHistoryRepository, OrderHistoryRepository>();
        services.AddScoped<IOrderUnitOfWork, OrderUnitOfWork>();
        services.AddScoped<IInventoryReservationService, InventoryReservationService>();


        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var connectionString = configuration.GetConnectionString("Redis");

            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Redis connection string is missing.");

            return ConnectionMultiplexer.Connect(connectionString);
        });

        // Redis'te gerçek okuma/yazma işini IDatabase yapıyor.
        // IConnectionMultiplexer ana bağlantı nesnesi, IDatabase ise o bağlantıdan alınan çalışma alanı gibi düşünebilirsin.
        services.AddScoped(sp =>
            sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());

        // services.Configure<IdempotencyOptions>() bize IOptions<IdempotencyOptions> verir.
        // RedisIdempotencyStore ise doğrudan IdempotencyOptions istediği için burada value halini ayrıca DI'a koyuyoruz.
        services.AddSingleton(sp =>
            sp.GetRequiredService<IOptions<IdempotencyOptions>>().Value);

        services.AddScoped<IIdempotencyStore, RedisIdempotencyStore>();



        services.AddGrpcClient<InventoryReservations.InventoryReservationsClient>(options =>
        {
            options.Address = new Uri(configuration["InventoryService:Address"]!);
        });

        services.AddHealthChecks()
            .AddCheck<MongoDbHealthCheck>("mongodb")
            .AddCheck<RedisHealthCheck>("redis")
            .AddCheck<InventoryServiceHealthCheck>("inventoryservice-grpc");

        return services;
    }
}
