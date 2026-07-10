using InventoryService.Infrastructure.CollectionInitializers.SeedData;
using InventoryService.Infrastructure.Mongo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace InventoryService.Infrastructure.CollectionInitializers;

public sealed class InventorySeedDataService(
    IMongoDatabase database,
    IOptions<MongoDbOptions> mongoOptions,
    ILogger<InventorySeedDataService> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedInventoryItemsAsync(cancellationToken);
        await SeedReservationsAsync(cancellationToken);
        await SeedInventoryTransactionsAsync(cancellationToken);
    }

    private async Task SeedInventoryItemsAsync(CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<BsonDocument>(mongoOptions.Value.InventoryItemsCollectionName);
        var now = DateTime.UtcNow;
        var insertedCount = 0;

        foreach (var item in InventorySeedCatalog.InventoryItems)
        {
            var filter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("sku", item.Sku),
                Builders<BsonDocument>.Filter.Eq("warehouseId", item.WarehouseId));

            var update = Builders<BsonDocument>.Update
                .SetOnInsert("sku", item.Sku)
                .SetOnInsert("warehouseId", item.WarehouseId)
                .SetOnInsert("quantityAvailable", item.QuantityAvailable)
                .SetOnInsert("quantityReserved", item.QuantityReserved)
                .SetOnInsert("createdAt", now)
                .SetOnInsert("updatedAt", now);

            var result = await collection.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = true },
                cancellationToken);

            if (result.UpsertedId is not null)
            {
                insertedCount++;
            }
        }

        logger.LogInformation(
            "Seed data inventory item initialization completed. InsertedCount: {InsertedCount}, TotalSeedCount: {TotalSeedCount}",
            insertedCount,
            InventorySeedCatalog.InventoryItems.Count);
    }

    private async Task SeedReservationsAsync(CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<BsonDocument>(mongoOptions.Value.ReservationsCollectionName);
        var now = DateTime.UtcNow;
        var insertedCount = 0;

        foreach (var reservation in InventorySeedCatalog.Reservations)
        {
            var filter = Builders<BsonDocument>.Filter.Eq("reservationId", reservation.ReservationId);
            var itemDocuments = new BsonArray(reservation.Items.Select(item => new BsonDocument
            {
                { "Sku", item.Sku },
                { "WarehouseId", item.WarehouseId },
                { "Quantity", item.Quantity }
            }));

            var update = Builders<BsonDocument>.Update
                .SetOnInsert("reservationId", reservation.ReservationId)
                .SetOnInsert("orderId", reservation.OrderId)
                .SetOnInsert("createdAt", now)
                .Set("items", itemDocuments)
                .Set("status", reservation.Status)
                .Set("expiresAt", now.Add(reservation.ExpiresIn))
                .Set("updatedAt", now);

            var result = await collection.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = true },
                cancellationToken);

            if (result.UpsertedId is not null)
            {
                insertedCount++;
            }
        }

        logger.LogInformation(
            "Seed data reservation initialization completed. InsertedCount: {InsertedCount}, TotalSeedCount: {TotalSeedCount}",
            insertedCount,
            InventorySeedCatalog.Reservations.Count);
    }

    private async Task SeedInventoryTransactionsAsync(CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<BsonDocument>(mongoOptions.Value.InventoryTransactionsCollectionName);
        var now = DateTime.UtcNow;
        var insertedCount = 0;

        foreach (var transaction in InventorySeedCatalog.InventoryTransactions)
        {
            var filter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("correlationId", transaction.CorrelationId),
                Builders<BsonDocument>.Filter.Eq("sku", transaction.Sku),
                Builders<BsonDocument>.Filter.Eq("warehouseId", transaction.WarehouseId),
                Builders<BsonDocument>.Filter.Eq("type", transaction.Type));

            var update = Builders<BsonDocument>.Update
                .SetOnInsert("sku", transaction.Sku)
                .SetOnInsert("warehouseId", transaction.WarehouseId)
                .SetOnInsert("type", transaction.Type)
                .SetOnInsert("quantityAvailableDelta", transaction.QuantityAvailableDelta)
                .SetOnInsert("quantityReservedDelta", transaction.QuantityReservedDelta)
                .SetOnInsert("correlationId", transaction.CorrelationId)
                .SetOnInsert("createdAt", now);

            if (!string.IsNullOrWhiteSpace(transaction.ReservationId))
            {
                update = update.SetOnInsert("reservationId", transaction.ReservationId);
            }

            if (!string.IsNullOrWhiteSpace(transaction.OrderId))
            {
                update = update.SetOnInsert("orderId", transaction.OrderId);
            }

            if (!string.IsNullOrWhiteSpace(transaction.Reason))
            {
                update = update.SetOnInsert("reason", transaction.Reason);
            }

            var result = await collection.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = true },
                cancellationToken);

            if (result.UpsertedId is not null)
            {
                insertedCount++;
            }
        }

        logger.LogInformation(
            "Seed data inventory transaction initialization completed. InsertedCount: {InsertedCount}, TotalSeedCount: {TotalSeedCount}",
            insertedCount,
            InventorySeedCatalog.InventoryTransactions.Count);
    }
}
