namespace InventoryService.Application.Reservations.Results.Reserve;

public sealed record ReserveBatchFailure(
    string Sku,
    string WarehouseId,
    string ErrorCode,
    string Reason);