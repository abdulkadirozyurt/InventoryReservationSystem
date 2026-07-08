namespace InventoryService.Application.Inventory.Commands.DecreaseStock;

public sealed record DecreaseStockCommand(
    string Sku,
    string WarehouseId,
    int Quantity,
    string Reason,
    string CorrelationId);
