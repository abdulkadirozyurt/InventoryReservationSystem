namespace InventoryService.Application.Reservations.Results.Confirm;

public sealed record ConfirmReservationResult(
    bool Success,
    string? ErrorCode,
    string? ErrorMessage);
