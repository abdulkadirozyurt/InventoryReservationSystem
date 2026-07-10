using System.Diagnostics;
using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Inventory.Exceptions;
using InventoryService.Application.Inventory.Services;
using InventoryService.Application.Observability;
using InventoryService.Application.Observability.Abstractions;
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
    IInventoryServiceMetrics metrics,
    LowStockAlertService lowStockAlertService,
    ILogger<ReserveBatchCommandHandler> logger)
{
    private const string OperationName = "reserve_batch";
    private const string ValidationFailure = "VALIDATION_ERROR";
    private const string StockNotFound = "STOCK_NOT_FOUND";
    private const string InsufficientStock = "INSUFFICIENT_STOCK";
    private const string LockTimeout = "LOCK_TIMEOUT";

    private static readonly TimeSpan LockExpiry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LockWaitTimeout = TimeSpan.FromSeconds(5);

    public async Task<ReserveBatchResult> HandleAsync(ReserveBatchCommand command, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var validationFailures = Validate(command);

        if (validationFailures.Count > 0)
        {
            metrics.RecordReservationOperation(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordReservationFailure(OperationName, ValidationFailure, InventoryErrorClass.Validation);
            logger.LogWarning(
                "Reserve batch validation failed. CorrelationId: {CorrelationId}, FailureCount: {FailureCount}, ErrorClass: {ErrorClass}",
                command.CorrelationId,
                validationFailures.Count,
                InventoryErrorClass.Validation);

            return new ReserveBatchResult(false, null, validationFailures);
        }

        // Collapse duplicate SKU + warehouse lines before allocation and stock checks.
        var requestedItems = AggregateRequestedItems(command.Items);

        // Fallback açıksa gerçek rezervasyon satırları lock almadan önce planlanır.
        // Çünkü lock set'i istenen depo değil, stok düşülecek nihai SKU+depo satırlarından oluşmalıdır.
        var allocation = await AllocateReservationItemsAsync(requestedItems, command.EnableFallback, cancellationToken);
        if (allocation.Failures.Count > 0)
        {
            metrics.RecordReservationOperation(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordReservationFailure(OperationName, allocation.Failures[0].ErrorCode, InventoryErrorClass.Business);
            return new ReserveBatchResult(false, null, allocation.Failures);
        }

        var allocatedItems = allocation.Items;

        // Stock lock'ları nihai SKU+depo satırları üzerindeki paralel değişiklikleri sıraya koyar.
        // Order lock ise aynı OrderId farklı SKU listeleriyle tekrar gelse bile ikinci isteği bekletir.
        // Böylece idempotency kontrolü yapılırken iki istek aynı anda stok değiştiremez.
        var reservationLockKeys = CreateReservationLockKeys(command.OrderId, allocatedItems);

        try
        {
            // Acquire distributed locks for all allocated SKU + warehouse combinations.
            await using var lockHandle = await distributedLockService.AcquireAsync(
                reservationLockKeys,
                LockExpiry,
                LockWaitTimeout,
                cancellationToken);


            var stockItemsToReserve = new List<(InventoryItem StockItem, int Quantity)>();

            var stockFailures = new List<ReserveBatchFailure>();

            var reservationId = Guid.CreateVersion7().ToString("N");
            var replayedReservationId = string.Empty;

            // Read every stock row before allowing the batch to continue.
            await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var existingReservation = await reservationRepository.GetByOrderIdAsync(command.OrderId, token);

                if (existingReservation is not null)
                {
                    if (!HasSameReservationItems(existingReservation, allocatedItems))
                    {
                        stockFailures.Add(new ReserveBatchFailure(
                            string.Empty,
                            string.Empty,
                            "RESERVATION_CONFLICT",
                            "Order already has a reservation with different items."));

                        logger.LogWarning(
                            "Reserve batch rejected because order already has a different reservation. OrderId: {OrderId}, ReservationId: {ReservationId}, CorrelationId: {CorrelationId}, ErrorClass: {ErrorClass}",
                            command.OrderId,
                            existingReservation.ReservationId,
                            command.CorrelationId,
                            InventoryErrorClass.Business);

                        return;
                    }

                    // Bu OrderId daha önce aynı SKU, depo ve miktarlarla işlendi.
                    // İsteği yeniden çalıştırmak stok miktarını ikinci kez düşürürdü.
                    // Bunun yerine ilk işlemde oluşturulan ReservationId'yi aynen geri döndürüyoruz.
                    replayedReservationId = existingReservation.ReservationId;

                    logger.LogInformation(
                        "Reserve batch replay returned existing reservation. OrderId: {OrderId}, ReservationId: {ReservationId}, CorrelationId: {CorrelationId}",
                        command.OrderId,
                        existingReservation.ReservationId,
                        command.CorrelationId);

                    return;
                }

                // Allocation lock öncesi anlık görüntüyle yapılır; lock beklerken başka işlem stok tüketebilir.
                // Bu yüzden mutasyondan hemen önce aynı nihai SKU+depo satırlarını tekrar okuyup miktarı doğruluyoruz.
                foreach (var allocatedItem in allocatedItems)
                {
                    var stockItem = await inventoryItemRepository.GetBySkuAndWarehouseAsync(allocatedItem.Sku, allocatedItem.WarehouseId, token);

                    if (stockItem is null)
                    {
                        stockFailures.Add(new ReserveBatchFailure(allocatedItem.Sku, allocatedItem.WarehouseId, StockNotFound, "Stock was not found."));
                        continue;
                    }

                    if (stockItem.QuantityAvailable < allocatedItem.Quantity)
                    {
                        stockFailures.Add(new ReserveBatchFailure(allocatedItem.Sku, allocatedItem.WarehouseId, InsufficientStock, "Insufficient stock available."));
                        continue;
                    }

                    stockItemsToReserve.Add((stockItem, allocatedItem.Quantity));
                }

                if (stockFailures.Count > 0)
                    return;

                await PersistSuccessfulReservationAsync(stockItemsToReserve, reservationId, command, token);

            }, cancellationToken);

            if (!string.IsNullOrWhiteSpace(replayedReservationId))
            {
                var replayDuration = Stopwatch.GetElapsedTime(startedAt);
                metrics.RecordReservationOperation(OperationName, "success", replayDuration);
                metrics.RecordTimeToReserve(replayDuration);

                return new ReserveBatchResult(true, replayedReservationId, []);
            }

            // If there are any stock failures, return them without making any reservations.
            if (stockFailures.Count > 0)
            {
                metrics.RecordReservationOperation(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
                metrics.RecordReservationFailure(OperationName, stockFailures[0].ErrorCode, InventoryErrorClass.Business);
                return new ReserveBatchResult(false, null, stockFailures);
            }

            var duration = Stopwatch.GetElapsedTime(startedAt);
            metrics.RecordReservationOperation(OperationName, "success", duration);
            metrics.RecordTimeToReserve(duration);

            return new ReserveBatchResult(true, reservationId, []);
        }
        catch (TimeoutException exception)
        {
            metrics.RecordReservationOperation(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordReservationFailure(OperationName, LockTimeout, InventoryErrorClass.Timeout);
            logger.LogWarning(
                exception,
                "Reserve batch lock acquisition timed out. CorrelationId: {CorrelationId}, LockKeyCount: {LockKeyCount}, ErrorClass: {ErrorClass}",
                command.CorrelationId,
                reservationLockKeys.Count,
                InventoryErrorClass.Timeout
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
        catch (DuplicateReservationException exception)
        {
            // Çok nadir durumda order lock süresi dolabilir veya iki servis instance'ı aynı anda ilerleyebilir.
            // MongoDB unique orderId index'i bu durumda yalnızca bir reservation kaydının commit edilmesine izin verir.
            // Duplicate alan transaction tamamen rollback olur; stok ve audit değişiklikleri de geri alınır.
            // Sonra commit edilen reservation'ı transaction dışında okuyup güvenli replay sonucu olarak döndürüyoruz.
            var existingReservation = await reservationRepository.GetByOrderIdAsync(
                command.OrderId,
                cancellationToken);

            if (existingReservation is not null && HasSameReservationItems(existingReservation, requestedItems))
            {
                var duration = Stopwatch.GetElapsedTime(startedAt);
                metrics.RecordReservationOperation(OperationName, "success", duration);
                metrics.RecordTimeToReserve(duration);

                logger.LogInformation(
                    "Reserve batch duplicate race returned existing reservation. OrderId: {OrderId}, ReservationId: {ReservationId}, CorrelationId: {CorrelationId}",
                    command.OrderId,
                    existingReservation.ReservationId,
                    command.CorrelationId);

                return new ReserveBatchResult(true, existingReservation.ReservationId, []);
            }

            logger.LogWarning(
                exception,
                "Reserve batch duplicate race found a conflicting reservation. OrderId: {OrderId}, CorrelationId: {CorrelationId}, ErrorClass: {ErrorClass}",
                command.OrderId,
                command.CorrelationId,
                InventoryErrorClass.Business);

            metrics.RecordReservationOperation(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordReservationFailure(OperationName, "RESERVATION_CONFLICT", InventoryErrorClass.Business);

            return new ReserveBatchResult(
                false,
                null,
                [new ReserveBatchFailure(
                    string.Empty,
                    string.Empty,
                    "RESERVATION_CONFLICT",
                    "Order already has a reservation with different items.")]);
        }
        catch (InventoryStoreUnavailableException exception)
        {
            metrics.RecordReservationOperation(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordReservationFailure(OperationName, "InventoryStoreUnavailable", InventoryErrorClass.Transient);
            logger.LogError(
                exception,
                "Reserve batch failed due to inventory store unavailability. CorrelationId: {CorrelationId}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                command.CorrelationId,
                "InventoryStoreUnavailable",
                InventoryErrorClass.Transient);

            throw;
        }
        catch (Exception exception)
        {
            metrics.RecordReservationOperation(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordReservationFailure(OperationName, "UnexpectedSystemError", InventoryErrorClass.System);
            logger.LogError(
                exception,
                "Reserve batch failed with an unexpected system error. CorrelationId: {CorrelationId}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                command.CorrelationId,
                "UnexpectedSystemError",
                InventoryErrorClass.System);

            throw;
        }

    }


    private async Task<(ReserveBatchItemCommand[] Items, List<ReserveBatchFailure> Failures)> AllocateReservationItemsAsync(
        IReadOnlyCollection<ReserveBatchItemCommand> requestedItems,
        bool enableFallback,
        CancellationToken cancellationToken)
    {
        var allocatedItems = new List<ReserveBatchItemCommand>();
        var failures = new List<ReserveBatchFailure>();

        foreach (var requestedItem in requestedItems)
        {
            if (!enableFallback)
            {
                var stockItem = await inventoryItemRepository.GetBySkuAndWarehouseAsync(requestedItem.Sku, requestedItem.WarehouseId, cancellationToken);

                if (stockItem is null)
                {
                    failures.Add(new ReserveBatchFailure(requestedItem.Sku, requestedItem.WarehouseId, StockNotFound, "Stock was not found."));
                    continue;
                }

                if (stockItem.QuantityAvailable < requestedItem.Quantity)
                {
                    failures.Add(new ReserveBatchFailure(requestedItem.Sku, requestedItem.WarehouseId, InsufficientStock, "Insufficient stock available."));
                    continue;
                }

                allocatedItems.Add(requestedItem);
                continue;
            }

            var stockItems = await inventoryItemRepository.GetBySkuAsync(requestedItem.Sku, cancellationToken);
            var remainingQuantity = requestedItem.Quantity;

            // Önce istenen depoyu, sonra alternatif depoları deterministik sırada deniyoruz.
            // Böylece aynı stok görüntüsü için her node aynı allocation ve aynı lock sırasını üretir.
            foreach (var stockItem in stockItems
                         .OrderBy(item => item.WarehouseId == requestedItem.WarehouseId ? 0 : 1)
                         .ThenBy(item => item.WarehouseId, StringComparer.Ordinal))
            {
                if (remainingQuantity <= 0)
                    break;

                if (stockItem.QuantityAvailable <= 0)
                    continue;

                var quantityToReserve = Math.Min(remainingQuantity, stockItem.QuantityAvailable);
                allocatedItems.Add(new ReserveBatchItemCommand(stockItem.Sku, stockItem.WarehouseId, quantityToReserve));
                remainingQuantity -= quantityToReserve;
            }

            if (remainingQuantity > 0)
            {
                failures.Add(new ReserveBatchFailure(
                    requestedItem.Sku,
                    requestedItem.WarehouseId,
                    stockItems.Count == 0 ? StockNotFound : InsufficientStock,
                    stockItems.Count == 0 ? "Stock was not found." : "Insufficient stock available."));
            }
        }

        if (failures.Count > 0)
            return ([], failures);

        return (AggregateRequestedItems(allocatedItems), failures);
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

            lowStockAlertService.Check(OperationName, command.CorrelationId, stockItem.Sku, stockItem.WarehouseId, stockItem.QuantityAvailable);

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

    private static IReadOnlyCollection<string> CreateReservationLockKeys(
        string orderId,
        IEnumerable<ReserveBatchItemCommand> requestedItems)
    {
        // Her ürün için inventory lock anahtarı, ayrıca istek için order lock anahtarı oluşturuyoruz.
        // Tüm anahtarları alfabetik sıralamak farklı isteklerin lock'ları farklı sırada alıp deadlock oluşturmasını engeller.
        return requestedItems
            .Select(requestedItem => $"inventory:{requestedItem.Sku}:{requestedItem.WarehouseId}")
            .Append($"reservation-order:{orderId}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool HasSameReservationItems(
        Reservation reservation,
        IReadOnlyCollection<ReserveBatchItemCommand> requestedItems)
    {
        // Mongo'daki reservation item'larıyla yeni isteği aynı sıraya getiriyoruz.
        // SKU, WarehouseId veya Quantity değerlerinden biri bile farklıysa bunu retry değil conflict kabul ediyoruz.
        // Böylece aynı OrderId kullanılarak farklı bir rezervasyon oluşturulamaz.
        var existingItems = reservation.Items
            .Select(item => (item.Sku, item.WarehouseId, item.Quantity))
            .OrderBy(item => item.Sku, StringComparer.Ordinal)
            .ThenBy(item => item.WarehouseId, StringComparer.Ordinal)
            .ToArray();

        var newItems = requestedItems
            .Select(item => (item.Sku, item.WarehouseId, item.Quantity))
            .OrderBy(item => item.Sku, StringComparer.Ordinal)
            .ThenBy(item => item.WarehouseId, StringComparer.Ordinal)
            .ToArray();

        return existingItems.SequenceEqual(newItems);
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
