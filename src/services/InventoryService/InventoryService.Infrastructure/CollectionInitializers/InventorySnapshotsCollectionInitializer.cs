using InventoryService.Domain.Inventory;
using InventoryService.Infrastructure.Mongo;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace InventoryService.Infrastructure.CollectionInitializers;

public sealed class InventorySnapshotsCollectionInitializer(IMongoDatabase database, IOptions<MongoDbOptions> options)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var collectionName = options.Value.InventorySnapshotsCollectionName;

        if (await CollectionExistsAsync(collectionName, cancellationToken))
        {
            return;
        }

        await database.CreateCollectionAsync(collectionName, cancellationToken: cancellationToken);

        var collection = database.GetCollection<BsonDocument>(collectionName);

        // SnapshotId'ye göre tekil index oluşturuyoruz.
        var indexKeysDefinition = Builders<BsonDocument>.IndexKeys.Ascending("snapshotId");
        var indexOptions = new CreateIndexOptions { Unique = true, Name = "ix_inventory_snapshots_snapshot_id" };
        var indexModel = new CreateIndexModel<BsonDocument>(indexKeysDefinition, indexOptions);

        await collection.Indexes.CreateOneAsync(indexModel, cancellationToken: cancellationToken);
    }

    private async Task<bool> CollectionExistsAsync(string collectionName, CancellationToken cancellationToken)
    {
        var filter = new BsonDocument("name", collectionName);

        using var cursor = await database.ListCollectionNamesAsync(
            new ListCollectionNamesOptions { Filter = filter },
            cancellationToken);

        return await cursor.AnyAsync(cancellationToken);
    }
}
