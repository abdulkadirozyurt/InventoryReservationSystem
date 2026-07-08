namespace InventoryService.Application.Reservations.Commands.Reserve;

public sealed record ReserveBatchItemCommand(string Sku, string WarehouseId, int Quantity);