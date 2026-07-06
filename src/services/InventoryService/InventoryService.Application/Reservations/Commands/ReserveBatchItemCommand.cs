namespace InventoryService.Application.Reservations.Commands;

public sealed record ReserveBatchItemCommand(string Sku, string WarehouseId, int Quantity);