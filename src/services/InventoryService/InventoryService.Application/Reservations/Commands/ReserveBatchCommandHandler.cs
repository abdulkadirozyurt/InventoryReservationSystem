using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Application.Reservations.Results;
using InventoryService.Domain.Inventory;
using Microsoft.Extensions.Logging;

namespace InventoryService.Application.Reservations.Commands;

public sealed class ReserveBatchCommandHandler(
    IInventoryItemRepository inventoryItemRepository,
    IInventoryTransactionRepository inventoryTransactionRepository,
    IReservationRepository reservationRepository,
    IInventoryUnitOfWork unitOfWork,
    IDistributedLockService distributedLockService,
    ILogger<ReserveBatchCommandHandler> logger)
{
    private const string ValidationFailure = "VALIDATION_ERROR";

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
        var requestedItems = command.Items
            .GroupBy(requestedItem => new { requestedItem.Sku, requestedItem.WarehouseId })
            .Select(group => new ReserveBatchItemCommand(
                group.Key.Sku,
                group.Key.WarehouseId,
                group.Sum(requestedItem => requestedItem.Quantity)))
            .ToArray();

        var inventoryStockLockKeys = CreateInventoryStockLockKeys(requestedItems);

        try
        {
            await using var lockHandle = await distributedLockService.AcquireAsync(
                inventoryStockLockKeys,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(5),
                cancellationToken);


            var stockItemsToReserve = new List<(InventoryItem StockItem, int Quantity)>();

            var stockFailures = new List<ReserveBatchFailure>();

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
                        stockFailures.Add(new ReserveBatchFailure(requestedItem.Sku, requestedItem.WarehouseId, "STOCK_NOT_FOUND", "Stock was not found."));
                        continue;
                    }

                    // if stock is found but the available quantity is less than the requested quantity, add a failure
                    if (stockItem.QuantityAvailable < requestedItem.Quantity)
                    {
                        stockFailures.Add(new ReserveBatchFailure(requestedItem.Sku, requestedItem.WarehouseId, "INSUFFICIENT_STOCK", "Insufficient stock available."));
                        continue;
                    }

                    stockItemsToReserve.Add((stockItem, requestedItem.Quantity));
                }

                foreach (var (stockItem, quantity) in stockItemsToReserve)
                {
                    stockItem.Reserve(quantity);
                    await inventoryItemRepository.UpdateAsync(stockItem, token);
                }
            }, cancellationToken);

            // If there are any stock failures, return them without making any reservations.
            if (stockFailures.Count > 0)
                return new ReserveBatchResult(false, null, stockFailures);

            return new ReserveBatchResult(false, Guid.CreateVersion7().ToString("N"), []);
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
                        "LOCK_TIMEOUT",
                        "Could not acquire inventory locks.")
                ]
            );
        }

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
}