using InventoryService.Application.Reservations.Abstractions;

namespace InventoryService.Infrastructure.Redis;

public sealed class RedisDistributedLockService : IDistributedLockService
{
    public Task<IDistributedLockHandle> AcquireAsync(IReadOnlyCollection<string> lockKeys, TimeSpan lockTTL, TimeSpan acquireTimeout, CancellationToken cancellationToken = default)
    {
        var orderedLockKeys = lockKeys
            .Order(StringComparer.Ordinal) // Ordinal => Compares strings with case sensitivity and culture-insensitivity by using ASCII values.
            .ToArray();

        IDistributedLockHandle handle = new RedisDistributedLockHandle(orderedLockKeys);

        return Task.FromResult(handle);
    }
}
