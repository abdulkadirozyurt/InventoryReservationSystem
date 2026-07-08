using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Inventory.Exceptions;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Application.Reservations.Results.Release;
using InventoryService.Domain.Reservations;
using Microsoft.Extensions.Logging;

namespace InventoryService.Application.Reservations.Commands.ReleaseBatch;

public sealed class ReleaseBatchCommandHandler(
    IInventoryItemRepository inventoryItemRepository,
    IInventoryTransactionRepository inventoryTransactionRepository,
    IReservationRepository reservationRepository,
    IInventoryUnitOfWork inventoryUnitOfWork,
    IDistributedLockService distributedLockService,
    ILogger<ReleaseBatchCommandHandler> logger)
{
    private const string ValidationFailure = "VALIDATION_ERROR";
    private const string ReservationNotFound = "RESERVATION_NOT_FOUND";
    private const string InvalidReservationState = "INVALID_RESERVATION_STATE";
    private const string ItemMismatch = "ITEM_MISMATCH";
    private const string StockNotFound = "STOCK_NOT_FOUND";
    private const string ReservedStockInsufficient = "RESERVED_STOCK_INSUFFICIENT";
    private const string LockTimeout = "LOCK_TIMEOUT";
    private const string InventoryStoreUnavailable = "INVENTORY_STORE_UNAVAILABLE";
    private const string SystemError = "SYSTEM_ERROR";

    private static readonly TimeSpan LockExpiry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LockWaitTimeout = TimeSpan.FromSeconds(5);

    public async Task<ReleaseBatchResult> HandleAsync(ReleaseBatchCommand command, CancellationToken cancellationToken)
    {
        var validationResult = Validate(command);

        if (validationResult is not null)
        {
            logger.LogWarning(
                "Release batch validation failed. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}",
                command.CorrelationId,
                command.ReservationId,
                ValidationFailure);

            return validationResult;
        }

        try
        {
            var reservation = await reservationRepository.GetByReservationIdAsync(command.ReservationId, cancellationToken);

            if (reservation is null)
            {
                logger.LogWarning(
                    "Release batch failed because reservation was not found. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}",
                    command.CorrelationId,
                    command.ReservationId,
                    ReservationNotFound);

                return new ReleaseBatchResult(false, ReservationNotFound, "Reservation not found.");
            }

            if (reservation.Status is not ReservationStatus.Pending)
            {
                logger.LogWarning(
                    "Release batch failed because reservation state is invalid. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, Status: {Status}, ErrorCategory: {ErrorCategory}",
                    command.CorrelationId,
                    command.ReservationId,
                    reservation.Status,
                    InvalidReservationState);

                return new ReleaseBatchResult(false, InvalidReservationState, "Reservation must be pending to release.");
            }

            if (command.Items.Count > 0 && !ItemsMatchReservation(command.Items, reservation.Items))
            {
                logger.LogWarning(
                    "Release batch failed because request items do not match reservation items. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}",
                    command.CorrelationId,
                    command.ReservationId,
                    ItemMismatch);

                return new ReleaseBatchResult(false, ItemMismatch, "Request items do not match reservation items.");
            }

            var releaseItems = AggregateReservationItems(reservation.Items);
            var inventoryStrockLockKeys = CreateInventoryStockLockKeys(releaseItems);

            await using var lockHandle = await distributedLockService.AcquireAsync(
                inventoryStrockLockKeys,
                LockExpiry,
                LockWaitTimeout,
                cancellationToken);

            ReleaseBatchResult? transactionResult = null;

            await inventoryUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
            {
                var currentReservation = await reservationRepository.GetByReservationIdAsync(command.ReservationId, transactionCancellationToken);

                if (currentReservation is null)
                {
                    transactionResult = new ReleaseBatchResult(false, ReservationNotFound, "Reservation not found.");
                    return;
                }
                if (currentReservation.Status is not ReservationStatus.Released or ReservationStatus.Expired)
                {
                    transactionResult = new ReleaseBatchResult(true, null, null);
                }
                if (currentReservation.Status is not ReservationStatus.Pending)
                {
                    transactionResult = new ReleaseBatchResult(false, InvalidReservationState, "Reservation must be pending to release.");
                    return;
                }

                var currentReleaseItems = AggregateReservationItems(currentReservation.Items);                

                foreach (var releaseItem in currentReleaseItems)
                {
                    var inventoryItem = await inventoryItemRepository.GetBySkuAndWarehouseAsync(
                        releaseItem.Sku,
                        releaseItem.WarehouseId,
                        transactionCancellationToken);

                    if (inventoryItem is null)
                    {
                        transactionResult = new ReleaseBatchResult(false, StockNotFound, "Stock was not found");
                        return;
                    }

                    try
                    {
                        inventoryItem.Release(releaseItem.Quantity);
                    }
                    catch (InvalidOperationException)
                    {
                        transactionResult = new ReleaseBatchResult(
                            false,
                            ReservedStockInsufficient,
                            "Reserved stock is insufficient to release.");

                        return;
                    }

                    await inventoryItemRepository.UpdateAsync(inventoryItem, transactionCancellationToken);
                }

                transactionResult = new ReleaseBatchResult(false, SystemError, "Release batch mutation is not implemented yet.");
            }, cancellationToken);


            return transactionResult ?? new ReleaseBatchResult(false, SystemError, "Release batch failed due to an unexpected transaction result.");
        }
        catch (TimeoutException exception)
        {
            logger.LogWarning(
                exception,
                "Release batch failed while waiting for inventory locks. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}",
                command.CorrelationId,
                command.ReservationId,
                LockTimeout);

            return new ReleaseBatchResult(false, LockTimeout, "Timed out while waiting for inventory locks.");
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning(
                exception,
                "Release batch was cancelled. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}",
                command.CorrelationId,
                command.ReservationId);

            throw;
        }
        catch (InventoryStoreUnavailableException exception)
        {
            logger.LogError(
                exception,
                "Release batch failed due to inventory store unavailability. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}",
                command.CorrelationId,
                command.ReservationId,
                "TransientMongoError");

            return new ReleaseBatchResult(false, InventoryStoreUnavailable, "Inventory store is unavailable.");
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Release batch failed with an unexpected system error. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}",
                command.CorrelationId,
                command.ReservationId,
                "UnexpectedSystemError");

            return new ReleaseBatchResult(false, SystemError, "Release batch failed due to an unexpected system error.");
        }
    }

    private static ReleaseBatchResult? Validate(ReleaseBatchCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.ReservationId))
            return new ReleaseBatchResult(false, ValidationFailure, "Reservation ID is required.");

        if (string.IsNullOrWhiteSpace(command.CorrelationId))
            return new ReleaseBatchResult(false, ValidationFailure, "Correlation ID is required.");

        foreach (var item in command.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Sku))
                return new ReleaseBatchResult(false, ValidationFailure, "SKU is required.");

            if (string.IsNullOrWhiteSpace(item.WarehouseId))
                return new ReleaseBatchResult(false, ValidationFailure, "Warehouse ID is required.");

            if (item.Quantity <= 0)
                return new ReleaseBatchResult(false, ValidationFailure, "Quantity must be greater than zero.");
        }

        return null;
    }

    private static IReadOnlyCollection<string> CreateInventoryStockLockKeys(IEnumerable<ReleaseBatchItemCommand> requestedItems)
    {
        return requestedItems
            .Select(requestedItem => $"inventory:{requestedItem.Sku}:{requestedItem.WarehouseId}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static ReleaseBatchItemCommand[] AggregateRequestedItems(IEnumerable<ReleaseBatchItemCommand> items)
    {
        return items
            .GroupBy(item => new { item.Sku, item.WarehouseId })
            .Select(group => new ReleaseBatchItemCommand(
                group.Key.Sku,
                group.Key.WarehouseId,
                group.Sum(item => item.Quantity)))
            .ToArray();
    }

    private static ReleaseBatchItemCommand[] AggregateReservationItems(IEnumerable<ReservationItem> items)
    {
        return items
            .GroupBy(item => new { item.Sku, item.WarehouseId })
            .Select(group => new ReleaseBatchItemCommand(
                group.Key.Sku,
                group.Key.WarehouseId,
                group.Sum(item => item.Quantity)))
            .ToArray();
    }

    private static bool ItemsMatchReservation(IReadOnlyCollection<ReleaseBatchItemCommand> requestedItems, IReadOnlyCollection<ReservationItem> reservationItems)
    {
        var aggregatedRequestedItems = AggregateRequestedItems(requestedItems);
        var aggregatedReservationItems = AggregateReservationItems(reservationItems);

        if (aggregatedRequestedItems.Length != aggregatedReservationItems.Length)
            return false;

        foreach (var requestedItem in aggregatedRequestedItems)
        {
            var matchingReservationItem = aggregatedReservationItems.FirstOrDefault(reservationItem =>
                    string.Equals(reservationItem.Sku, requestedItem.Sku, StringComparison.Ordinal) &&
                    string.Equals(reservationItem.WarehouseId, requestedItem.WarehouseId, StringComparison.Ordinal));

            if (matchingReservationItem is null)
                return false;

            if (matchingReservationItem.Quantity != requestedItem.Quantity)
                return false;
        }

        return true; ;
    }

}
