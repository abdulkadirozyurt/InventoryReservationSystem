using InventoryReservationSystem.Contracts.Inventory;
using OrderService.Application.Orders.Abstractions;
using OrderService.Domain.Orders;

namespace OrderService.API.Endpoints;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/orders").WithTags("Orders");

        group.MapPost("/", CreateOrderAsync)
            .WithName("CreateOrder");

        group.MapGet("/{orderNumber}", GetOrderAsync)
            .WithName("GetOrder");

        group.MapPost("/{orderNumber}/cancel", CancelOrderAsync)
            .WithName("CancelOrder");

        group.MapPost("/bulk-cancel", BulkCancelOrdersAsync)
            .WithName("BulkCancelOrders");

        group.MapGet("/", ListOrdersAsync)
            .WithName("ListOrders");

        group.MapPost("/{orderNumber}/confirm", ConfirmOrderAsync)
            .WithName("ConfirmOrder");

        return app;
    }

    private static async Task<IResult> CreateOrderAsync(
        CreateOrderRequest request,
        InventoryReservations.InventoryReservationsClient inventoryClient,
        IOrderRepository orderRepository,
        IOrderHistoryRepository orderHistoryRepository,
        IOrderUnitOfWork unitOfWork,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var correlationId = context.Items[Extensions.CorrelationIdItemName]?.ToString()
                    ?? Guid.CreateVersion7().ToString("N");

        var orderNumber = Guid.CreateVersion7().ToString("N");

        var reserveRequest = new ReserveBatchRequest
        {
            Metadata = new RequestMetadata
            {
                CorrelationId = correlationId
            },
            OrderId = orderNumber
        };

        reserveRequest.Items.AddRange(request.Items.Select(item => new ReservationItem
        {
            Sku = item.Sku,
            WarehouseId = item.WarehouseId,
            Quantity = item.Quantity
        }));

        var reserveResponse = await inventoryClient.ReserveBatchAsync(reserveRequest, cancellationToken: cancellationToken);

        if (reserveResponse.Success)
        {
            // order içindeki item'ları OrderLineItem domain modeline dönüştür
            var lineItems = request.Items.Select(item =>
            {
                var lineItem = new OrderLineItem(item.Sku, item.WarehouseId, item.Quantity);
                lineItem.SetReservedQuantity(item.Quantity);
                return lineItem;
            }).ToList();

            // order ve history nesnelerini oluştur
            var order = new Order(orderNumber, lineItems);

            var history = new OrderHistory(orderNumber, null, OrderStatus.Pending, correlationId, "Order created with inventory reservation");

            // transaction içinde order ve history'yi ekle
            await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await orderRepository.AddAsync(order, ct);
                await orderHistoryRepository.AddAsync(history, ct);
            }, cancellationToken);
        }


        return Results.Ok(new CreateOrderResponse(
            reserveResponse.Success,
            reserveResponse.ReservationId,
            reserveResponse.Failures.Select(failure => new CreateOrderFailureResponse(
                failure.Sku,
                failure.WarehouseId,
                failure.ErrorCode,
                failure.Reason)).ToArray()));
    }

    private static async Task<IResult> GetOrderAsync(string orderNumber, IOrderRepository orderRepository, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByOrderNumberAsync(orderNumber, cancellationToken);
        if (order is null)
            return Results.NotFound();

        return Results.Ok(MapOrder(order));
    }


    private static Task<IResult> CancelOrderAsync(string orderNumber)
    {
        return Task.FromResult<IResult>(Results.Ok(new CancelOrderResponse(orderNumber, true)));
    }

    private static Task<IResult> BulkCancelOrdersAsync(BulkCancelOrdersRequest request)
    {
        return Task.FromResult<IResult>(Results.Ok(new BulkCancelOrdersResponse(
            request.OrderNumbers.Select(orderNumber => new CancelOrderResponse(orderNumber, true)).ToArray())));
    }

    private static async Task<IResult> ListOrdersAsync(
        OrderStatus? status,
        DateTime? from,
        DateTime? to,
        IOrderRepository orderRepository,
        CancellationToken cancellationToken)
    {
        var orders = await orderRepository.ListAsync(status, from, to, cancellationToken);

        return Results.Ok(new ListOrdersResponse(
            orders.Select(MapOrder).ToArray()));
    }

    private static async Task<IResult> ConfirmOrderAsync(
    string orderNumber,
    InventoryReservations.InventoryReservationsClient inventoryClient,
    IOrderRepository orderRepository,
    IOrderHistoryRepository orderHistoryRepository,
    IOrderUnitOfWork unitOfWork,
    HttpContext context,
    CancellationToken cancellationToken)
    {
        var correlationId = context.Items[Microsoft.Extensions.Hosting.Extensions.CorrelationIdItemName]?.ToString()
            ?? Guid.CreateVersion7().ToString("N");

        var order = await orderRepository.GetByOrderNumberAsync(orderNumber, cancellationToken);

        if (order is null)
        {
            return Results.NotFound(new ConfirmOrderResponse(
                orderNumber,
                false,
                "OrderNotFound",
                "Order was not found."));
        }

        if (order.Status != OrderStatus.Pending)
        {
            return Results.BadRequest(new ConfirmOrderResponse(
                order.OrderNumber,
                false,
                "InvalidOrderStatus",
                $"Order status must be Pending. Current status: {order.Status}."));
        }

        if (string.IsNullOrWhiteSpace(order.ReservationId))
        {
            return Results.BadRequest(new ConfirmOrderResponse(
                order.OrderNumber,
                false,
                "MissingReservationId",
                "Order does not have a reservation id."));
        }

        var confirmRequest = new ConfirmReservationRequest
        {
            Metadata = new RequestMetadata
            {
                CorrelationId = correlationId
            },
            ReservationId = order.ReservationId
        };

        var confirmResponse = await inventoryClient.ConfirmReservationAsync(
            confirmRequest,
            cancellationToken: cancellationToken);

        if (!confirmResponse.Success)
        {
            return Results.BadRequest(new ConfirmOrderResponse(
                order.OrderNumber,
                false,
                confirmResponse.ErrorCode,
                confirmResponse.ErrorMessage));
        }

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var previousStatus = order.Status;

            order.Confirm();

            await orderRepository.UpdateAsync(order, ct);

            var history = new OrderHistory(
                order.OrderNumber,
                previousStatus,
                order.Status,
                correlationId,
                "Order confirmed.");

            await orderHistoryRepository.AddAsync(history, ct);
        }, cancellationToken);

        return Results.Ok(new ConfirmOrderResponse(order.OrderNumber, true));
    }

    private static GetOrderResponse MapOrder(Order order)
    {
        return new GetOrderResponse(
            order.OrderNumber,
            order.Status.ToString(),
            order.ReservationId,
            order.Items.Select(item => new GetOrderItemResponse(
                item.Sku,
                item.WarehouseId,
                item.RequestedQuantity,
                item.ReservedQuantity)).ToArray(),
            order.CreatedAt,
            order.UpdatedAt);
    }
}

