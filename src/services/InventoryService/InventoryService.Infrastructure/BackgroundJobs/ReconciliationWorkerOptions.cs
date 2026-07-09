namespace InventoryService.Infrastructure.BackgroundJobs;

public sealed class ReconciliationWorkerOptions
{
    // Controls report-only reconciliation cadence. Short enough for local/runtime verification, configurable for production noise control.
    public int IntervalSeconds { get; set; } = 30;
}
