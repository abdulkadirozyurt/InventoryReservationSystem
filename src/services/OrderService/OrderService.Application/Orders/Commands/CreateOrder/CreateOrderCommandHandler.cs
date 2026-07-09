using Microsoft.Extensions.Logging;
using OrderService.Application.Orders.Abstractions;
using OrderService.Domain.Orders;

namespace OrderService.Application.Orders.Commands.CreateOrder;

public sealed class CreateOrderCommandHandler(
    IOrderRepository orderRepository,
    IOrderHistoryRepository orderHistoryRepository,
    IOrderUnitOfWork unitOfWork,
    IInventoryReservationService inventoryReservationService,
    ILogger<CreateOrderCommandHandler> logger)
{
    public async Task<CreateOrderResult> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
    {
        var orderNumber = command.OrderNumber;

        logger.LogInformation(
            "Creating order. OrderNumber: {OrderNumber}, CorrelationId: {CorrelationId}",
            orderNumber,
            command.CorrelationId);

        var existingOrder = await orderRepository.GetByOrderNumberAsync(orderNumber, cancellationToken);

        if (existingOrder is not null)
        {
            // Aynı idempotency operation tekrar çalışırsa aynı order number üretilir.
            // Order daha önce kaydedildiyse InventoryService'e tekrar gidip stok düşürmüyoruz.
            logger.LogInformation(
                "Create order replay returned existing order. OrderNumber: {OrderNumber}, ReservationId: {ReservationId}, CorrelationId: {CorrelationId}",
                existingOrder.OrderNumber,
                existingOrder.ReservationId,
                command.CorrelationId);

            return new CreateOrderResult(true, existingOrder.OrderNumber, existingOrder.ReservationId, []);
        }

        var reservationItems = command.Items
            .Select(item => new InventoryReservationItem(item.Sku, item.WarehouseId, item.Quantity))
            .ToArray();

        var reservationResult = await inventoryReservationService.ReserveBatchAsync(
            orderNumber,
            reservationItems,
            command.CorrelationId,
            cancellationToken);

        logger.LogInformation(
            "Inventory reservation completed for order creation. OrderNumber: {OrderNumber}, ReservationId: {ReservationId}, Success: {Success}, CorrelationId: {CorrelationId}",
            orderNumber,
            reservationResult.ReservationId,
            reservationResult.Success,
            command.CorrelationId);

        if (!reservationResult.Success)
        {
            return new CreateOrderResult(
                false,
                orderNumber,
                reservationResult.ReservationId,
                reservationResult.Failures.Select(failure => new CreateOrderFailureResult(
                    failure.Sku,
                    failure.WarehouseId,
                    failure.ErrorCode,
                    failure.Reason)).ToArray());
        }

        var lineItems = command.Items.Select(item =>
        {
            var lineItem = new OrderLineItem(item.Sku, item.WarehouseId, item.Quantity);
            lineItem.SetReservedQuantity(item.Quantity);
            return lineItem;
        }).ToList();

        var order = new Order(orderNumber, lineItems);
        order.AttachReservation(reservationResult.ReservationId!);

        var history = new OrderHistory(
            orderNumber,
            null,
            OrderStatus.Pending,
            command.CorrelationId,
            "Order created with inventory reservation.");

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await orderRepository.AddAsync(order, ct);
            await orderHistoryRepository.AddAsync(history, ct);
        }, cancellationToken);

        logger.LogInformation(
            "Order created. OrderNumber: {OrderNumber}, ReservationId: {ReservationId}, CorrelationId: {CorrelationId}",
            order.OrderNumber,
            order.ReservationId,
            command.CorrelationId);

        return new CreateOrderResult(true, order.OrderNumber, order.ReservationId, []);
    }
}
