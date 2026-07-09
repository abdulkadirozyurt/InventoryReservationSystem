using InventoryService.Domain.Checkpoints;

namespace InventoryService.Application.Reservations.Abstractions;

public interface ICheckpointRepository
{
    Task<Checkpoint?> GetByNameAsync(string jobName, CancellationToken cancellationToken = default);
    Task SaveAsync(Checkpoint checkpoint, CancellationToken cancellationToken = default);
}
