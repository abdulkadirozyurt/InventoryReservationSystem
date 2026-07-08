using OrderService.Application.Orders.Abstractions;
using OrderService.Application.Orders.Mapping;
using OrderService.Application.Orders.Results;

namespace OrderService.Application.Orders.Queries.ListOrders;

public sealed class ListOrdersQueryHandler(IOrderRepository orderRepository)
{
    public async Task<IReadOnlyCollection<OrderResult>> HandleAsync(ListOrdersQuery query, CancellationToken cancellationToken = default)
    {
        var orders = await orderRepository.ListAsync(query.Status, query.From, query.To, cancellationToken);
        return orders.Select(OrderResultMapper.Map).ToArray();
    }
}
