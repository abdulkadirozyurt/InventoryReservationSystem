namespace InventoryService.Application.Reservations.Commands.ReserveBatch;

public sealed record ReserveBatchItemCommand(string Sku, string WarehouseId, int Quantity);