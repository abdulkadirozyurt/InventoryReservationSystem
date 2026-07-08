namespace InventoryService.Application.Observability.Abstractions;

public interface IInventoryServiceMetrics
{
    void RecordDistributedLockAcquireSucceeded(TimeSpan duration);

    void RecordDistributedLockAcquireTimedOut(TimeSpan duration);

    void RecordDistributedLockAcquireFailed(TimeSpan duration);

    void RecordDistributedLockReleaseSucceeded();

    void RecordDistributedLockReleaseSkipped();

    void RecordDistributedLockReleaseFailed();

    void RecordDistributedLockHeld(TimeSpan duration);

    void RecordDistributedLockHeldTooLong(TimeSpan duration);

    void RecordReservationOperation(string operation, string outcome, TimeSpan duration);

    void RecordReservationFailure(string operation, string errorCode, string errorClass);

    void RecordTimeToReserve(TimeSpan duration);

    void RecordTimeToConfirmation(TimeSpan duration);

    void RecordStockAdjustment(string operation, string outcome, TimeSpan duration);

    void RecordStockAdjustmentFailure(string operation, string errorCode, string errorClass);
}
