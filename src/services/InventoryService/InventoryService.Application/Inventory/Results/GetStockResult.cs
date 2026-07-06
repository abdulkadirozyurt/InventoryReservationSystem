namespace InventoryService.Application.Inventory.Results;

public sealed record GetStockResult(
    string Sku,
    string? WarehouseId,
    int QuantityAvailable,
    int QuantityReserved,
    bool Found,
    string? ErrorCode = null,
    string? ErrorMessage = null);