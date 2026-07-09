using InventoryService.Domain.Checkpoints;
using InventoryService.Infrastructure.Mongo;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace InventoryService.Infrastructure.CollectionInitializers;

public sealed class CheckpointsCollectionInitializer(IMongoDatabase database, IOptions<MongoDbOptions> options)
{
    private readonly string collectionName = options.Value.CheckpointsCollectionName;
    private readonly IMongoCollection<Checkpoint> collection = database.GetCollection<Checkpoint>(options.Value.CheckpointsCollectionName);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var filter = new BsonDocument("name", collectionName);
        var collections = await database.ListCollectionNamesAsync(new ListCollectionNamesOptions { Filter = filter }, cancellationToken);
        var collectionExists = await collections.AnyAsync(cancellationToken);

        if (!collectionExists)
        {
            await database.CreateCollectionAsync(collectionName, cancellationToken: cancellationToken);
        }
    }
}
