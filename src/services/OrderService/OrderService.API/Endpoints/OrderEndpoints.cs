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
            Quantity = item.Quantity
        }));

        var reserveResponse = await inventoryClient.ReserveBatchAsync(reserveRequest);

        return Results.Ok(new CreateOrderResponse(reserveResponse.Success, reserveResponse.ReservationId));
    }
}

public sealed record CreateOrderRequest(IReadOnlyList<CreateOrderItemRequest> Items);

public sealed record CreateOrderItemRequest(string Sku, int Quantity);

public sealed record CreateOrderResponse(bool Success, string ReservationId);
