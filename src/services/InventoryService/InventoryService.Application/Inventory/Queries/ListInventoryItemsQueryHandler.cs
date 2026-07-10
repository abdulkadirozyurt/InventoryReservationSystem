using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Inventory.Exceptions;
using InventoryService.Application.Inventory.Results;
using Microsoft.Extensions.Logging;

namespace InventoryService.Application.Inventory.Queries;

public sealed class ListInventoryItemsQueryHandler(
    IInventoryItemRepository inventoryItemRepository,
    ILogger<ListInventoryItemsQueryHandler> logger)
{
    public async Task<IReadOnlyList<InventoryItemStockResult>> HandleAsync(
        ListInventoryItemsQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var items = await inventoryItemRepository.GetAllAsync(cancellationToken);
            var filtered = items.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(query.Sku))
            {
                filtered = filtered.Where(item => string.Equals(item.Sku, query.Sku.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(query.WarehouseId))
            {
                filtered = filtered.Where(item => string.Equals(item.WarehouseId, query.WarehouseId.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var search = query.Search.Trim();
                filtered = filtered.Where(item =>
                    item.Sku.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || item.WarehouseId.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            var result = filtered
                .OrderBy(item => item.Sku, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.WarehouseId, StringComparer.OrdinalIgnoreCase)
                .Select(item => new InventoryItemStockResult(
                    item.Sku,
                    item.WarehouseId,
                    item.QuantityAvailable,
                    item.QuantityReserved))
                .ToArray();

            logger.LogInformation(
                "Inventory catalogue query completed. CorrelationId: {CorrelationId}, Search: {Search}, Sku: {Sku}, WarehouseId: {WarehouseId}, ResultCount: {ResultCount}",
                query.CorrelationId,
                query.Search,
                query.Sku,
                query.WarehouseId,
                result.Length);

            return result;
        }
        catch (InventoryStoreUnavailableException)
        {
            throw;
        }
    }
}
