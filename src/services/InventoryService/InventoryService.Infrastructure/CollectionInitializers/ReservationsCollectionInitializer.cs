using InventoryService.Domain.Reservations;
using InventoryService.Infrastructure.Mongo;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace InventoryService.Infrastructure.CollectionInitializers;

public sealed class ReservationsCollectionInitializer(IMongoDatabase database, IOptions<MongoDbOptions> options)
{
    private readonly string collectionName = options.Value.ReservationsCollectionName;

    private readonly IMongoCollection<Reservation> collection =
        database.GetCollection<Reservation>(options.Value.ReservationsCollectionName);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (await CollectionExistsAsync(cancellationToken))
        {
            await ApplyValidationAsync(collectionName, cancellationToken);
        }
        else
        {
            await CreateCollectionAsync(collectionName, cancellationToken);
        }

        // Collection ister önceden var olsun ister yeni oluşsun, gerekli indexleri kontrol ediyoruz.
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

    private async Task<bool> CollectionExistsAsync(CancellationToken cancellationToken)
    {
        var filter = new BsonDocument("name", collectionName);

        using var cursor = await database.ListCollectionNamesAsync(
            new ListCollectionNamesOptions { Filter = filter },
            cancellationToken);

        return await cursor.AnyAsync(cancellationToken);
    }

    private async Task CreateIndexesAsync(CancellationToken cancellationToken)
    {
        var indexModels = new[]
        {
            new CreateIndexModel<Reservation>(
                Builders<Reservation>.IndexKeys.Ascending(reservation => reservation.OrderId),
                new CreateIndexOptions
                {
                    Name = "ux_reservations_order_id",
                    Unique = true
                }),
            new CreateIndexModel<Reservation>(
                Builders<Reservation>.IndexKeys.Ascending(reservation => reservation.ReservationId),
                new CreateIndexOptions
                {
                    Name = "ux_reservations_reservation_id",
                    Unique = true
                }),
            new CreateIndexModel<Reservation>(
                Builders<Reservation>.IndexKeys
                    .Ascending(reservation => reservation.Status)
                    .Ascending(reservation => reservation.ExpiresAt),
                new CreateIndexOptions
                {
                    Name = "ix_reservations_status_expires_at"
                })
        };

        // Bu indexler aynı order için ikinci reservation yazılmasını engeller.
        // Ayrıca reservationId'nin tekil kalmasını ve expire taramalarının hızlı çalışmasını sağlar.
        await collection.Indexes.CreateManyAsync(indexModels, cancellationToken);
    }
}
