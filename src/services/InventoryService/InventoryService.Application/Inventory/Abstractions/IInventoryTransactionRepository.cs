using InventoryService.Domain.InventoryTransactions;

namespace InventoryService.Application.Inventory.Abstractions;

public interface IInventoryTransactionRepository
{
    /// <summary>
    /// Persists a new inventory audit transaction record.
    /// </summary>
    /// <param name="transaction">The inventory transaction to add.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task AddAsync(InventoryTransaction transaction, CancellationToken cancellationToken = default);
}