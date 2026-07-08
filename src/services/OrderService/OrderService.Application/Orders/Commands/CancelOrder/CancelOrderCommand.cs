namespace OrderService.Application.Orders.Commands.CancelOrder;

public sealed record CancelOrderCommand(string OrderNumber, string CorrelationId, string? Reason);
