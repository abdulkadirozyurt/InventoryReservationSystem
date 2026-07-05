using InventoryService.Application.Reservations.Abstractions;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using StackExchange.Redis;

namespace InventoryService.Infrastructure.Redis;

public sealed class RedisDistributedLockService(
    IConnectionMultiplexer connectionMultiplexer,
    ILogger<RedisDistributedLockService> logger,
    ILoggerFactory loggerFactory) : IDistributedLockService
{
    private readonly IDatabase _database = connectionMultiplexer.GetDatabase();

    private static readonly ResiliencePipeline<bool> LockAcquireRetryPipeline =
            new ResiliencePipelineBuilder<bool>()
                .AddRetry(new RetryStrategyOptions<bool>
                {
                    ShouldHandle = new PredicateBuilder<bool>().HandleResult(false),
                    Delay = TimeSpan.FromMilliseconds(50),
                    MaxRetryAttempts = int.MaxValue
                }).Build();

    public async Task<IDistributedLockHandle> AcquireAsync(IReadOnlyCollection<string> lockKeys, TimeSpan lockTTL, TimeSpan acquireTimeout, CancellationToken cancellationToken = default)
    {
        var acquiredLockKeys = new List<string>();
        var lockToken = Guid.NewGuid().ToString("N"); // Generate a unique lock token for this lock acquisition.
        var lockHandleLogger = loggerFactory.CreateLogger<RedisDistributedLockHandle>();

        try
        {
            var orderedLockKeys = lockKeys
                .Distinct(StringComparer.Ordinal) // Distinct => Removes duplicate lock keys with case sensitivity and culture-insensitivity by using ASCII values.
                .Order(StringComparer.Ordinal)    // Ordinal  => Compares strings with case sensitivity and culture-insensitivity by using ASCII values.
                .ToArray();

            // Combines request cancellation and lock acquire timeout.            
            using var acquireTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            acquireTimeoutCts.CancelAfter(acquireTimeout);

            foreach (var lockKey in orderedLockKeys)
            {
                var lockTaken = await LockAcquireRetryPipeline.ExecuteAsync(
                    async token => await _database.LockTakeAsync(lockKey, lockToken, lockTTL),
                    acquireTimeoutCts.Token);

                if (!lockTaken)
                    throw new TimeoutException("Could not acquire distributed lock");

                acquiredLockKeys.Add(lockKey);
            }

            logger.LogDebug(RedisLogMessages.DistributedLockAcquired, acquiredLockKeys.Count);

            return new RedisDistributedLockHandle(_database, lockToken, acquiredLockKeys, lockHandleLogger);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // If we fail to acquire the lock for any key, we need to release all previously acquired locks.
            if (acquiredLockKeys.Count > 0)
                await new RedisDistributedLockHandle(_database, lockToken, acquiredLockKeys, lockHandleLogger).DisposeAsync();

            logger.LogWarning(
                RedisLogMessages.DistributedLockAcquisitionTimedOut,
                acquireTimeout.TotalMilliseconds,
                lockKeys.Count,
                acquiredLockKeys.Count);

            throw new TimeoutException("Could not acquire distributed lock within the configured timeout.", exception);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                RedisLogMessages.DistributedLockAcquisitionFailed,
                lockKeys.Count,
                acquiredLockKeys.Count);

            // If we fail to acquire the lock for any key, we need to release all previously acquired locks.
            if (acquiredLockKeys.Count > 0)
                await new RedisDistributedLockHandle(_database, lockToken, acquiredLockKeys, lockHandleLogger).DisposeAsync();

            throw;
        }
    }
}
