namespace OrderService.Application.Orders.Commands.BulkCancelOrders;

public sealed record BulkCancelOrdersCommand(IReadOnlyList<string> OrderNumbers, string CorrelationId, string? Reason);
