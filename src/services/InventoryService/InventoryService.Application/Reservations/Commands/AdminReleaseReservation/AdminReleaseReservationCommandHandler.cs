using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Application.Reservations.Commands.ReleaseBatch;
using InventoryService.Application.Reservations.Results.Release;
using InventoryService.Domain.Reservations;
using Microsoft.Extensions.Logging;

namespace InventoryService.Application.Reservations.Commands.AdminReleaseReservation;

public sealed class AdminReleaseReservationCommandHandler(
    IReservationRepository reservationRepository,
    ReleaseBatchCommandHandler releaseBatchCommandHandler,
    ILogger<AdminReleaseReservationCommandHandler> logger)
{
    private const string ValidationFailure = "VALIDATION_ERROR";
    private const string ReservationNotFound = "RESERVATION_NOT_FOUND";
    private const string InvalidReservationState = "INVALID_RESERVATION_STATE";

    public async Task<ReleaseBatchResult> HandleAsync(AdminReleaseReservationCommand command, CancellationToken cancellationToken)
    {
        // 1. Girdi doğrulama: Reason ve RequestedBy boş olamaz.
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            logger.LogWarning("Admin release failed: Reason is empty. CorrelationId: {CorrelationId}", command.CorrelationId);
            return new ReleaseBatchResult(false, ValidationFailure, "Reason is required to perform admin release override.");
        }

        if (string.IsNullOrWhiteSpace(command.RequestedBy))
        {
            logger.LogWarning("Admin release failed: RequestedBy is empty. CorrelationId: {CorrelationId}", command.CorrelationId);
            return new ReleaseBatchResult(false, ValidationFailure, "RequestedBy is required to perform admin release override.");
        }

        if (string.IsNullOrWhiteSpace(command.ReservationId))
        {
            logger.LogWarning("Admin release failed: ReservationId is empty. CorrelationId: {CorrelationId}", command.CorrelationId);
            return new ReleaseBatchResult(false, ValidationFailure, "ReservationId is required.");
        }

        // 2. Rezervasyonun varlığı kontrol edilir.
        var reservation = await reservationRepository.GetByReservationIdAsync(command.ReservationId, cancellationToken);
        if (reservation is null)
        {
            logger.LogWarning("Admin release failed: Reservation not found. ReservationId: {ReservationId}, CorrelationId: {CorrelationId}", command.ReservationId, command.CorrelationId);
            return new ReleaseBatchResult(false, ReservationNotFound, $"Reservation with ID {command.ReservationId} was not found.");
        }

        // 3. Idempotency ve Güvenlik: Rezervasyon zaten serbest bırakılmış veya süresi dolmuşsa no-op olarak başarılı dönülür.
        if (reservation.Status is ReservationStatus.Released or ReservationStatus.Expired)
        {
            logger.LogInformation("Admin release skipped (idempotent / no-op): Reservation already in terminal release/expired state. ReservationId: {ReservationId}, Status: {Status}, CorrelationId: {CorrelationId}",
                command.ReservationId, reservation.Status, command.CorrelationId);
            return new ReleaseBatchResult(true, null, null);
        }

        // 4. Durum Kontrolü: Rezervasyon Pending değilse (örn. Confirmed), geçersiz durum hatası fırlatılır.
        if (reservation.Status != ReservationStatus.Pending)
        {
            logger.LogWarning("Admin release failed: Invalid state. ReservationId: {ReservationId}, Status: {Status}, CorrelationId: {CorrelationId}",
                command.ReservationId, reservation.Status, command.CorrelationId);
            return new ReleaseBatchResult(false, InvalidReservationState, $"Only pending reservations can be released, current status: {reservation.Status}.");
        }

        // 5. Kod Tekrarını Önleme: Mevcut ReleaseBatchCommandHandler kullanılarak envanter iadeleri ve loglamalar gerçekleştirilir.
        var releaseItems = reservation.Items
            .Select(item => new ReleaseBatchItemCommand(item.Sku, item.WarehouseId, item.Quantity))
            .ToList();

        var releaseBatchCommand = new ReleaseBatchCommand(
            ReservationId: command.ReservationId,
            Items: releaseItems,
            CorrelationId: command.CorrelationId,
            IsExpiry: false,
            Reason: command.Reason,
            RequestedBy: command.RequestedBy
        );

        logger.LogInformation("Delegating admin release to ReleaseBatchCommandHandler. ReservationId: {ReservationId}, RequestedBy: {RequestedBy}, CorrelationId: {CorrelationId}",
            command.ReservationId, command.RequestedBy, command.CorrelationId);

        return await releaseBatchCommandHandler.HandleAsync(releaseBatchCommand, cancellationToken);
    }
}
