namespace InventoryService.Application.Inventory.Abstractions;

public interface IInventoryUnitOfWork
{
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
}
