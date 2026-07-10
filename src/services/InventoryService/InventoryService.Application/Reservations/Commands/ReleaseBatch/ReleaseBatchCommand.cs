namespace InventoryService.Application.Reservations.Commands.ReleaseBatch;

public sealed record ReleaseBatchCommand(
    string ReservationId,
    IReadOnlyCollection<ReleaseBatchItemCommand> Items,
    string CorrelationId,
    // IsExpiry=true => background expiry job çağırdı, reservation Expired state'e geçer, audit "Expired" yazılır.
    // IsExpiry=false => normal client/admin release, reservation Released state'e geçer.
    // Admin override reason/requestedBy audit reason içine taşınır; stok doğrudan elle mutate edilmez.
    bool IsExpiry = false,
    string? Reason = null,
    string? RequestedBy = null);

public sealed record ReleaseBatchItemCommand(
    string Sku,
    string WarehouseId,
    int Quantity);