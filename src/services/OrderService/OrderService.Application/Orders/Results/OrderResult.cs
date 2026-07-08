namespace OrderService.Application.Orders.Results;

public sealed record OrderResult(
    string OrderNumber,
    string Status,
    string? ReservationId,
    IReadOnlyCollection<OrderItemResult> Items,
    DateTime CreatedAt,
    DateTime UpdatedAt);
