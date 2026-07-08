using Microsoft.Extensions.Logging;
using OrderService.Application.Orders.Abstractions;
using OrderService.Application.Orders.Results;
using OrderService.Domain.Orders;

namespace OrderService.Application.Orders.Commands.CancelOrder;

public sealed class CancelOrderCommandHandler(
    IOrderRepository orderRepository,
    IOrderHistoryRepository orderHistoryRepository,
    IOrderUnitOfWork unitOfWork,
    IInventoryReservationService inventoryReservationService,
    ILogger<CancelOrderCommandHandler> logger)
{
    public async Task<OrderOperationResult> HandleAsync(CancelOrderCommand command, CancellationToken cancellationToken = default)
    {
        var order = await orderRepository.GetByOrderNumberAsync(command.OrderNumber, cancellationToken);

        if (order is null)
            return new OrderOperationResult(command.OrderNumber, false, "OrderNotFound", "Order was not found.");

        if (order.Status != OrderStatus.Pending)
            return new OrderOperationResult(order.OrderNumber, false, "InvalidOrderStatus", $"Order status must be Pending. Current status: {order.Status}.");

        if (string.IsNullOrWhiteSpace(order.ReservationId))
            return new OrderOperationResult(order.OrderNumber, false, "MissingReservationId", "Order does not have a reservation id.");

        var reservationItems = order.Items
            .Select(item => new InventoryReservationItem(item.Sku, item.WarehouseId, item.ReservedQuantity))
            .ToArray();

        var releaseResult = await inventoryReservationService.ReleaseBatchAsync(
            order.ReservationId,
            reservationItems,
            command.CorrelationId,
            cancellationToken);

        logger.LogInformation(
            "Inventory reservation release completed. OrderNumber: {OrderNumber}, ReservationId: {ReservationId}, Success: {Success}, CorrelationId: {CorrelationId}",
            order.OrderNumber,
            order.ReservationId,
            releaseResult.Success,
            command.CorrelationId);

        if (!releaseResult.Success)
            return new OrderOperationResult(order.OrderNumber, false, releaseResult.ErrorCode, releaseResult.ErrorMessage);

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var previousStatus = order.Status;
            order.Cancel();

            await orderRepository.UpdateAsync(order, ct);

            var history = new OrderHistory(
                order.OrderNumber,
                previousStatus,
                order.Status,
                command.CorrelationId,
                command.Reason ?? "Order cancelled.");

            await orderHistoryRepository.AddAsync(history, ct);
        }, cancellationToken);

        return new OrderOperationResult(order.OrderNumber, true);
    }
}
