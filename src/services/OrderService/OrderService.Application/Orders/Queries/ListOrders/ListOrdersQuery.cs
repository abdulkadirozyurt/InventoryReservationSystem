using OrderService.Domain.Orders;

namespace OrderService.Application.Orders.Queries.ListOrders;

public sealed record ListOrdersQuery(OrderStatus? Status, DateTime? From, DateTime? To);
