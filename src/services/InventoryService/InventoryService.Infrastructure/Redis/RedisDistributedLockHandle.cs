using System.Diagnostics;
using InventoryService.Application.Observability.Abstractions;
using InventoryService.Application.Reservations.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace InventoryService.Infrastructure.Redis;

// Represents a handle for a distributed lock acquired from Redis.
// This class implements the IDistributedLockHandle interface and provides access to the lock keys associated with the acquired lock.
internal sealed class RedisDistributedLockHandle(
    IDatabase database,
    string lockToken,
    IReadOnlyCollection<string> lockKeys,
    ILogger<RedisDistributedLockHandle> logger,
    IInventoryServiceMetrics metrics,
    TimeSpan lockTTL,
    long acquiredAt) : IDistributedLockHandle
{
    public IReadOnlyCollection<string> LockKeys => lockKeys;

    // disposes locks automatically in a "using" statement or when the object is no longer needed.
    public async ValueTask DisposeAsync()
    {
        var heldDuration = Stopwatch.GetElapsedTime(acquiredAt);

        if (heldDuration > lockTTL)
        {
            metrics.RecordDistributedLockHeldTooLong(heldDuration);
            logger.LogWarning(
                RedisLogMessages.DistributedLockHeldTooLong,
                heldDuration.TotalMilliseconds,
                lockTTL.TotalMilliseconds,
                LockKeys.Count);
        }
        else
        {
            metrics.RecordDistributedLockHeld(heldDuration);
        }

        foreach (var key in LockKeys.Reverse())
        {
            try
            {
                var released = await database.LockReleaseAsync(key, lockToken);

                if (released)
                {
                    metrics.RecordDistributedLockReleaseSucceeded();
                }
                else
                {
                    metrics.RecordDistributedLockReleaseSkipped();
                    logger.LogWarning(RedisLogMessages.DistributedLockReleaseSkipped, key);
                }
            }
            catch (Exception exception)
            {
                metrics.RecordDistributedLockReleaseFailed();
                logger.LogError(exception, RedisLogMessages.DistributedLockReleaseFailed, key);
            }
        }
    }
}
