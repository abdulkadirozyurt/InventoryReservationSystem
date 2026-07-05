namespace InventoryService.Infrastructure.Redis;

internal static class RedisLogMessages
{
    public const string DistributedLockAcquired =
        "Redis distributed lock acquired for {LockKeyCount} lock keys.";

    public const string DistributedLockAcquisitionTimedOut =
        "Redis distributed lock acquisition timed out after {AcquireTimeoutMs} ms for {LockKeyCount} lock keys. {AcquiredLockKeyCount} lock keys were acquired before timeout.";

    public const string DistributedLockAcquisitionFailed =
        "Redis distributed lock acquisition failed for {LockKeyCount} lock keys. {AcquiredLockKeyCount} lock keys were acquired before failure.";

    public const string DistributedLockReleaseSkipped =
        "Redis distributed lock release skipped for {LockKey}. Lock token did not match or lock already expired.";

    public const string DistributedLockReleaseFailed =
        "Redis distributed lock release failed for {LockKey}.";
}
