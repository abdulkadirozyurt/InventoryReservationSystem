namespace InventoryService.Application.Inventory.Commands.IncreaseStock;

public sealed record IncreaseStockCommand(
    string Sku,
    string WarehouseId,
    int Quantity,
    string Reason,
    string CorrelationId);