public sealed record CreateOrderRequest(IReadOnlyList<CreateOrderItemRequest> Items);

public sealed record CreateOrderItemRequest(string Sku, string WarehouseId, int Quantity);

public sealed record CreateOrderResponse(bool Success, string ReservationId, IReadOnlyCollection<CreateOrderFailureResponse> Failures);

public sealed record CreateOrderFailureResponse(string Sku, string WarehouseId, string ErrorCode, string Reason);

public sealed record GetOrderResponse(
    string OrderNumber,
    string Status,
    string? ReservationId,
    IReadOnlyCollection<GetOrderItemResponse> Items,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record GetOrderItemResponse(
    string Sku,
    string WarehouseId,
    int RequestedQuantity,
    int ReservedQuantity);

public sealed record CancelOrderResponse(string OrderNumber, bool Success);

public sealed record BulkCancelOrdersRequest(IReadOnlyList<string> OrderNumbers);

public sealed record BulkCancelOrdersResponse(IReadOnlyCollection<CancelOrderResponse> Results);

public sealed record ListOrdersResponse(IReadOnlyCollection<GetOrderResponse> Orders);
public sealed record ConfirmOrderResponse(
    string OrderNumber,
    bool Success,
    string? ErrorCode = null,
    string? ErrorMessage = null);