using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Inventory.Exceptions;
using InventoryService.Application.Inventory.Results;
using InventoryService.Application.Inventory.Services;
using InventoryService.Application.Observability;
using InventoryService.Application.Observability.Abstractions;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Domain.Inventory;
using InventoryService.Domain.InventoryTransactions;
using Microsoft.Extensions.Logging;

namespace InventoryService.Application.Inventory.Commands.RebalanceWarehouse;

public sealed class RebalanceWarehouseCommandHandler(
    IInventoryItemRepository inventoryItemRepository,
    IInventoryTransactionRepository inventoryTransactionRepository,
    IInventoryUnitOfWork unitOfWork,
    IDistributedLockService distributedLockService,
    IInventoryServiceMetrics metrics,
    LowStockAlertService lowStockAlertService,
    ILogger<RebalanceWarehouseCommandHandler> logger)
{
    private const string OperationName = "rebalance_warehouse";
    private const string ValidationFailure = "VALIDATION_ERROR";
    private const string StockNotFound = "STOCK_NOT_FOUND";
    private const string InsufficientStock = "INSUFFICIENT_STOCK";
    private const string LockTimeout = "LOCK_TIMEOUT";
    private const string InventoryStoreUnavailable = "INVENTORY_STORE_UNAVAILABLE";
    private const string SystemError = "SYSTEM_ERROR";

    private static readonly TimeSpan LockExpiry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LockWaitTimeout = TimeSpan.FromSeconds(5);

    public async Task<RebalanceWarehouseResult> HandleAsync(RebalanceWarehouseCommand command, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();

        logger.LogInformation(
            "Handling rebalance warehouse command. CorrelationId: {CorrelationId}, Sku: {Sku}, SourceWarehouseId: {SourceWarehouseId}, TargetWarehouseId: {TargetWarehouseId}, Quantity: {Quantity}",
            command.CorrelationId,
            command.Sku,
            command.SourceWarehouseId,
            command.TargetWarehouseId,
            command.Quantity);

        var validationError = Validate(command);
        if (validationError is not null)
        {
            metrics.RecordStockAdjustment(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordStockAdjustmentFailure(OperationName, ValidationFailure, InventoryErrorClass.Validation);
            logger.LogWarning(
                "Rebalance warehouse validation failed. CorrelationId: {CorrelationId}, Sku: {Sku}, SourceWarehouseId: {SourceWarehouseId}, TargetWarehouseId: {TargetWarehouseId}, Quantity: {Quantity}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}, ErrorMessage: {ErrorMessage}",
                command.CorrelationId,
                command.Sku,
                command.SourceWarehouseId,
                command.TargetWarehouseId,
                command.Quantity,
                validationError.ErrorCode,
                InventoryErrorClass.Validation,
                validationError.ErrorMessage);

            return validationError;
        }

        // Farklı isteklerin lock'ları farklı sırada alıp deadlock oluşturmasını engellemek için anahtarları alfabetik sıralıyoruz.
        // İşlemin atomik gerçekleşmesi ve her iki deponun stoku transaction başlamadan önce güvenli şekilde güncellenebilmesi için hem kaynak hem de hedef depo kilitlenmelidir.
        var lockKeys = new[]
        {
            $"inventory:{command.Sku}:{command.SourceWarehouseId}",
            $"inventory:{command.Sku}:{command.TargetWarehouseId}"
        }.Distinct(StringComparer.Ordinal)
         .Order(StringComparer.Ordinal)
         .ToArray();

        try
        {
            await using var lockHandle = await distributedLockService.AcquireAsync(
                lockKeys,
                LockExpiry,
                LockWaitTimeout,
                cancellationToken);

            var sourceInventoryItem = await inventoryItemRepository.GetBySkuAndWarehouseAsync(command.Sku, command.SourceWarehouseId, cancellationToken);
            if (sourceInventoryItem is null)
            {
                metrics.RecordStockAdjustment(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
                metrics.RecordStockAdjustmentFailure(OperationName, StockNotFound, InventoryErrorClass.Business);
                logger.LogWarning(
                    "Rebalance warehouse failed because source stock was not found. CorrelationId: {CorrelationId}, Sku: {Sku}, SourceWarehouseId: {SourceWarehouseId}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                    command.CorrelationId,
                    command.Sku,
                    command.SourceWarehouseId,
                    StockNotFound,
                    InventoryErrorClass.Business);

                return new RebalanceWarehouseResult(false, StockNotFound, "Source stock not found.", command.Sku, command.SourceWarehouseId, command.TargetWarehouseId);
            }

            if (sourceInventoryItem.QuantityAvailable < command.Quantity)
            {
                metrics.RecordStockAdjustment(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
                metrics.RecordStockAdjustmentFailure(OperationName, InsufficientStock, InventoryErrorClass.Business);
                logger.LogWarning(
                    "Rebalance warehouse failed because source available stock is insufficient. CorrelationId: {CorrelationId}, Sku: {Sku}, SourceWarehouseId: {SourceWarehouseId}, Quantity: {Quantity}, QuantityAvailable: {QuantityAvailable}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                    command.CorrelationId,
                    command.Sku,
                    command.SourceWarehouseId,
                    command.Quantity,
                    sourceInventoryItem.QuantityAvailable,
                    InsufficientStock,
                    InventoryErrorClass.Business);

                return new RebalanceWarehouseResult(false, InsufficientStock, "Insufficient stock available in source warehouse.", command.Sku, command.SourceWarehouseId, command.TargetWarehouseId, sourceInventoryItem.QuantityAvailable, 0);
            }

            RebalanceWarehouseResult? transactionResult = null;

            await unitOfWork.ExecuteInTransactionAsync(async transCancellationToken =>
            {
                var sourceItemTx = await inventoryItemRepository.GetBySkuAndWarehouseAsync(command.Sku, command.SourceWarehouseId, transCancellationToken)
                    ?? throw new InvalidOperationException("Source stock item disappeared during transaction.");

                if (sourceItemTx.QuantityAvailable < command.Quantity)
                {
                    throw new InvalidOperationException("Insufficient stock available in source warehouse during transaction.");
                }

                sourceItemTx.DecreaseStock(command.Quantity);
                await inventoryItemRepository.UpdateAsync(sourceItemTx, transCancellationToken);
                lowStockAlertService.Check("RebalanceWarehouse-Source", command.CorrelationId, sourceItemTx.Sku, sourceItemTx.WarehouseId, sourceItemTx.QuantityAvailable);

                var targetItemTx = await inventoryItemRepository.GetBySkuAndWarehouseAsync(command.Sku, command.TargetWarehouseId, transCancellationToken);
                if (targetItemTx is null)
                {
                    targetItemTx = new InventoryItem(command.Sku, command.TargetWarehouseId, command.Quantity);
                    await inventoryItemRepository.AddAsync(targetItemTx, transCancellationToken);
                }
                else
                {
                    targetItemTx.IncreaseStock(command.Quantity);
                    await inventoryItemRepository.UpdateAsync(targetItemTx, transCancellationToken);
                }

                lowStockAlertService.Check("RebalanceWarehouse-Target", command.CorrelationId, targetItemTx.Sku, targetItemTx.WarehouseId, targetItemTx.QuantityAvailable);

                var sourceTx = new InventoryTransaction(
                    command.Sku,
                    command.SourceWarehouseId,
                    InventoryTransactionType.Rebalance,
                    -command.Quantity,
                    0,
                    command.CorrelationId,
                    null,
                    null,
                    command.Reason);

                var targetTx = new InventoryTransaction(
                    command.Sku,
                    command.TargetWarehouseId,
                    InventoryTransactionType.Rebalance,
                    command.Quantity,
                    0,
                    command.CorrelationId,
                    null,
                    null,
                    command.Reason);

                await inventoryTransactionRepository.AddAsync(sourceTx, transCancellationToken);
                await inventoryTransactionRepository.AddAsync(targetTx, transCancellationToken);

                logger.LogInformation(
                    "Rebalance warehouse completed. CorrelationId: {CorrelationId}, Sku: {Sku}, SourceWarehouseId: {SourceWarehouseId}, TargetWarehouseId: {TargetWarehouseId}, QuantityMoved: {QuantityMoved}, SourceAvailableStock: {SourceAvailableStock}, TargetAvailableStock: {TargetAvailableStock}, Reason: {Reason}",
                    command.CorrelationId,
                    command.Sku,
                    command.SourceWarehouseId,
                    command.TargetWarehouseId,
                    command.Quantity,
                    sourceItemTx.QuantityAvailable,
                    targetItemTx.QuantityAvailable,
                    command.Reason);

                transactionResult = new RebalanceWarehouseResult(
                    true,
                    null,
                    null,
                    command.Sku,
                    command.SourceWarehouseId,
                    command.TargetWarehouseId,
                    sourceItemTx.QuantityAvailable,
                    targetItemTx.QuantityAvailable);
            }, cancellationToken);

            var result = transactionResult ?? new RebalanceWarehouseResult(false, SystemError, "Rebalance transaction failed.");
            if (result.Success)
            {
                metrics.RecordStockAdjustment(OperationName, "success", Stopwatch.GetElapsedTime(startedAt));
            }
            else
            {
                metrics.RecordStockAdjustment(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
                metrics.RecordStockAdjustmentFailure(OperationName, result.ErrorCode ?? SystemError, InventoryErrorClass.System);
            }

            return result;
        }
        catch (TimeoutException exception)
        {
            metrics.RecordStockAdjustment(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordStockAdjustmentFailure(OperationName, LockTimeout, InventoryErrorClass.Timeout);
            logger.LogError(
                exception,
                "Rebalance warehouse failed while waiting for inventory lock. CorrelationId: {CorrelationId}, Sku: {Sku}, Source: {Source}, Target: {Target}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                command.CorrelationId,
                command.Sku,
                command.SourceWarehouseId,
                command.TargetWarehouseId,
                LockTimeout,
                InventoryErrorClass.Timeout);

            return new RebalanceWarehouseResult(false, LockTimeout, "Could not acquire inventory lock in time.", command.Sku, command.SourceWarehouseId, command.TargetWarehouseId);
        }
        catch (OperationCanceledException exception)
        {
            metrics.RecordStockAdjustment(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordStockAdjustmentFailure(OperationName, SystemError, InventoryErrorClass.System);
            logger.LogWarning(
                exception,
                "Rebalance warehouse canceled. CorrelationId: {CorrelationId}, Sku: {Sku}, Source: {Source}, Target: {Target}",
                command.CorrelationId,
                command.Sku,
                command.SourceWarehouseId,
                command.TargetWarehouseId);

            return new RebalanceWarehouseResult(false, SystemError, "Operation canceled.", command.Sku, command.SourceWarehouseId, command.TargetWarehouseId);
        }
        catch (InventoryStoreUnavailableException exception)
        {
            metrics.RecordStockAdjustment(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordStockAdjustmentFailure(OperationName, InventoryStoreUnavailable, InventoryErrorClass.Transient);
            logger.LogError(
                exception,
                "Rebalance warehouse failed due to inventory store unavailability. CorrelationId: {CorrelationId}, Sku: {Sku}, Source: {Source}, Target: {Target}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                command.CorrelationId,
                command.Sku,
                command.SourceWarehouseId,
                command.TargetWarehouseId,
                "TransientMongoError",
                InventoryErrorClass.Transient);

            return new RebalanceWarehouseResult(false, InventoryStoreUnavailable, "Inventory store is unavailable.", command.Sku, command.SourceWarehouseId, command.TargetWarehouseId);
        }
        catch (Exception exception)
        {
            metrics.RecordStockAdjustment(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordStockAdjustmentFailure(OperationName, SystemError, InventoryErrorClass.System);
            logger.LogError(
                exception,
                "Rebalance warehouse failed with an unexpected system error. CorrelationId: {CorrelationId}, Sku: {Sku}, Source: {Source}, Target: {Target}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                command.CorrelationId,
                command.Sku,
                command.SourceWarehouseId,
                command.TargetWarehouseId,
                "UnexpectedSystemError",
                InventoryErrorClass.System);

            return new RebalanceWarehouseResult(false, SystemError, "An unexpected system error occurred.", command.Sku, command.SourceWarehouseId, command.TargetWarehouseId);
        }
    }

    private static RebalanceWarehouseResult? Validate(RebalanceWarehouseCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Sku))
            return new RebalanceWarehouseResult(false, ValidationFailure, "SKU is required.");

        if (string.IsNullOrWhiteSpace(command.SourceWarehouseId))
            return new RebalanceWarehouseResult(false, ValidationFailure, "Source warehouse ID is required.", command.Sku);

        if (string.IsNullOrWhiteSpace(command.TargetWarehouseId))
            return new RebalanceWarehouseResult(false, ValidationFailure, "Target warehouse ID is required.", command.Sku, command.SourceWarehouseId);

        if (command.SourceWarehouseId.Equals(command.TargetWarehouseId, StringComparison.OrdinalIgnoreCase))
            return new RebalanceWarehouseResult(false, ValidationFailure, "Source and target warehouses must be different.", command.Sku, command.SourceWarehouseId, command.TargetWarehouseId);

        if (command.Quantity <= 0)
            return new RebalanceWarehouseResult(false, ValidationFailure, "Quantity must be greater than zero.", command.Sku, command.SourceWarehouseId, command.TargetWarehouseId);

        if (string.IsNullOrWhiteSpace(command.Reason))
            return new RebalanceWarehouseResult(false, ValidationFailure, "Reason is required.", command.Sku, command.SourceWarehouseId, command.TargetWarehouseId);

        if (string.IsNullOrWhiteSpace(command.CorrelationId))
            return new RebalanceWarehouseResult(false, ValidationFailure, "Correlation ID is required.", command.Sku, command.SourceWarehouseId, command.TargetWarehouseId);

        return null;
    }
}
