namespace InventoryService.Application.Inventory.Results;

public sealed record RebalanceWarehouseResult(
    bool Success,
    string? ErrorCode,
    string? ErrorMessage,
    string Sku = "",
    string SourceWarehouseId = "",
    string TargetWarehouseId = "",
    int SourceAvailableStock = 0,
    int TargetAvailableStock = 0);
