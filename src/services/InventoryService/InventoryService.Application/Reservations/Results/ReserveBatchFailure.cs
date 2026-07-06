namespace InventoryService.Application.Reservations.Results;

public sealed record ReserveBatchFailure(string Sku,
    string WarehouseId,
    string ErrorCode,
    string Reason);