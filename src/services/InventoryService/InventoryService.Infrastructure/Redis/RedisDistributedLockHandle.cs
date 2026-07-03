using InventoryService.Application.Reservations.Abstractions;

namespace InventoryService.Infrastructure.Redis;

internal sealed class RedisDistributedLockHandle(IReadOnlyCollection<string> lockKeys) : IDistributedLockHandle
{
    public IReadOnlyCollection<string> LockKeys => lockKeys;

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
