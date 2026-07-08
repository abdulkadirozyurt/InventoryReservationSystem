using OrderService.Domain.Orders;

namespace OrderService.Application.Orders.Abstractions;

public interface IOrderHistoryRepository
{
    Task AddAsync(OrderHistory history, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<OrderHistory>> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default);
}
