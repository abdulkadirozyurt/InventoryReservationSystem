namespace InventoryService.Application.Reservations.Commands.ConfirmReservation;

public sealed record ConfirmReservationCommand(
    string ReservationId,
    string CorrelationId);
