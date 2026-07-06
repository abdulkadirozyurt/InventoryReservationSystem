using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Inventory.Exceptions;
using InventoryService.Domain.Inventory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace InventoryService.Infrastructure.Mongo;

public sealed class InventoryItemRepository(
    IMongoDatabase database,
    IOptions<MongoDbOptions> options,
    ILogger<IInventoryItemRepository> logger) : IInventoryItemRepository
{
    private readonly IMongoCollection<InventoryItem> _collection = database.GetCollection<InventoryItem>(options.Value.InventoryItemsCollectionName);
    public async Task<InventoryItem?> GetBySkuAndWarehouseAsync(string sku, string warehouseId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _collection.Find(item => item.Sku == sku && item.WarehouseId == warehouseId).FirstOrDefaultAsync(cancellationToken);
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while getting inventory item by SKU and warehouse. Sku: {Sku}, WarehouseId: {WarehouseId}, ErrorCategory: {ErrorCategory}",
                sku,
                warehouseId,
                "TransientMongoError");
            throw;
        }
    }

    public async Task<IReadOnlyList<InventoryItem>> GetBySkuAsync(string sku, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _collection.Find(item => item.Sku == sku).ToListAsync(cancellationToken);
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
}
