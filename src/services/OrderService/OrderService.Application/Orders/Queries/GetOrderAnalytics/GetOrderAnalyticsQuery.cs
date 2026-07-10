using OrderService.Domain.Orders;

namespace OrderService.Application.Orders.Queries.GetOrderAnalytics;

public sealed record GetOrderAnalyticsQuery(DateTime From, DateTime To, string? Sku, string? WarehouseId, OrderStatus? Status);
