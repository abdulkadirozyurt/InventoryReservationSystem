using Grpc.Core;
using InventoryReservationSystem.Contracts.Inventory;
using Microsoft.Extensions.Logging;
using OrderService.Application.Orders.Abstractions;

namespace OrderService.Infrastructure.Services;

public sealed class InventoryReservationService(
    InventoryReservations.InventoryReservationsClient client,
    ILogger<InventoryReservationService> logger) : IInventoryReservationService
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

        try
        {
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
        catch (RpcException exception)
        {
            logger.LogError(
                exception,
                "Reserve batch gRPC call failed. OrderId: {OrderId}, CorrelationId: {CorrelationId}, StatusCode: {StatusCode}",
                orderId,
                correlationId,
                exception.StatusCode);

            return new InventoryReservationResult(
                false,
                null,
                items.Select(item => new InventoryReservationFailure(
                    item.Sku,
                    item.WarehouseId,
                    "INVENTORY_SERVICE_UNAVAILABLE",
                    exception.Status.Detail)).ToArray());
        }
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

        try
        {
            var response = await client.ReleaseBatchAsync(request, cancellationToken: cancellationToken);
            return new InventoryReservationOperationResult(response.Success, response.ErrorCode, response.ErrorMessage);
        }
        catch (RpcException exception)
        {
            logger.LogError(
                exception,
                "Release batch gRPC call failed. ReservationId: {ReservationId}, CorrelationId: {CorrelationId}, StatusCode: {StatusCode}",
                reservationId,
                correlationId,
                exception.StatusCode);

            return new InventoryReservationOperationResult(false, "INVENTORY_SERVICE_UNAVAILABLE", exception.Status.Detail);
        }
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

        try
        {
            var response = await client.ConfirmReservationAsync(request, cancellationToken: cancellationToken);
            return new InventoryReservationOperationResult(response.Success, response.ErrorCode, response.ErrorMessage);
        }
        catch (RpcException exception)
        {
            logger.LogError(
                exception,
                "Confirm reservation gRPC call failed. ReservationId: {ReservationId}, CorrelationId: {CorrelationId}, StatusCode: {StatusCode}",
                reservationId,
                correlationId,
                exception.StatusCode);

            return new InventoryReservationOperationResult(false, "INVENTORY_SERVICE_UNAVAILABLE", exception.Status.Detail);
        }
    }
}
