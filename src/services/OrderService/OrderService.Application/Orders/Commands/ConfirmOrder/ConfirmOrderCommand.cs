namespace OrderService.Application.Orders.Commands.ConfirmOrder;

public sealed record ConfirmOrderCommand(string OrderNumber, string CorrelationId);
