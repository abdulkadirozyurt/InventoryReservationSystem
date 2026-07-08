namespace OrderService.Application.Orders.Commands.CreateOrder;

public sealed record CreateOrderCommand(
    IReadOnlyList<CreateOrderItemCommand> Items,
    string CorrelationId);

public sealed record CreateOrderItemCommand(string Sku, string WarehouseId, int Quantity);
