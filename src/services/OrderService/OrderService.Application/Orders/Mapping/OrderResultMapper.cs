using OrderService.Application.Orders.Results;
using OrderService.Domain.Orders;

namespace OrderService.Application.Orders.Mapping;

internal static class OrderResultMapper
{
    public static OrderResult Map(Order order)
    {
        return new OrderResult(
            order.OrderNumber,
            order.Status.ToString(),
            order.ReservationId,
            order.Items.Select(item => new OrderItemResult(
                item.Sku,
                item.WarehouseId,
                item.RequestedQuantity,
                item.ReservedQuantity)).ToArray(),
            order.CreatedAt,
            order.UpdatedAt);
    }
}
