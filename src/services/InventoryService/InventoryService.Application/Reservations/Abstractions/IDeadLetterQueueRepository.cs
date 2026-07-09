using InventoryService.Domain.DeadLetterQueue;

namespace InventoryService.Application.Reservations.Abstractions;

public interface IDeadLetterQueueRepository
{
    Task<int> UpsertFailureAsync(DeadLetterRecord record, CancellationToken cancellationToken = default);
}
