using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Inventory.Exceptions;
using InventoryService.Application.Inventory.Results;
using InventoryService.Application.Observability;
using InventoryService.Application.Observability.Abstractions;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Domain.Inventory;
using InventoryService.Domain.InventoryTransactions;
using Microsoft.Extensions.Logging;

namespace InventoryService.Application.Inventory.Commands.RestoreSnapshot;

public sealed class RestoreInventorySnapshotCommandHandler(
    IInventoryItemRepository inventoryItemRepository,
    IInventorySnapshotRepository inventorySnapshotRepository,
    IInventoryTransactionRepository inventoryTransactionRepository,
    IDistributedLockService distributedLockService,
    IInventoryUnitOfWork unitOfWork,
    IInventoryServiceMetrics metrics,
    ILogger<RestoreInventorySnapshotCommandHandler> logger)
{
    private const string OperationName = "RestoreSnapshot";
    private const string ValidationFailure = "VALIDATION_ERROR";
    private const string SnapshotNotFound = "SNAPSHOT_NOT_FOUND";
    private const string SystemError = "SYSTEM_ERROR";
    private const string LockTimeout = "LOCK_TIMEOUT";
    private const string InventoryStoreUnavailable = "InventoryStoreUnavailable";

    private static readonly TimeSpan LockExpiry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LockWaitTimeout = TimeSpan.FromSeconds(5);

    public async Task<RestoreInventorySnapshotResult> HandleAsync(
        RestoreInventorySnapshotCommand command,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();

        logger.LogInformation(
            "Handling restore inventory snapshot command. CorrelationId: {CorrelationId}, SnapshotId: {SnapshotId}, RequestedBy: {RequestedBy}",
            command.CorrelationId,
            command.SnapshotId,
            command.RequestedBy);

        if (string.IsNullOrWhiteSpace(command.SnapshotId))
        {
            logger.LogWarning(
                "Restore inventory snapshot validation failed. CorrelationId: {CorrelationId}, ErrorMessage: {ErrorMessage}",
                command.CorrelationId,
                "SnapshotId is required.");
            metrics.RecordStockAdjustment(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordStockAdjustmentFailure(OperationName, ValidationFailure, InventoryErrorClass.Validation);
            return new RestoreInventorySnapshotResult(false, ValidationFailure, "SnapshotId is required.");
        }

        if (string.IsNullOrWhiteSpace(command.RequestedBy))
        {
            logger.LogWarning(
                "Restore inventory snapshot validation failed. CorrelationId: {CorrelationId}, ErrorMessage: {ErrorMessage}",
                command.CorrelationId,
                "RequestedBy is required.");
            metrics.RecordStockAdjustment(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordStockAdjustmentFailure(OperationName, ValidationFailure, InventoryErrorClass.Validation);
            return new RestoreInventorySnapshotResult(false, ValidationFailure, "RequestedBy is required.");
        }

        if (string.IsNullOrWhiteSpace(command.CorrelationId))
        {
            logger.LogWarning(
                "Restore inventory snapshot validation failed. ErrorMessage: {ErrorMessage}",
                "CorrelationId is required.");
            metrics.RecordStockAdjustment(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordStockAdjustmentFailure(OperationName, ValidationFailure, InventoryErrorClass.Validation);
            return new RestoreInventorySnapshotResult(false, ValidationFailure, "CorrelationId is required.");
        }

        try
        {
            var snapshot = await inventorySnapshotRepository.GetByIdAsync(command.SnapshotId, cancellationToken);
            if (snapshot == null)
            {
                logger.LogWarning(
                    "Restore inventory snapshot failed because snapshot was not found. SnapshotId: {SnapshotId}, CorrelationId: {CorrelationId}",
                    command.SnapshotId,
                    command.CorrelationId);
                metrics.RecordStockAdjustment(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
                metrics.RecordStockAdjustmentFailure(OperationName, SnapshotNotFound, InventoryErrorClass.Business);
                return new RestoreInventorySnapshotResult(false, SnapshotNotFound, "Snapshot not found.");
            }

            var currentItemsInDb = await inventoryItemRepository.GetAllAsync(cancellationToken);

            var lockKeys = snapshot.Items.Select(item => $"inventory:{item.Sku}:{item.WarehouseId}")
                .Concat(currentItemsInDb.Select(item => $"inventory:{item.Sku}:{item.WarehouseId}"))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

            logger.LogInformation(
                "Acquiring locks for inventory snapshot restore. CorrelationId: {CorrelationId}, LockKeyCount: {LockKeyCount}",
                command.CorrelationId,
                lockKeys.Length);

            await using var lockHandle = await distributedLockService.AcquireAsync(lockKeys, LockExpiry, LockWaitTimeout, cancellationToken);

            var auditReason = $"Snapshot restore: {command.SnapshotId} by {command.RequestedBy}";
            var restoredCount = 0;

            await unitOfWork.ExecuteInTransactionAsync(async (txToken) =>
            {
                // Kilitler alındıktan sonra transaction kapsamında envanter listesi tekrar çekilerek veri tutarlılığı güvence altına alınır.
                var txCurrentItems = await inventoryItemRepository.GetAllAsync(txToken);
                var currentItemsMap = txCurrentItems.ToDictionary(item => (item.Sku, item.WarehouseId));

                foreach (var snapshotItem in snapshot.Items)
                {
                    var key = (snapshotItem.Sku, snapshotItem.WarehouseId);
                    if (currentItemsMap.TryGetValue(key, out var currentItem))
                    {
                        var qaDelta = snapshotItem.QuantityAvailable - currentItem.QuantityAvailable;
                        var qrDelta = snapshotItem.QuantityReserved - currentItem.QuantityReserved;

                        if (qaDelta != 0 || qrDelta != 0)
                        {
                            currentItem.RestoreQuantities(snapshotItem.QuantityAvailable, snapshotItem.QuantityReserved);
                            await inventoryItemRepository.UpdateAsync(currentItem, txToken);

                            var transaction = new InventoryTransaction(
                                snapshotItem.Sku,
                                snapshotItem.WarehouseId,
                                InventoryTransactionType.SnapshotRestore,
                                qaDelta,
                                qrDelta,
                                command.CorrelationId,
                                null,
                                null,
                                auditReason
                            );
                            await inventoryTransactionRepository.AddAsync(transaction, txToken);
                            restoredCount++;
                        }
                    }
                    else
                    {
                        var newItem = new InventoryItem(snapshotItem.Sku, snapshotItem.WarehouseId, snapshotItem.QuantityAvailable);
                        if (snapshotItem.QuantityReserved > 0)
                        {
                            newItem.RestoreQuantities(snapshotItem.QuantityAvailable, snapshotItem.QuantityReserved);
                        }

                        await inventoryItemRepository.AddAsync(newItem, txToken);

                        var transaction = new InventoryTransaction(
                            snapshotItem.Sku,
                            snapshotItem.WarehouseId,
                            InventoryTransactionType.SnapshotRestore,
                            snapshotItem.QuantityAvailable,
                            snapshotItem.QuantityReserved,
                            command.CorrelationId,
                            null,
                            null,
                            auditReason
                        );
                        await inventoryTransactionRepository.AddAsync(transaction, txToken);
                        restoredCount++;
                    }
                }
            }, cancellationToken);

            logger.LogInformation(
                "Successfully restored inventory snapshot. CorrelationId: {CorrelationId}, SnapshotId: {SnapshotId}, ChangedRowCount: {ChangedRowCount}",
                command.CorrelationId,
                command.SnapshotId,
                restoredCount);

            metrics.RecordStockAdjustment(OperationName, "succeeded", Stopwatch.GetElapsedTime(startedAt));
            return new RestoreInventorySnapshotResult(true, null, null);
        }
        catch (TimeoutException exception)
        {
            metrics.RecordStockAdjustment(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordStockAdjustmentFailure(OperationName, LockTimeout, InventoryErrorClass.Timeout);
            logger.LogError(
                exception,
                "Restore inventory snapshot failed while waiting for lock. CorrelationId: {CorrelationId}, SnapshotId: {SnapshotId}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                command.CorrelationId,
                command.SnapshotId,
                "LockAcquisitionTimeout",
                InventoryErrorClass.Timeout);
            return new RestoreInventorySnapshotResult(false, LockTimeout, "Could not acquire inventory locks in time.");
        }
        catch (InventoryStoreUnavailableException exception)
        {
            metrics.RecordStockAdjustment(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordStockAdjustmentFailure(OperationName, InventoryStoreUnavailable, InventoryErrorClass.Transient);
            logger.LogError(
                exception,
                "Restore inventory snapshot failed due to store unavailability. CorrelationId: {CorrelationId}, SnapshotId: {SnapshotId}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                command.CorrelationId,
                command.SnapshotId,
                "TransientMongoError",
                InventoryErrorClass.Transient);
            return new RestoreInventorySnapshotResult(false, InventoryStoreUnavailable, "Inventory store is unavailable.");
        }
        catch (Exception exception)
        {
            metrics.RecordStockAdjustment(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordStockAdjustmentFailure(OperationName, SystemError, InventoryErrorClass.System);
            logger.LogError(
                exception,
                "Restore inventory snapshot failed with unexpected system error. CorrelationId: {CorrelationId}, SnapshotId: {SnapshotId}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                command.CorrelationId,
                command.SnapshotId,
                "UnexpectedSystemError",
                InventoryErrorClass.System);
            return new RestoreInventorySnapshotResult(false, SystemError, "An unexpected system error occurred.");
        }
    }
}
