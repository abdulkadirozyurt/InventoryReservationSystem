using OrderService.Domain.Orders;

namespace OrderService.Application.Orders.Abstractions;

public interface IOrderRepository
{
    Task<IReadOnlyList<Order>> ListAsync(OrderStatus? status, DateTime? from, DateTime? to, CancellationToken cancellationToken = default);
    Task AddAsync(Order order, CancellationToken cancellationToken = default);
    Task<Order?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default);
    Task<Order?> GetByReservationIdAsync(string reservationId, CancellationToken cancellationToken = default);
    Task UpdateAsync(Order order, CancellationToken cancellationToken = default);
}
