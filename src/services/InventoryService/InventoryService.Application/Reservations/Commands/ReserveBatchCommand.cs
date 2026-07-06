namespace InventoryService.Application.Reservations.Commands;

public sealed record ReserveBatchCommand(
    string OrderId,
    IReadOnlyCollection<ReserveBatchItemCommand> Items,
    string CorrelationId);
