using InventoryReservationSystem.Contracts.Inventory;
using Microsoft.AspNetCore.Http.HttpResults;

namespace OrderService.API.Endpoints;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/orders").WithTags("Orders");

        group.MapPost("/", CreateOrderAsync)
            .WithName("CreateOrder");

        return app;
    }

    private static async Task<IResult> CreateOrderAsync(
        CreateOrderRequest request,
        InventoryReservations.InventoryReservationsClient inventoryClient,
        HttpContext context)
    {
        var correlationId = context.Items[Microsoft.Extensions.Hosting.Extensions.CorrelationIdItemName]?.ToString()
                    ?? Guid.CreateVersion7().ToString("N");



        var reserveRequest = new ReserveBatchRequest
        {
            Metadata = new RequestMetadata
            {
                CorrelationId = correlationId
            },
            OrderId = Guid.CreateVersion7().ToString("N")
        };

        reserveRequest.Items.AddRange(request.Items.Select(item => new ReservationItem
        {
            Sku = item.Sku,
            WarehouseId = item.WarehouseId,
            Quantity = item.Quantity
        }));

        var reserveResponse = await inventoryClient.ReserveBatchAsync(reserveRequest);

        return Results.Ok(new CreateOrderResponse(
            reserveResponse.Success,
            reserveResponse.ReservationId,
            reserveResponse.Failures.Select(failure => new CreateOrderFailureResponse(
                failure.Sku,
                failure.WarehouseId,
                failure.ErrorCode,
                failure.Reason)).ToArray()));
    }
}

public sealed record CreateOrderRequest(IReadOnlyList<CreateOrderItemRequest> Items);

public sealed record CreateOrderItemRequest(string Sku, string WarehouseId, int Quantity);

public sealed record CreateOrderResponse(bool Success, string ReservationId, IReadOnlyCollection<CreateOrderFailureResponse> Failures);

public sealed record CreateOrderFailureResponse(string Sku, string WarehouseId, string ErrorCode, string Reason);
