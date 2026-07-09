namespace InventoryService.Application.Reservations.Commands.ReleaseBatch;

public sealed record ReleaseBatchCommand(
    string ReservationId,
    IReadOnlyCollection<ReleaseBatchItemCommand> Items,
    string CorrelationId,
    // IsExpiry=true => background expiry job çağırdı, reservation Expired state'e geçer, audit "Expired" yazılır.
    // IsExpiry=false => normal client release (cancel/manual), reservation Released state'e geçer, audit "Released" yazılır.
    bool IsExpiry = false);

public sealed record ReleaseBatchItemCommand(
    string Sku,
    string WarehouseId,
    int Quantity);