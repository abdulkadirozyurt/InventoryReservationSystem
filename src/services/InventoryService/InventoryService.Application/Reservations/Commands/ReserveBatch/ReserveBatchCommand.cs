namespace InventoryService.Application.Reservations.Commands.ReserveBatch;

public sealed record ReserveBatchCommand(
    string OrderId,
    IReadOnlyCollection<ReserveBatchItemCommand> Items,
    string CorrelationId,
    bool EnableFallback = false);
