using InventoryService.Domain.InventoryTransactions;
using InventoryService.Infrastructure.Mongo;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace InventoryService.Infrastructure.CollectionInitializers;

public sealed class InventoryTransactionsCollectionInitializer(IMongoDatabase database, IOptions<MongoDbOptions> options)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var collectionName = options.Value.InventoryTransactionsCollectionName;

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
        var collection = database.GetCollection<InventoryTransaction>(options.Value.InventoryTransactionsCollectionName);

        // ilgili SKU ilgili depoda nasıl değişmiş.
        var skuWarehouseCreatedAtIndex = new CreateIndexModel<InventoryTransaction>(
            Builders<InventoryTransaction>.IndexKeys
                .Ascending(x => x.Sku)
                .Ascending(x => x.WarehouseId)
                .Descending(x => x.CreatedAt),
            new CreateIndexOptions
            {
                Name = "ix_inventory_transactions_sku_warehouse_created_at"
            });

        // CorrelationId ile transaction'lar arasında ilişki kurmak için.
        var correlationIdIndex = new CreateIndexModel<InventoryTransaction>(
            Builders<InventoryTransaction>.IndexKeys.Ascending(x => x.CorrelationId),
            new CreateIndexOptions
            {
                Name = "ix_inventory_transactions_correlation_id"
            });

        // reservasyon, stoğu nasıl etkilemiş
        var reservationIdIndex = new CreateIndexModel<InventoryTransaction>(
            Builders<InventoryTransaction>.IndexKeys.Ascending(x => x.ReservationId),
            new CreateIndexOptions
            {
                Name = "ix_inventory_transactions_reservation_id"
            });

        // siparişin stok geçmişi
        var orderIdIndex = new CreateIndexModel<InventoryTransaction>(
            Builders<InventoryTransaction>.IndexKeys.Ascending(x => x.OrderId),
            new CreateIndexOptions
            {
                Name = "ix_inventory_transactions_order_id"
            });

        await collection.Indexes.CreateManyAsync(
            [
                skuWarehouseCreatedAtIndex,
                correlationIdIndex,
                reservationIdIndex,
                orderIdIndex
            ],
            cancellationToken);
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
                            "type",
                            "quantityAvailableDelta",
                            "quantityReservedDelta",
                            "correlationId",
                            "createdAt"
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
                                "type", new BsonDocument
                                {
                                    { "bsonType", "string" },
                                    {
                                        "enum", new BsonArray
                                        {
                                            "Reserve",
                                            "Release",
                                            "Confirm",
                                            "AdjustStock",
                                            "Rebalance",
                                            "SnapshotRestore"
                                        }
                                    }
                                }
                            },
                            {
                                "quantityAvailableDelta", new BsonDocument
                                {
                                    { "bsonType", "int" }
                                }
                            },
                            {
                                "quantityReservedDelta", new BsonDocument
                                {
                                    { "bsonType", "int" }
                                }
                            },
                            {
                                "correlationId", new BsonDocument
                                {
                                    { "bsonType", "string" },
                                    { "minLength", 1 }
                                }
                            },
                            {
                                "reservationId", new BsonDocument
                                {
                                    { "bsonType", "string" }
                                }
                            },
                            {
                                "orderId", new BsonDocument
                                {
                                    { "bsonType", "string" }
                                }
                            },
                            {
                                "reason", new BsonDocument
                                {
                                    { "bsonType", "string" }
                                }
                            },
                            {
                                "createdAt", new BsonDocument
                                {
                                    { "bsonType", "date" }
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
