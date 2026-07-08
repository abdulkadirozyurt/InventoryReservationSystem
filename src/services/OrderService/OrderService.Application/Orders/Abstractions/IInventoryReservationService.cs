namespace OrderService.Application.Orders.Abstractions;

public interface IInventoryReservationService
{
    Task<InventoryReservationResult> ReserveBatchAsync(
        string orderId,
        IReadOnlyCollection<InventoryReservationItem> items,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<InventoryReservationOperationResult> ReleaseBatchAsync(
        string reservationId,
        IReadOnlyCollection<InventoryReservationItem> items,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<InventoryReservationOperationResult> ConfirmReservationAsync(
        string reservationId,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public sealed record InventoryReservationItem(string Sku, string WarehouseId, int Quantity);

public sealed record InventoryReservationResult(
    bool Success,
    string? ReservationId,
    IReadOnlyCollection<InventoryReservationFailure> Failures);

public sealed record InventoryReservationFailure(string Sku, string WarehouseId, string ErrorCode, string Reason);

public sealed record InventoryReservationOperationResult(bool Success, string? ErrorCode, string? ErrorMessage);
