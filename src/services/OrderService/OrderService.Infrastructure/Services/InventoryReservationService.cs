using InventoryReservationSystem.Contracts.Inventory;
using OrderService.Application.Orders.Abstractions;

namespace OrderService.Infrastructure.Services;

public sealed class InventoryReservationService(
    InventoryReservations.InventoryReservationsClient client) : IInventoryReservationService
{
    public async Task<InventoryReservationResult> ReserveBatchAsync(
        string orderId,
        IReadOnlyCollection<InventoryReservationItem> items,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var request = new ReserveBatchRequest
        {
            Metadata = new RequestMetadata { CorrelationId = correlationId },
            OrderId = orderId
        };

        request.Items.AddRange(items.Select(item => new ReservationItem
        {
            Sku = item.Sku,
            WarehouseId = item.WarehouseId,
            Quantity = item.Quantity
        }));

        var response = await client.ReserveBatchAsync(request, cancellationToken: cancellationToken);

        return new InventoryReservationResult(
            response.Success,
            response.ReservationId,
            response.Failures.Select(failure => new InventoryReservationFailure(
                failure.Sku,
                failure.WarehouseId,
                failure.ErrorCode,
                failure.Reason)).ToArray());
    }

    public async Task<InventoryReservationOperationResult> ReleaseBatchAsync(
        string reservationId,
        IReadOnlyCollection<InventoryReservationItem> items,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var request = new ReleaseBatchRequest
        {
            Metadata = new RequestMetadata { CorrelationId = correlationId },
            ReservationId = reservationId
        };

        request.Items.AddRange(items.Select(item => new ReservationItem
        {
            Sku = item.Sku,
            WarehouseId = item.WarehouseId,
            Quantity = item.Quantity
        }));

        var response = await client.ReleaseBatchAsync(request, cancellationToken: cancellationToken);
        return new InventoryReservationOperationResult(response.Success, response.ErrorCode, response.ErrorMessage);
    }

    public async Task<InventoryReservationOperationResult> ConfirmReservationAsync(
        string reservationId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var request = new ConfirmReservationRequest
        {
            Metadata = new RequestMetadata { CorrelationId = correlationId },
            ReservationId = reservationId
        };

        var response = await client.ConfirmReservationAsync(request, cancellationToken: cancellationToken);
        return new InventoryReservationOperationResult(response.Success, response.ErrorCode, response.ErrorMessage);
    }
}
