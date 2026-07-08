namespace InventoryService.Application.Reservations.Results.Reserve;

public sealed record ReserveBatchResult(
    bool Success,
    string? ReservationId,
    IReadOnlyCollection<ReserveBatchFailure> Failures);