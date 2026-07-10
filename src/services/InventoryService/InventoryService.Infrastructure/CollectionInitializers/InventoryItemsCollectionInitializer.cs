using InventoryService.Domain.Inventory;
using InventoryService.Infrastructure.Mongo;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace InventoryService.Infrastructure.CollectionInitializers;

public sealed class InventoryItemsCollectionInitializer(IMongoDatabase database, IOptions<MongoDbOptions> options)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var collectionName = options.Value.InventoryItemsCollectionName;

        if (await CollectionExistsAsync(collectionName, cancellationToken))
        {
            await ApplyValidationAsync(collectionName, cancellationToken);
        }
        else
        {
            await CreateCollectionAsync(collectionName, cancellationToken);
        }

        await CreateIndexesAsync(cancellationToken);
    }

    private async Task CreateCollectionAsync(string collectionName, CancellationToken cancellationToken)
    {
        /*
        var command = new BsonDocument
        {
            { "create", collectionName },
            { "validator", BuildValidator() },
            { "validationLevel", "strict" },
            { "validationAction", "error" }
        };

        await database.RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken);
        */
        await database.CreateCollectionAsync(collectionName, cancellationToken: cancellationToken);
    }

    private async Task ApplyValidationAsync(string collectionName, CancellationToken cancellationToken)
    {
        /*
        var command = new BsonDocument
        {
            { "collMod", collectionName },
            { "validator", BuildValidator() },
            { "validationLevel", "strict" },
            { "validationAction", "error" }
        };

        await database.RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken);
        */
        await Task.CompletedTask;
    }

    private async Task CreateIndexesAsync(CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<InventoryItem>(options.Value.InventoryItemsCollectionName);

        var indexKeys = Builders<InventoryItem>.IndexKeys
            .Ascending(x => x.Sku)
            .Ascending(x => x.WarehouseId);

        var indexModel = new CreateIndexModel<InventoryItem>(indexKeys, new CreateIndexOptions
        {
            Name = "ux_inventory_items_sku_warehouse",
            Unique = true
        });

        await collection.Indexes.CreateOneAsync(indexModel, cancellationToken: cancellationToken);
    }

    private static BsonDocument BuildValidator()
    {
        return new BsonDocument
        {
            {
                "$jsonSchema", new BsonDocument
                {
                    { "bsonType", "object" },
                    {
                        "required", new BsonArray
                        {
                            "sku",
                            "warehouseId",
                            "quantityAvailable",
                            "quantityReserved"
                        }
                    },
                    {
                        "properties", new BsonDocument
                        {
                            {
                                "sku", new BsonDocument
                                {
                                    { "bsonType", "string" },
                                    { "minLength", 1 }
                                }
                            },
                            {
                                "warehouseId", new BsonDocument
                                {
                                    { "bsonType", "string" },
                                    { "minLength", 1 }
                                }
                            },
                            {
                                "quantityAvailable", new BsonDocument
                                {
                                    { "bsonType", "int" },
                                    { "minimum", 0 }
                                }
                            },
                            {
                                "quantityReserved", new BsonDocument
                                {
                                    { "bsonType", "int" },
                                    { "minimum", 0 }
                                }
                            }
                        }
                    }
                }
            }
        };
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
