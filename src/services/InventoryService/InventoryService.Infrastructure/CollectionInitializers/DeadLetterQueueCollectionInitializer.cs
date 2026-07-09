using InventoryService.Domain.DeadLetterQueue;
using InventoryService.Infrastructure.Mongo;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace InventoryService.Infrastructure.CollectionInitializers;

public sealed class DeadLetterQueueCollectionInitializer(IMongoDatabase database, IOptions<MongoDbOptions> options)
{
    private readonly string collectionName = options.Value.DeadLetterQueueCollectionName;
    private readonly IMongoCollection<DeadLetterRecord> collection = database.GetCollection<DeadLetterRecord>(options.Value.DeadLetterQueueCollectionName);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var filter = new BsonDocument("name", collectionName);
        var collections = await database.ListCollectionNamesAsync(new ListCollectionNamesOptions { Filter = filter }, cancellationToken);
        var collectionExists = await collections.AnyAsync(cancellationToken);

        if (!collectionExists)
        {
            await database.CreateCollectionAsync(collectionName, cancellationToken: cancellationToken);
        }

        var indexModels = new[]
        {
            new CreateIndexModel<DeadLetterRecord>(
                Builders<DeadLetterRecord>.IndexKeys
                    .Ascending(dlq => dlq.OperationType)
                    .Ascending(dlq => dlq.ReservationId)
                    .Ascending(dlq => dlq.OrderId),
                new CreateIndexOptions
                {
                    Name = "ux_dlq_operation_reservation_order",
                    Unique = true
                }),
            new CreateIndexModel<DeadLetterRecord>(
                Builders<DeadLetterRecord>.IndexKeys
                    .Ascending(dlq => dlq.CorrelationId)
                    .Ascending(dlq => dlq.OperationType),
                new CreateIndexOptions { Name = "ix_dlq_correlation_operation" })
        };

        await collection.Indexes.CreateManyAsync(indexModels, cancellationToken: cancellationToken);
    }
}
