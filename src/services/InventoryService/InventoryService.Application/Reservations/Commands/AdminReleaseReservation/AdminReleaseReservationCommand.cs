namespace InventoryService.Application.Reservations.Commands.AdminReleaseReservation;

public sealed record AdminReleaseReservationCommand(
    string ReservationId,
    string Reason,
    string RequestedBy,
    string CorrelationId);
