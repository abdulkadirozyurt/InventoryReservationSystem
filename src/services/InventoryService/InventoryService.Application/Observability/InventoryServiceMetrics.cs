using InventoryService.Application.Observability.Abstractions;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace InventoryService.Application.Observability;

internal sealed class InventoryServiceMetrics : IInventoryServiceMetrics
{
    public const string MeterName = "InventoryService";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Histogram<double> DistributedLockAcquireDuration = Meter.CreateHistogram<double>(
        "inventory.distributed_lock.acquire.duration",
        unit: "ms",
        description: "Redis distributed lock acquisition duration.");

    private static readonly Counter<long> DistributedLockAcquireAttempts = Meter.CreateCounter<long>(
        "inventory.distributed_lock.acquire.attempts",
        description: "Redis distributed lock acquisition attempts.");

    private static readonly Counter<long> DistributedLockReleaseAttempts = Meter.CreateCounter<long>(
        "inventory.distributed_lock.release.attempts",
        description: "Redis distributed lock release attempts.");

    private static readonly Histogram<double> DistributedLockHeldDuration = Meter.CreateHistogram<double>(
        "inventory.distributed_lock.held.duration",
        unit: "ms",
        description: "Redis distributed lock ownership duration.");

    private static readonly Counter<long> DistributedLockHeldTooLongCount = Meter.CreateCounter<long>(
        "inventory.distributed_lock.held_too_long",
        description: "Redis distributed locks held longer than their configured TTL.");

    private static readonly Histogram<double> ReservationOperationDuration = Meter.CreateHistogram<double>(
        "inventory.reservation.operation.duration",
        unit: "ms",
        description: "Inventory reservation operation duration.");

    private static readonly Counter<long> ReservationOperationCount = Meter.CreateCounter<long>(
        "inventory.reservation.operation.count",
        description: "Inventory reservation operation count.");

    private static readonly Counter<long> ReservationFailureCount = Meter.CreateCounter<long>(
        "inventory.reservation.failure.count",
        description: "Inventory reservation operation failure count.");

    private static readonly Histogram<double> TimeToReserve = Meter.CreateHistogram<double>(
        "inventory.reservation.time_to_reserve",
        unit: "ms",
        description: "Time from reserve command handling start to reservation creation.");

    private static readonly Histogram<double> TimeToConfirmation = Meter.CreateHistogram<double>(
        "inventory.reservation.time_to_confirmation",
        unit: "ms",
        description: "Time from reservation creation to confirmation.");

    private static readonly Histogram<double> StockAdjustmentDuration = Meter.CreateHistogram<double>(
        "inventory.stock_adjustment.duration",
        unit: "ms",
        description: "Inventory stock adjustment duration.");

    private static readonly Counter<long> StockAdjustmentCount = Meter.CreateCounter<long>(
        "inventory.stock_adjustment.count",
        description: "Inventory stock adjustment count.");

    private static readonly Counter<long> StockAdjustmentFailureCount = Meter.CreateCounter<long>(
        "inventory.stock_adjustment.failure.count",
        description: "Inventory stock adjustment failure count.");

    public void RecordDistributedLockAcquireSucceeded(TimeSpan duration) =>
        RecordDistributedLockAcquire("success", duration);

    public void RecordDistributedLockAcquireTimedOut(TimeSpan duration) =>
        RecordDistributedLockAcquire("timeout", duration);

    public void RecordDistributedLockAcquireFailed(TimeSpan duration) =>
        RecordDistributedLockAcquire("failure", duration);

    public void RecordDistributedLockReleaseSucceeded() =>
        DistributedLockReleaseAttempts.Add(1, new KeyValuePair<string, object?>("outcome", "success"));

    public void RecordDistributedLockReleaseSkipped() =>
        DistributedLockReleaseAttempts.Add(1, new KeyValuePair<string, object?>("outcome", "skipped"));

    public void RecordDistributedLockReleaseFailed() =>
        DistributedLockReleaseAttempts.Add(1, new KeyValuePair<string, object?>("outcome", "failure"));

    public void RecordDistributedLockHeld(TimeSpan duration) =>
        DistributedLockHeldDuration.Record(duration.TotalMilliseconds);

    public void RecordDistributedLockHeldTooLong(TimeSpan duration)
    {
        DistributedLockHeldTooLongCount.Add(1);
        DistributedLockHeldDuration.Record(duration.TotalMilliseconds);
    }

    public void RecordReservationOperation(string operation, string outcome, TimeSpan duration)
    {
        var tags = new TagList
        {
            { "operation", operation },
            { "outcome", outcome }
        };

        ReservationOperationCount.Add(1, tags);
        ReservationOperationDuration.Record(duration.TotalMilliseconds, tags);
    }

    public void RecordReservationFailure(string operation, string errorCode, string errorClass)
    {
        var tags = new TagList
        {
            { "operation", operation },
            { "error.code", errorCode },
            { "error.class", errorClass }
        };

        ReservationFailureCount.Add(1, tags);
    }

    public void RecordTimeToReserve(TimeSpan duration) =>
        TimeToReserve.Record(duration.TotalMilliseconds);

    public void RecordTimeToConfirmation(TimeSpan duration) =>
        TimeToConfirmation.Record(duration.TotalMilliseconds);

    public void RecordStockAdjustment(string operation, string outcome, TimeSpan duration)
    {
        var tags = new TagList
        {
            { "operation", operation },
            { "outcome", outcome }
        };

        StockAdjustmentCount.Add(1, tags);
        StockAdjustmentDuration.Record(duration.TotalMilliseconds, tags);
    }

    public void RecordStockAdjustmentFailure(string operation, string errorCode, string errorClass)
    {
        var tags = new TagList
        {
            { "operation", operation },
            { "error.code", errorCode },
            { "error.class", errorClass }
        };

        StockAdjustmentFailureCount.Add(1, tags);
    }

    private static void RecordDistributedLockAcquire(string outcome, TimeSpan duration)
    {
        var tag = new KeyValuePair<string, object?>("outcome", outcome);

        DistributedLockAcquireAttempts.Add(1, tag);
        DistributedLockAcquireDuration.Record(duration.TotalMilliseconds, tag);
    }
}
