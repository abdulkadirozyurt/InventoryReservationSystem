namespace InventoryService.Application.Inventory.Commands.RebalanceWarehouse;

public sealed record RebalanceWarehouseCommand(
    string Sku,
    string SourceWarehouseId,
    string TargetWarehouseId,
    int Quantity,
    string Reason,
    string CorrelationId);
