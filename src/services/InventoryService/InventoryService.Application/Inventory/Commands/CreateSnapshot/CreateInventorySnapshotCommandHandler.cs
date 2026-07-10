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
using InventoryService.Domain.Inventory;
using Microsoft.Extensions.Logging;

namespace InventoryService.Application.Inventory.Commands.CreateSnapshot;

public sealed class CreateInventorySnapshotCommandHandler(
    IInventoryItemRepository inventoryItemRepository,
    IInventorySnapshotRepository inventorySnapshotRepository,
    IInventoryServiceMetrics metrics,
    ILogger<CreateInventorySnapshotCommandHandler> logger)
{
    private const string OperationName = "CreateSnapshot";
    private const string ValidationFailure = "VALIDATION_ERROR";
    private const string SystemError = "SYSTEM_ERROR";
    private const string InventoryStoreUnavailable = "InventoryStoreUnavailable";

    public async Task<CreateInventorySnapshotResult> HandleAsync(
        CreateInventorySnapshotCommand command,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();

        logger.LogInformation(
            "Handling create inventory snapshot command. CorrelationId: {CorrelationId}, RequestedBy: {RequestedBy}",
            command.CorrelationId,
            command.RequestedBy);

        if (string.IsNullOrWhiteSpace(command.RequestedBy))
        {
            logger.LogWarning(
                "Create inventory snapshot validation failed. CorrelationId: {CorrelationId}, ErrorMessage: {ErrorMessage}",
                command.CorrelationId,
                "RequestedBy is required.");
            metrics.RecordStockAdjustment(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordStockAdjustmentFailure(OperationName, ValidationFailure, InventoryErrorClass.Validation);
            return new CreateInventorySnapshotResult(false, ValidationFailure, "RequestedBy is required.", null);
        }

        if (string.IsNullOrWhiteSpace(command.CorrelationId))
        {
            logger.LogWarning(
                "Create inventory snapshot validation failed. ErrorMessage: {ErrorMessage}",
                "CorrelationId is required.");
            metrics.RecordStockAdjustment(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordStockAdjustmentFailure(OperationName, ValidationFailure, InventoryErrorClass.Validation);
            return new CreateInventorySnapshotResult(false, ValidationFailure, "CorrelationId is required.", null);
        }

        try
        {
            var inventoryItems = await inventoryItemRepository.GetAllAsync(cancellationToken);

            var snapshotId = Guid.CreateVersion7().ToString("N");
            var createdAt = DateTime.UtcNow;

            var snapshotItems = inventoryItems.Select(item => new SnapshotItem(
                item.Sku,
                item.WarehouseId,
                item.QuantityAvailable,
                item.QuantityReserved
            )).ToList();

            var snapshot = new InventorySnapshot(
                snapshotId,
                createdAt,
                command.RequestedBy,
                command.CorrelationId,
                snapshotItems
            );

            await inventorySnapshotRepository.AddAsync(snapshot, cancellationToken);

            logger.LogInformation(
                "Successfully created inventory snapshot. SnapshotId: {SnapshotId}, CorrelationId: {CorrelationId}, RowCount: {RowCount}",
                snapshotId,
                command.CorrelationId,
                snapshotItems.Count);

            metrics.RecordStockAdjustment(OperationName, "succeeded", Stopwatch.GetElapsedTime(startedAt));
            return new CreateInventorySnapshotResult(true, null, null, snapshotId);
        }
        catch (InventoryStoreUnavailableException exception)
        {
            metrics.RecordStockAdjustment(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordStockAdjustmentFailure(OperationName, InventoryStoreUnavailable, InventoryErrorClass.Transient);
            logger.LogError(
                exception,
                "Create inventory snapshot failed due to store unavailability. CorrelationId: {CorrelationId}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                command.CorrelationId,
                "TransientMongoError",
                InventoryErrorClass.Transient);
            return new CreateInventorySnapshotResult(false, InventoryStoreUnavailable, "Inventory store is unavailable.", null);
        }
        catch (Exception exception)
        {
            metrics.RecordStockAdjustment(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordStockAdjustmentFailure(OperationName, SystemError, InventoryErrorClass.System);
            logger.LogError(
                exception,
                "Create inventory snapshot failed with unexpected system error. CorrelationId: {CorrelationId}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                command.CorrelationId,
                "UnexpectedSystemError",
                InventoryErrorClass.System);
            return new CreateInventorySnapshotResult(false, SystemError, "An unexpected system error occurred.", null);
        }
    }
}
