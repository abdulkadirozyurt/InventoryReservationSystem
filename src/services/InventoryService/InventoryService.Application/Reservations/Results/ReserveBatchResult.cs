namespace InventoryService.Application.Reservations.Results;

public sealed record ReserveBatchResult(
    bool Success,
    string? ReservationId,
    IReadOnlyCollection<ReserveBatchFailure> Failures);