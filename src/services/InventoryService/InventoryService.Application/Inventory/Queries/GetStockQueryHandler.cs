using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Inventory.Exceptions;
using InventoryService.Application.Inventory.Results;
using Microsoft.Extensions.Logging;

namespace InventoryService.Application.Inventory.Queries;

public sealed class GetStockQueryHandler(
    IInventoryItemRepository inventoryItemRepository,
    ILogger<GetStockQueryHandler> logger)
{
    private const string InvalidRequest = "INVALID_REQUEST";
    private const string StockNotFound = "STOCK_NOT_FOUND";
    private const string InventoryStoreUnavailable = "INVENTORY_STORE_UNAVAILABLE";

    public async Task<GetStockResult> HandleAsync(GetStockQuery query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.Sku))
        {
            logger.LogWarning("Get stock request rejected because SKU is empty. CorrelationId: {CorrelationId}", query.CorrelationId);

            return new GetStockResult(
                query.Sku,
                query.WarehouseId,
                0,
                0,
                false,
                InvalidRequest,
                "SKU is required."
            );
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(query.WarehouseId))
            {
                var inventoryItem = await inventoryItemRepository.GetBySkuAndWarehouseAsync(query.Sku, query.WarehouseId, cancellationToken);

                if (inventoryItem is null)
                {
                    logger.LogInformation(
                        "Stock was not found for SKU and warehouse. CorrelationId: {CorrelationId}, Sku: {Sku}, WarehouseId: {WarehouseId}",
                        query.CorrelationId,
                        query.Sku,
                        query.WarehouseId);

                    return new GetStockResult(
                        query.Sku,
                        query.WarehouseId,
                        0,
                        0,
                        false,
                        StockNotFound,
                        "Stock was not found for the specified SKU and warehouse.");
                }

                logger.LogInformation(
                    "Stock was found for SKU and warehouse. CorrelationId: {CorrelationId}, Sku: {Sku}, WarehouseId: {WarehouseId}",
                    query.CorrelationId,
                    query.Sku,
                    query.WarehouseId);

                return new GetStockResult(
                    inventoryItem.Sku,
                    inventoryItem.WarehouseId,
                    inventoryItem.QuantityAvailable,
                    inventoryItem.QuantityReserved,
                    Found: true);
            }

            var inventoryItems = await inventoryItemRepository.GetBySkuAsync(query.Sku, cancellationToken);

            if (inventoryItems.Count == 0)
            {
                logger.LogInformation("Stock was not found for SKU. CorrelationId: {CorrelationId}, Sku: {Sku}", query.CorrelationId, query.Sku);

                return new GetStockResult(
                   query.Sku,
                   null,
                   0,
                   0,
                   false,
                   StockNotFound,
                   "Stock was not found for the specified SKU.");
            }

            var quantityAvailable = inventoryItems.Sum(item => item.QuantityAvailable);
            var quantityReserved = inventoryItems.Sum(item => item.QuantityReserved);

            logger.LogInformation(
               "Aggregated stock was found for SKU. CorrelationId: {CorrelationId}, Sku: {Sku}, WarehouseCount: {WarehouseCount}",
               query.CorrelationId,
               query.Sku,
               inventoryItems.Count);

            return new GetStockResult(
                query.Sku,
                null,
                quantityAvailable,
                quantityReserved,
                true);
        }
        catch (InventoryStoreUnavailableException)
        {
            return new GetStockResult(
                query.Sku,
                query.WarehouseId,
                0,
                0,
                false,
                InventoryStoreUnavailable,
                "Inventory store is unavailable. ");
        }
    }
}