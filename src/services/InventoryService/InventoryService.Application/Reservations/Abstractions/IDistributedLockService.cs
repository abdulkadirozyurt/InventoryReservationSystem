namespace InventoryService.Application.Reservations.Abstractions;

public interface IDistributedLockService
{
    Task<IDistributedLockHandle> AcquireAsync(
        IReadOnlyCollection<string> lockKeys,  // SKU + warehouse
        TimeSpan lockTTL,                      // Time to live for the lock
        TimeSpan acquireTimeout,               // Time to wait for the lock to be acquired
        CancellationToken cancellationToken = default);
}

