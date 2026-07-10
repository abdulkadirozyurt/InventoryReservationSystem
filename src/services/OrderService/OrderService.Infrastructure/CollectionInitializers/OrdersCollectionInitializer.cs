using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using OrderService.Infrastructure.Mongo;

namespace OrderService.Infrastructure.CollectionInitializers;

public sealed class OrdersCollectionInitializer(IMongoDatabase database, IOptions<MongoDbOptions> options)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var collectionName = options.Value.OrdersCollectionName;

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
        var command = new BsonDocument
        {
            { "create", collectionName },
            { "validator", BuildValidator() },
            { "validationLevel", "strict" },
            { "validationAction", "error" }
        };

        await database.RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken);
    }

    private async Task ApplyValidationAsync(string collectionName, CancellationToken cancellationToken)
    {
        var command = new BsonDocument
        {
            { "collMod", collectionName },
            { "validator", BuildValidator() },
            { "validationLevel", "strict" },
            { "validationAction", "error" }
        };

        await database.RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken);
    }

    private async Task CreateIndexesAsync(CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<BsonDocument>(options.Value.OrdersCollectionName);

        var orderNumberIndexModel = new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("orderNumber"),
            new CreateIndexOptions<BsonDocument>
            {
                Name = "ux_orders_order_number",
                Unique = true
            });

        var reservationIdIndexModel = new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("reservationId"),
            new CreateIndexOptions<BsonDocument>
            {
                Name = "ux_orders_reservation_id",
                Unique = true,
                PartialFilterExpression = new BsonDocument("reservationId", new BsonDocument("$type", "string"))
            });

        var statusCreatedAtIndexModel = new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys
                .Ascending("status")
                .Descending("createdAt"),
            new CreateIndexOptions<BsonDocument>
            {
                Name = "ix_orders_status_created_at"
            });

        // Index for sorting by createdAt in descending order
        var createdAtIndexModel = new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Descending("createdAt"),
            new CreateIndexOptions<BsonDocument>
            {
                Name = "ix_orders_created_at"
            });

        await collection.Indexes.CreateManyAsync(
            [orderNumberIndexModel, reservationIdIndexModel, statusCreatedAtIndexModel, createdAtIndexModel],
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
                            "orderNumber",
                            "status",
                            "items",
                            "createdAt",
                            "updatedAt"
                        }
                    },
                    {
                        "properties", new BsonDocument
                        {
                            {
                                "orderNumber", new BsonDocument
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
                                "status", new BsonDocument
                                {
                                    { "bsonType", "string" },
                                    { "enum", new BsonArray { "Pending", "Confirmed", "Cancelled", "Expired" } }
                                }
                            },
                            {
                                "items", new BsonDocument
                                {
                                    { "bsonType", "array" },
                                    { "minItems", 1 },
                                    {
                                        "items", new BsonDocument
                                        {
                                            { "bsonType", "object" },
                                            {
                                                "required", new BsonArray
                                                {
                                                    "sku",
                                                    "warehouseId",
                                                    "requestedQuantity",
                                                    "reservedQuantity"
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
                                                        "requestedQuantity", new BsonDocument
                                                        {
                                                            { "bsonType", "int" },
                                                            { "minimum", 1 }
                                                        }
                                                    },
                                                    {
                                                        "reservedQuantity", new BsonDocument
                                                        {
                                                            { "bsonType", "int" },
                                                            { "minimum", 0 }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            },
                            {
                                "createdAt", new BsonDocument
                                {
                                    { "bsonType", "date" }
                                }
                            },
                            {
                                "updatedAt", new BsonDocument
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
