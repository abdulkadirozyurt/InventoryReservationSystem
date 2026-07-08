using OrderService.Application.Orders.Abstractions;
using OrderService.Application.Orders.Mapping;
using OrderService.Application.Orders.Results;

namespace OrderService.Application.Orders.Queries.GetOrder;

public sealed class GetOrderQueryHandler(IOrderRepository orderRepository)
{
    public async Task<OrderResult?> HandleAsync(GetOrderQuery query, CancellationToken cancellationToken = default)
    {
        var order = await orderRepository.GetByOrderNumberAsync(query.OrderNumber, cancellationToken);
        return order is null ? null : OrderResultMapper.Map(order);
    }
}
