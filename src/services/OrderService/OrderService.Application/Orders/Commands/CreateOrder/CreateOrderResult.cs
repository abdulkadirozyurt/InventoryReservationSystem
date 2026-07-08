namespace OrderService.Application.Orders.Commands.CreateOrder;

public sealed record CreateOrderResult(
    bool Success,
    string OrderNumber,
    string? ReservationId,
    IReadOnlyCollection<CreateOrderFailureResult> Failures);

public sealed record CreateOrderFailureResult(string Sku, string WarehouseId, string ErrorCode, string Reason);
