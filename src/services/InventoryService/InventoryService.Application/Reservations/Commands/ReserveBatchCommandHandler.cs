using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Application.Reservations.Results;
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

    public async Task<ReserveBatchResult> HandleAsync(ReserveBatchCommand request, CancellationToken cancellationToken)
    {
        var failures = Validate(request);

        if (failures.Count > 0)
        {
            logger.LogWarning(
                "Reserve batch validation failed. CorrelationId: {CorrelationId}, FailureCount: {FailureCount}",
                request.CorrelationId,
                failures.Count);

            return new ReserveBatchResult(false, null, failures);
        }

        var lockKeys = CreateLockKeys(request);

        try
        {
            await using var lockHandle = await distributedLockService.AcquireAsync(
            lockKeys,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(5),
            cancellationToken);

            var reservationFailures = new List<ReserveBatchFailure>();

            // stock checking logic
            await unitOfWork.ExecuteInTransactionAsync(async token => 
            {
                foreach (var item in request.Items)
                {
                    var inventoryItem = await inventoryItemRepository.GetBySkuAndWarehouseAsync(item.Sku, item.WarehouseId, token);

                    // Check if the inventory item exists
                    if (inventoryItem is null)
                    {
                        reservationFailures.Add(new ReserveBatchFailure(item.Sku, item.WarehouseId, "STOCK_NOT_FOUND", "Stock was not found."));
                        continue;
                    }

                    // Check if there is enough available quantity to reserve
                    if (inventoryItem.QuantityAvailable < item.Quantity)
                    {
                        reservationFailures.Add(new ReserveBatchFailure(item.Sku, item.WarehouseId, "INSUFFICIENT_STOCK", "Insufficient stock available."));
                    }
                }
            }, cancellationToken);


            if (reservationFailures.Count > 0)
                return new ReserveBatchResult(false, null, reservationFailures);

            // Proceed with reservation logic





            return new ReserveBatchResult(false, null, []);
        }
        catch (TimeoutException exception)
        {
            logger.LogWarning(
                exception,
                "Reserve batch lock acquisition timed out. CorrelationId: {CorrelationId}, LockKeyCount: {LockKeyCount}",
                request.CorrelationId,
                lockKeys.Count
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

    private static List<ReserveBatchFailure> Validate(ReserveBatchCommand request)
    {
        var failures = new List<ReserveBatchFailure>();

        if (string.IsNullOrWhiteSpace(request.OrderId))
        {
            failures.Add(new ReserveBatchFailure(string.Empty, string.Empty, ValidationFailure, "Order ID is required."));
        }

        if (string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            failures.Add(new ReserveBatchFailure(string.Empty, string.Empty, ValidationFailure, "Correlation ID is required."));
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            failures.Add(new ReserveBatchFailure(string.Empty, string.Empty, ValidationFailure, "Items are required."));
        }

        if (request.Items is not null)
        {
            foreach (var item in request.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Sku))
                    failures.Add(new ReserveBatchFailure(item.Sku, item.WarehouseId, ValidationFailure, "SKU is required."));

                if (string.IsNullOrWhiteSpace(item.WarehouseId))
                    failures.Add(new ReserveBatchFailure(item.Sku, item.WarehouseId, ValidationFailure, "Warehouse ID is required."));

                if (item.Quantity <= 0)
                    failures.Add(new ReserveBatchFailure(item.Sku, item.WarehouseId, ValidationFailure, "Quantity must be greater than zero."));
            }
        }

        return failures;
    }

    private static IReadOnlyCollection<string> CreateLockKeys(ReserveBatchCommand request)
    {
        return request.Items
            .Select(item => $"inventory:{item.Sku}:{item.WarehouseId}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}