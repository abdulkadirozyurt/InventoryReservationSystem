using InventoryService.Application.Reservations.Abstractions;
using StackExchange.Redis;

namespace InventoryService.Infrastructure.Redis;

// Represents a handle for a distributed lock acquired from Redis.
// This class implements the IDistributedLockHandle interface and provides access to the lock keys associated with the acquired lock.
internal sealed class RedisDistributedLockHandle(
    IDatabase database,
    string lockToken,
    IReadOnlyCollection<string> lockKeys) : IDistributedLockHandle
{
    public IReadOnlyCollection<string> LockKeys => lockKeys;

    // disposes locks automatically in a "using" statement or when the object is no longer needed.
    public async ValueTask DisposeAsync()
    {
        foreach (var key in LockKeys.Reverse())
        {            
            await database.LockReleaseAsync(key, lockToken);
        }
    }
}
