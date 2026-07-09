namespace InventoryService.Infrastructure.BackgroundJobs;

public sealed class ExpiryWorkerOptions
{
    public int BatchSize { get; set; } = 20;
    public int IntervalSeconds { get; set; } = 10;
    public int MaxRetryCount { get; set; } = 3;
}
