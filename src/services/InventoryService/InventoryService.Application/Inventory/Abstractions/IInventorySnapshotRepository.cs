using InventoryService.Domain.Inventory;

namespace InventoryService.Application.Inventory.Abstractions;

public interface IInventorySnapshotRepository
{
    Task AddAsync(InventorySnapshot snapshot, CancellationToken cancellationToken = default);
    Task<InventorySnapshot?> GetByIdAsync(string snapshotId, CancellationToken cancellationToken = default);
}
