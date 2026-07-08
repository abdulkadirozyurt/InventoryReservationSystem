using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using OrderService.Infrastructure.Mongo;

namespace OrderService.Infrastructure.CollectionInitializers;

public sealed class OrderHistoryCollectionInitializer(IMongoDatabase database, IOptions<MongoDbOptions> options)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var collectionName = options.Value.OrderHistoryCollectionName;

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
        var collection = database.GetCollection<BsonDocument>(options.Value.OrderHistoryCollectionName);

        var orderNumberChangedAtIndexModel = new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys
                .Ascending("orderNumber")
                .Ascending("changedAt"),
            new CreateIndexOptions<BsonDocument>
            {
                Name = "ix_order_history_order_number_changed_at"
            });

        var correlationIdIndexModel = new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("correlationId"),
            new CreateIndexOptions<BsonDocument>
            {
                Name = "ix_order_history_correlation_id"
            });

        var toStatusChangedAtIndexModel = new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys
                .Ascending("toStatus")
                .Descending("changedAt"),
            new CreateIndexOptions<BsonDocument>
            {
                Name = "ix_order_history_to_status_changed_at"
            });

        await collection.Indexes.CreateManyAsync(
            [orderNumberChangedAtIndexModel, correlationIdIndexModel, toStatusChangedAtIndexModel],
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
                            "toStatus",
                            "changedAt",
                            "correlationId",
                            "reason"
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
                                "fromStatus", new BsonDocument
                                {
                                    { "bsonType", "string" },
                                    { "enum", new BsonArray { "Pending", "Confirmed", "Cancelled", "Expired" } }
                                }
                            },
                            {
                                "toStatus", new BsonDocument
                                {
                                    { "bsonType", "string" },
                                    { "enum", new BsonArray { "Pending", "Confirmed", "Cancelled", "Expired" } }
                                }
                            },
                            {
                                "changedAt", new BsonDocument
                                {
                                    { "bsonType", "date" }
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
                                "reason", new BsonDocument
                                {
                                    { "bsonType", "string" },
                                    { "minLength", 1 }
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
