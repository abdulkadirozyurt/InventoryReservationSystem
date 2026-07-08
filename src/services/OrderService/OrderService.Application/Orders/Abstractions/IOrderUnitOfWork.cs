namespace OrderService.Application.Orders.Abstractions;

public interface IOrderUnitOfWork
{
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
}
