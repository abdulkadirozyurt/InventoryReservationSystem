namespace InventoryService.Application.Inventory.Results;

public sealed record StockAdjustmentResult(
    bool Success,
    string? ErrorCode,
    string? ErrorMessage,
    string Sku,
    string WarehouseId,
    int QuantityAvailable,
    int QuantityReserved);
