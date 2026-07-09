namespace OrderService.Infrastructure.Idempotency;

public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    // How long to keep completed requests in the store
    public TimeSpan CompletedTtl { get; init; } = TimeSpan.FromHours(24);

    // How long to keep processing requests in the store (in case of a crash, we can retry after this time)
    public TimeSpan ProcessingTtl { get; init; } = TimeSpan.FromMinutes(2);

    // How long to wait for a processing request to complete before we start replaying it
    public TimeSpan ReplayWaitTimeout { get; init; } = TimeSpan.FromSeconds(3);
    
    // How often to poll for a processing request to complete before we start replaying it
    public TimeSpan ReplayPollInterval { get; init; } = TimeSpan.FromMilliseconds(100);

}
