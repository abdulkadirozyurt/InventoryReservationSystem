using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace InventoryService.Infrastructure.Mongo;

public sealed class ReservationsCollectionInitializer(IMongoDatabase database, IOptions<MongoDbOptions> options)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var collectionName = options.Value.ReservationsCollectionName;

        if (await CollectionExistsAsync(collectionName, cancellationToken))
        {
            await ApplyValidationAsync(collectionName, cancellationToken);
            return;
        }

        await CreateCollectionAsync(collectionName, cancellationToken);
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
                            "reservationId",
                            "orderId",
                            "items",
                            "status",
                            "createdAt",
                            "expiresAt",
                            "updatedAt"
                        }
                    },
                    {
                        "properties", new BsonDocument
                        {
                            {
                                "reservationId", new BsonDocument
                                {
                                    { "bsonType", "string" },
                                    { "minLength", 1 }
                                }
                            },
                            {
                                "orderId", new BsonDocument
                                {
                                    { "bsonType", "string" },
                                    { "minLength", 1 }
                                }
                            },
                            {
                                "items", new BsonDocument
                                {
                                    { "bsonType", "array" },
                                    { "minItems", 1 }
                                }
                            },
                            {
                                "status", new BsonDocument
                                {
                                    { "bsonType", "string" },
                                    { "enum", new BsonArray { "Pending", "Confirmed", "Released", "Expired" } }
                                }
                            },
                            {
                                "createdAt", new BsonDocument
                                {
                                    { "bsonType", "date" }
                                }
                            },
                            {
                                "expiresAt", new BsonDocument
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
