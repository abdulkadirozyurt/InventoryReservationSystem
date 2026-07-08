using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Inventory.Exceptions;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Application.Reservations.Results.Reserve;
using InventoryService.Domain.Inventory;
using InventoryService.Domain.InventoryTransactions;
using InventoryService.Domain.Reservations;
using Microsoft.Extensions.Logging;

namespace InventoryService.Application.Reservations.Commands.ReserveBatch;

public sealed class ReserveBatchCommandHandler(
    IInventoryItemRepository inventoryItemRepository,
    IInventoryTransactionRepository inventoryTransactionRepository,
    IReservationRepository reservationRepository,
    IInventoryUnitOfWork unitOfWork,
    IDistributedLockService distributedLockService,
    ILogger<ReserveBatchCommandHandler> logger)
{
    private const string ValidationFailure = "VALIDATION_ERROR";
    private const string StockNotFound = "STOCK_NOT_FOUND";
    private const string InsufficientStock = "INSUFFICIENT_STOCK";
    private const string LockTimeout = "LOCK_TIMEOUT";

    private static readonly TimeSpan LockExpiry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LockWaitTimeout = TimeSpan.FromSeconds(5);

    public async Task<ReserveBatchResult> HandleAsync(ReserveBatchCommand command, CancellationToken cancellationToken)
    {
        var validationFailures = Validate(command);

        if (validationFailures.Count > 0)
        {
            logger.LogWarning(
                "Reserve batch validation failed. CorrelationId: {CorrelationId}, FailureCount: {FailureCount}",
                command.CorrelationId,
                validationFailures.Count);

            return new ReserveBatchResult(false, null, validationFailures);
        }

        // Collapse duplicate SKU + warehouse lines before locking and stock checks.
        var requestedItems = AggregateRequestedItems(command.Items);

        // Create lock keys for each unique SKU + warehouse combination.
        var inventoryStockLockKeys = CreateInventoryStockLockKeys(requestedItems);

        try
        {
            // Acquire distributed locks for all requested SKU + warehouse combinations.
            await using var lockHandle = await distributedLockService.AcquireAsync(
                inventoryStockLockKeys,
                LockExpiry,
                LockWaitTimeout,
                cancellationToken);


            var stockItemsToReserve = new List<(InventoryItem StockItem, int Quantity)>();

            var stockFailures = new List<ReserveBatchFailure>();

            var reservationId = Guid.CreateVersion7().ToString("N");

            // Read every stock row before allowing the batch to continue.
            await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                foreach (var requestedItem in requestedItems)
                {
                    // find stock for the requested SKU and warehouse
                    var stockItem = await inventoryItemRepository.GetBySkuAndWarehouseAsync(requestedItem.Sku, requestedItem.WarehouseId, token);

                    // if stock is not found, add a failure and continue to the next item
                    if (stockItem is null)
                    {
                        stockFailures.Add(new ReserveBatchFailure(requestedItem.Sku, requestedItem.WarehouseId, StockNotFound, "Stock was not found."));
                        continue;
                    }

                    // if stock is found but the available quantity is less than the requested quantity, add a failure
                    if (stockItem.QuantityAvailable < requestedItem.Quantity)
                    {
                        stockFailures.Add(new ReserveBatchFailure(requestedItem.Sku, requestedItem.WarehouseId, InsufficientStock, "Insufficient stock available."));
                        continue;
                    }

                    stockItemsToReserve.Add((stockItem, requestedItem.Quantity));
                }

                if (stockFailures.Count > 0)
                    return;

                await PersistSuccessfulReservationAsync(stockItemsToReserve, reservationId, command, token);

            }, cancellationToken);

            // If there are any stock failures, return them without making any reservations.
            if (stockFailures.Count > 0)
                return new ReserveBatchResult(false, null, stockFailures);

            return new ReserveBatchResult(true, reservationId, []);
        }
        catch (TimeoutException exception)
        {
            logger.LogWarning(
                exception,
                "Reserve batch lock acquisition timed out. CorrelationId: {CorrelationId}, LockKeyCount: {LockKeyCount}",
                command.CorrelationId,
                inventoryStockLockKeys.Count
            );

            return new ReserveBatchResult(
                false,
                null,
                [
                    new ReserveBatchFailure(
                        string.Empty,
                        string.Empty,
                        LockTimeout,
                        "Could not acquire inventory locks.")
                ]
            );
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning(
                exception,
                "Reserve batch was cancelled. CorrelationId: {CorrelationId}",
                command.CorrelationId);

            throw;
        }
        catch (InventoryStoreUnavailableException exception)
        {
            logger.LogError(
                exception,
                "Reserve batch failed due to inventory store unavailability. CorrelationId: {CorrelationId}, ErrorCategory: {ErrorCategory}",
                command.CorrelationId,
                "InventoryStoreUnavailable");

            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Reserve batch failed with an unexpected system error. CorrelationId: {CorrelationId}, ErrorCategory: {ErrorCategory}",
                command.CorrelationId,
                "UnexpectedSystemError");

            throw;
        }

    }


    private async Task PersistSuccessfulReservationAsync(
        IEnumerable<(InventoryItem StockItem, int Quantity)> stockItemsToReserve, 
        string reservationId, 
        ReserveBatchCommand command, 
        CancellationToken cancellationToken)
    {
        var reservationItems = new List<ReservationItem>();

        foreach (var (stockItem, quantity) in stockItemsToReserve)
        {
            stockItem.Reserve(quantity);
            await inventoryItemRepository.UpdateAsync(stockItem, cancellationToken);

            reservationItems.Add(new ReservationItem(stockItem.Sku, stockItem.WarehouseId, quantity));

            var transaction = new InventoryTransaction(
                stockItem.Sku,
                stockItem.WarehouseId,
                InventoryTransactionType.Reserve,
                -quantity,
                quantity,
                command.CorrelationId,
                reservationId,
                command.OrderId,
                null);

            await inventoryTransactionRepository.AddAsync(transaction, cancellationToken);
        }

        var reservation = new Reservation(
            reservationId,
            command.OrderId,
            reservationItems,
            DateTime.UtcNow.AddMinutes(10));

        await reservationRepository.AddAsync(reservation, cancellationToken);
    }

    private static List<ReserveBatchFailure> Validate(ReserveBatchCommand command)
    {
        var failures = new List<ReserveBatchFailure>();

        if (string.IsNullOrWhiteSpace(command.OrderId))
        {
            failures.Add(new ReserveBatchFailure(string.Empty, string.Empty, ValidationFailure, "Order ID is required."));
        }

        if (string.IsNullOrWhiteSpace(command.CorrelationId))
        {
            failures.Add(new ReserveBatchFailure(string.Empty, string.Empty, ValidationFailure, "Correlation ID is required."));
        }

        if (command.Items is null || command.Items.Count == 0)
        {
            failures.Add(new ReserveBatchFailure(string.Empty, string.Empty, ValidationFailure, "Items are required."));
        }

        if (command.Items is not null)
        {
            foreach (var requestedItem in command.Items)
            {
                if (string.IsNullOrWhiteSpace(requestedItem.Sku))
                    failures.Add(new ReserveBatchFailure(requestedItem.Sku, requestedItem.WarehouseId, ValidationFailure, "SKU is required."));

                if (string.IsNullOrWhiteSpace(requestedItem.WarehouseId))
                    failures.Add(new ReserveBatchFailure(requestedItem.Sku, requestedItem.WarehouseId, ValidationFailure, "Warehouse ID is required."));

                if (requestedItem.Quantity <= 0)
                    failures.Add(new ReserveBatchFailure(requestedItem.Sku, requestedItem.WarehouseId, ValidationFailure, "Quantity must be greater than zero."));
            }
        }

        return failures;
    }

    private static IReadOnlyCollection<string> CreateInventoryStockLockKeys(IEnumerable<ReserveBatchItemCommand> requestedItems)
    {
        return requestedItems
            .Select(requestedItem => $"inventory:{requestedItem.Sku}:{requestedItem.WarehouseId}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static ReserveBatchItemCommand[] AggregateRequestedItems(IEnumerable<ReserveBatchItemCommand> items)
    {
        return items
            .GroupBy(item => new { item.Sku, item.WarehouseId })
            .Select(group => new ReserveBatchItemCommand(
                group.Key.Sku,
                group.Key.WarehouseId,
                group.Sum(item => item.Quantity)))
            .ToArray();
    }

}