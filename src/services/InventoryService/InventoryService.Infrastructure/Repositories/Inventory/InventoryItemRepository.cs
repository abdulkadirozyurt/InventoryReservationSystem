using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Inventory.Exceptions;
using InventoryService.Domain.Inventory;
using InventoryService.Infrastructure.Mongo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace InventoryService.Infrastructure.Repositories.Inventory;

public sealed class InventoryItemRepository(
    IMongoDatabase database,
    IMongoSessionProvider mongoSessionProvider,
    IOptions<MongoDbOptions> options,
    ILogger<IInventoryItemRepository> logger) : IInventoryItemRepository
{
    private readonly IMongoCollection<InventoryItem> _collection = database.GetCollection<InventoryItem>(options.Value.InventoryItemsCollectionName);

    public async Task<InventoryItem?> GetBySkuAndWarehouseAsync(string sku, string warehouseId, CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = Builders<InventoryItem>.Filter.And(
                Builders<InventoryItem>.Filter.Eq(item => item.Sku, sku),
                Builders<InventoryItem>.Filter.Eq(item => item.WarehouseId, warehouseId));

            var session = mongoSessionProvider.CurrentSession;
            if (session is not null)
                return await _collection.Find(session, filter).FirstOrDefaultAsync(cancellationToken);

            return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while getting inventory item by SKU and warehouse. Sku: {Sku}, WarehouseId: {WarehouseId}, ErrorCategory: {ErrorCategory}",
                sku,
                warehouseId,
                "TransientMongoError");
            throw new InventoryStoreUnavailableException("Inventory store is unavailable while querying stock", exception);
        }
    }

    public async Task<IReadOnlyList<InventoryItem>> GetBySkuAsync(string sku, CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = Builders<InventoryItem>.Filter.Eq(item => item.Sku, sku);
            var session = mongoSessionProvider.CurrentSession;

            if (session is not null)
                return await _collection.Find(session, filter).ToListAsync(cancellationToken);

            return await _collection.Find(filter).ToListAsync(cancellationToken);
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while getting inventory item by SKU and warehouse. Sku: {Sku}, ErrorCategory: {ErrorCategory}",
                sku,
                "TransientMongoError");
            throw new InventoryStoreUnavailableException("Inventory store is unavailable while querying stock", exception);
        }
    }

    public async Task UpdateAsync(InventoryItem inventoryItem, CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = Builders<InventoryItem>.Filter.And(
                Builders<InventoryItem>.Filter.Eq(item => item.Sku, inventoryItem.Sku),
                Builders<InventoryItem>.Filter.Eq(item => item.WarehouseId, inventoryItem.WarehouseId));

            var session = mongoSessionProvider.CurrentSession;
            if (session is not null)
            {
                await _collection.ReplaceOneAsync(
                    session,
                    filter,
                    inventoryItem,
                    cancellationToken: cancellationToken);

                return;
            }

            await _collection.ReplaceOneAsync(filter, inventoryItem, cancellationToken: cancellationToken);
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while updating inventory item. Sku: {Sku}, WarehouseId: {WarehouseId}, ErrorCategory: {ErrorCategory}",
                inventoryItem.Sku,
                inventoryItem.WarehouseId,
                "TransientMongoError");
            throw new InventoryStoreUnavailableException("Inventory store is unavailable while updating stock", exception);
        }
    }

    public async Task AddAsync(InventoryItem inventoryItem, CancellationToken cancellationToken = default)
    {
        try
        {
            var session = mongoSessionProvider.CurrentSession;
            if (session is not null)
            {
                await _collection.InsertOneAsync(session, inventoryItem, cancellationToken: cancellationToken);
                return;
            }

            await _collection.InsertOneAsync(inventoryItem, cancellationToken: cancellationToken);
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while adding inventory item. Sku: {Sku}, WarehouseId: {WarehouseId}, ErrorCategory: {ErrorCategory}",
                inventoryItem.Sku,
                inventoryItem.WarehouseId,
                "TransientMongoError");
            throw new InventoryStoreUnavailableException("Inventory store is unavailable while adding stock", exception);
        }
    }

    public async Task<IReadOnlyList<InventoryItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var session = mongoSessionProvider.CurrentSession;
            if (session is not null)
            {
                return await _collection.Find(session, Builders<InventoryItem>.Filter.Empty).ToListAsync(cancellationToken);
            }

            return await _collection.Find(Builders<InventoryItem>.Filter.Empty).ToListAsync(cancellationToken);
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while getting all inventory items. ErrorCategory: {ErrorCategory}",
                "TransientMongoError");
            throw new InventoryStoreUnavailableException("Inventory store is unavailable while querying all stock", exception);
        }
    }

    public async Task<IReadOnlyDictionary<(string Sku, string WarehouseId), int>> GetReservedQuantitySnapshotAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Only items with actual reserved stock can create inventory-side drift, so zero-reserved rows are ignored for a small read-only snapshot.
            var filter = Builders<InventoryItem>.Filter.Gt(item => item.QuantityReserved, 0);

            List<InventoryItem> items;
            var session = mongoSessionProvider.CurrentSession;
            if (session is not null)
            {
                items = await _collection.Find(session, filter).ToListAsync(cancellationToken);
            }
            else
            {
                items = await _collection.Find(filter).ToListAsync(cancellationToken);
            }

            var result = items.ToDictionary(
                item => (item.Sku, item.WarehouseId),
                item => item.QuantityReserved
            );

            return result;
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while getting reserved quantity snapshot. ErrorCategory: {ErrorCategory}",
                "TransientMongoError");

            throw new InventoryStoreUnavailableException("Inventory store is unavailable while retrieving reserved quantity snapshot", exception);
        }
    }
}
