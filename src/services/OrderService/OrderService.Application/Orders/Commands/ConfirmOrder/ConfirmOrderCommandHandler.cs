using Microsoft.Extensions.Logging;
using OrderService.Application.Orders.Abstractions;
using OrderService.Application.Orders.Results;
using OrderService.Domain.Orders;

namespace OrderService.Application.Orders.Commands.ConfirmOrder;

public sealed class ConfirmOrderCommandHandler(
    IOrderRepository orderRepository,
    IOrderHistoryRepository orderHistoryRepository,
    IOrderUnitOfWork unitOfWork,
    IInventoryReservationService inventoryReservationService,
    ILogger<ConfirmOrderCommandHandler> logger)
{
    public async Task<OrderOperationResult> HandleAsync(ConfirmOrderCommand command, CancellationToken cancellationToken = default)
    {
        var order = await orderRepository.GetByOrderNumberAsync(command.OrderNumber, cancellationToken);

        if (order is null)
            return new OrderOperationResult(command.OrderNumber, false, "OrderNotFound", "Order was not found.");

        if (order.Status != OrderStatus.Pending)
            return new OrderOperationResult(order.OrderNumber, false, "InvalidOrderStatus", $"Order status must be Pending. Current status: {order.Status}.");

        if (string.IsNullOrWhiteSpace(order.ReservationId))
            return new OrderOperationResult(order.OrderNumber, false, "MissingReservationId", "Order does not have a reservation id.");

        var confirmResult = await inventoryReservationService.ConfirmReservationAsync(
            order.ReservationId,
            command.CorrelationId,
            cancellationToken);

        logger.LogInformation(
            "Inventory reservation confirm completed. OrderNumber: {OrderNumber}, ReservationId: {ReservationId}, Success: {Success}, CorrelationId: {CorrelationId}",
            order.OrderNumber,
            order.ReservationId,
            confirmResult.Success,
            command.CorrelationId);

        if (!confirmResult.Success)
            return new OrderOperationResult(order.OrderNumber, false, confirmResult.ErrorCode, confirmResult.ErrorMessage);

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var previousStatus = order.Status;
            order.Confirm();

            await orderRepository.UpdateAsync(order, ct);

            var history = new OrderHistory(
                order.OrderNumber,
                previousStatus,
                order.Status,
                command.CorrelationId,
                "Order confirmed.");

            await orderHistoryRepository.AddAsync(history, ct);
        }, cancellationToken);

        return new OrderOperationResult(order.OrderNumber, true);
    }
}
