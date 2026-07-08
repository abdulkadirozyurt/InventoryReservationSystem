namespace InventoryService.Application.Reservations.Commands.ReleaseBatch;

public sealed record ReleaseBatchCommand(
    string ReservationId,
    IReadOnlyCollection<ReleaseBatchItemCommand> Items,
    string CorrelationId);

public sealed record ReleaseBatchItemCommand(
    string Sku,
    string WarehouseId,
    int Quantity);