namespace InventoryService.Application.Inventory.Results;

public sealed record InventoryItemStockResult(
    string Sku,
    string WarehouseId,
    int QuantityAvailable,
    int QuantityReserved);
