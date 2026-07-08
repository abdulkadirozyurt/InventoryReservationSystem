namespace InventoryService.Application.Reservations.Commands.Reserve;

public sealed record ReserveBatchCommand(
    string OrderId,
    IReadOnlyCollection<ReserveBatchItemCommand> Items,
    string CorrelationId);
