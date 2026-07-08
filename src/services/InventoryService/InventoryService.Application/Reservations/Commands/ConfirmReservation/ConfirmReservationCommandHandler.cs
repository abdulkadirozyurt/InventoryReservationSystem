using System.Diagnostics;
using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Inventory.Exceptions;
using InventoryService.Application.Observability;
using InventoryService.Application.Observability.Abstractions;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Application.Reservations.Results.Confirm;
using InventoryService.Domain.Inventory;
using InventoryService.Domain.InventoryTransactions;
using InventoryService.Domain.Reservations;
using Microsoft.Extensions.Logging;

namespace InventoryService.Application.Reservations.Commands.ConfirmReservation;

// ConfirmReservation, rezerve edilen stoğu kesin satışa çevirir.
// Burada available geri artmaz, sadece reserved düşer.
public sealed class ConfirmReservationCommandHandler(
    IInventoryItemRepository inventoryItemRepository,
    IInventoryTransactionRepository inventoryTransactionRepository,
    IReservationRepository reservationRepository,
    IInventoryUnitOfWork inventoryUnitOfWork,
    IDistributedLockService distributedLockService,
    IInventoryServiceMetrics metrics,
    ILogger<ConfirmReservationCommandHandler> logger)
{
    // Response error code'ları API contract'a sabit ve aranabilir hata döndürmek için tutulur.
    private const string OperationName = "confirm_reservation";
    private const string ValidationFailure = "VALIDATION_ERROR";
    private const string ReservationNotFound = "RESERVATION_NOT_FOUND";
    private const string InvalidReservationState = "INVALID_RESERVATION_STATE";
    private const string StockNotFound = "STOCK_NOT_FOUND";
    private const string ReservedStockInsufficient = "RESERVED_STOCK_INSUFFICIENT";
    private const string LockTimeout = "LOCK_TIMEOUT";
    private const string StoreUnavailable = "INVENTORY_STORE_UNAVAILABLE";
    private const string SystemError = "SYSTEM_ERROR";

    // Lock sonsuza kadar kalmasın ve aynı stok için sonsuz beklemeyelim.
    private static readonly TimeSpan LockExpiry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LockWaitTimeout = TimeSpan.FromSeconds(5);

    public async Task<ConfirmReservationResult> HandleAsync(ConfirmReservationCommand command, CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();

        // Basit input hataları lock/transaction işine girmeden döner.
        var validationResult = Validate(command);
        if (validationResult is not null)
        {
            metrics.RecordReservationOperation(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordReservationFailure(OperationName, ValidationFailure, InventoryErrorClass.Validation);
            logger.LogWarning(
                "Confirm reservation validation failed. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                command.CorrelationId,
                command.ReservationId,
                ValidationFailure,
                InventoryErrorClass.Validation);

            return validationResult;
        }

        try
        {
            var reservation = await reservationRepository.GetByReservationIdAsync(command.ReservationId, cancellationToken);
            if (reservation is null)
            {
                metrics.RecordReservationOperation(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
                metrics.RecordReservationFailure(OperationName, ReservationNotFound, InventoryErrorClass.Business);
                logger.LogWarning(
                    "Confirm reservation failed because reservation was not found. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                    command.CorrelationId,
                    command.ReservationId,
                    ReservationNotFound,
                    InventoryErrorClass.Business);

                return new ConfirmReservationResult(false, ReservationNotFound, "Reservation was not found.");
            }

            if (reservation.Status == ReservationStatus.Confirmed)
            {
                metrics.RecordReservationOperation(OperationName, "idempotent", Stopwatch.GetElapsedTime(startedAt));
                logger.LogInformation(
                    "Confirm reservation skipped because reservation was already confirmed. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, Status: {Status}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                    command.CorrelationId,
                    command.ReservationId,
                    reservation.Status,
                    "IdempotentConfirm",
                    InventoryErrorClass.Business);

                return new ConfirmReservationResult(true, null, null);
            }

            if (reservation.Status != ReservationStatus.Pending)
            {
                metrics.RecordReservationOperation(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
                metrics.RecordReservationFailure(OperationName, InvalidReservationState, InventoryErrorClass.Business);
                logger.LogWarning(
                    "Confirm reservation failed because reservation state is invalid. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, Status: {Status}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                    command.CorrelationId,
                    command.ReservationId,
                    reservation.Status,
                    InvalidReservationState,
                    InventoryErrorClass.Business);

                return new ConfirmReservationResult(false, InvalidReservationState, "Reservation must be pending to confirm.");
            }

            // ConfirmReservationCommand sadece ReservationId içerdiğinden (ürün listesi taşımadığından),
            // hangi SKU ve Depoların (Warehouse) kilitleneceğini öğrenmek için önce veritabanındaki rezervasyon kaydı okunur.
            var confirmItems = AggregateReservationItems(reservation.Items);
            var inventoryStockLockKeys = CreateInventoryStockLockKeys(confirmItems);
            var transactionResult = default(ConfirmReservationResult);

            // Aynı SKU/depo için reserve, release ve confirm aynı anda stok bozmasın diye lock alıyoruz.
            await using var lockHandle = await distributedLockService.AcquireAsync(
                inventoryStockLockKeys,
                LockExpiry,
                LockWaitTimeout,
                cancellationToken);

            await inventoryUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
            {
                // Lock aldıktan sonra reservation tekrar okunur; beklerken başka işlem status değiştirmiş olabilir.
                var currentReservation = await reservationRepository.GetByReservationIdAsync(command.ReservationId, transactionCancellationToken);
                if (currentReservation is null)
                {
                    logger.LogWarning(
                        "Confirm reservation transaction failed because reservation was not found. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                        command.CorrelationId,
                        command.ReservationId,
                        ReservationNotFound,
                        InventoryErrorClass.Business);

                    transactionResult = new ConfirmReservationResult(false, ReservationNotFound, "Reservation was not found.");
                    return;
                }

                if (currentReservation.Status == ReservationStatus.Confirmed)
                {
                    logger.LogInformation(
                        "Confirm reservation transaction skipped because reservation was already confirmed. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, Status: {Status}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                        command.CorrelationId,
                        command.ReservationId,
                        currentReservation.Status,
                        "IdempotentConfirm",
                        InventoryErrorClass.Business);

                    transactionResult = new ConfirmReservationResult(true, null, null);
                    return;
                }

                if (currentReservation.Status != ReservationStatus.Pending)
                {
                    logger.LogWarning(
                        "Confirm reservation transaction failed because reservation state is invalid. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, Status: {Status}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                        command.CorrelationId,
                        command.ReservationId,
                        currentReservation.Status,
                        InvalidReservationState,
                        InventoryErrorClass.Business);

                    transactionResult = new ConfirmReservationResult(false, InvalidReservationState, "Reservation must be pending to confirm.");
                    return;
                }

                var currentConfirmItems = AggregateReservationItems(currentReservation.Items);
                var inventoryItems = new List<(ReservationItem ReservationItem, InventoryItem InventoryItem)>();

                foreach (var reservationItem in currentConfirmItems)
                {
                    var inventoryItem = await inventoryItemRepository.GetBySkuAndWarehouseAsync(
                        reservationItem.Sku,
                        reservationItem.WarehouseId,
                        transactionCancellationToken);

                    if (inventoryItem is null)
                    {
                        logger.LogError(
                            "Confirm reservation transaction failed because inventory item was not found. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, Sku: {Sku}, WarehouseId: {WarehouseId}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                            command.CorrelationId,
                            command.ReservationId,
                            reservationItem.Sku,
                            reservationItem.WarehouseId,
                            StockNotFound,
                            InventoryErrorClass.Business);

                        transactionResult = new ConfirmReservationResult(false, StockNotFound, "Stock was not found for reservation item.");
                        return;
                    }

                    if (inventoryItem.QuantityReserved < reservationItem.Quantity)
                    {
                        logger.LogError(
                            "Confirm reservation transaction failed because reserved stock is insufficient. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, Sku: {Sku}, WarehouseId: {WarehouseId}, Quantity: {Quantity}, QuantityReserved: {QuantityReserved}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                            command.CorrelationId,
                            command.ReservationId,
                            reservationItem.Sku,
                            reservationItem.WarehouseId,
                            reservationItem.Quantity,
                            inventoryItem.QuantityReserved,
                            ReservedStockInsufficient,
                            InventoryErrorClass.Business);

                        transactionResult = new ConfirmReservationResult(false, ReservedStockInsufficient, "Reserved stock is insufficient for confirm.");
                        return;
                    }

                    inventoryItems.Add((reservationItem, inventoryItem));
                }

                var transactions = new List<InventoryTransaction>();

                foreach (var (reservationItem, inventoryItem) in inventoryItems)
                {
                    inventoryItem.Confirm(reservationItem.Quantity);
                    await inventoryItemRepository.UpdateAsync(inventoryItem, transactionCancellationToken);

                    transactions.Add(new InventoryTransaction(
                        reservationItem.Sku,
                        reservationItem.WarehouseId,
                        InventoryTransactionType.Confirm,
                        0,
                        -reservationItem.Quantity,
                        command.CorrelationId,
                        command.ReservationId,
                        currentReservation.OrderId,
                        "Reservation confirmed"));
                }

                currentReservation.Confirm();
                await reservationRepository.UpdateAsync(currentReservation, transactionCancellationToken);

                foreach (var transaction in transactions)
                {
                    await inventoryTransactionRepository.AddAsync(transaction, transactionCancellationToken);
                }

                transactionResult = new ConfirmReservationResult(true, null, null);
            }, cancellationToken);

            var result = transactionResult ?? new ConfirmReservationResult(false, SystemError, "Confirm reservation failed due to an unexpected transaction result.");
            RecordResult(result, startedAt, reservation.CreatedAt);
            return result;
        }
        catch (TimeoutException exception)
        {
            metrics.RecordReservationOperation(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordReservationFailure(OperationName, LockTimeout, InventoryErrorClass.Timeout);
            logger.LogWarning(
                exception,
                "Confirm reservation failed while waiting for inventory locks. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                command.CorrelationId,
                command.ReservationId,
                LockTimeout,
                InventoryErrorClass.Timeout);

            return new ConfirmReservationResult(false, LockTimeout, "Timed out while waiting for inventory locks.");
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning(
                exception,
                "Confirm reservation was cancelled. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}",
                command.CorrelationId,
                command.ReservationId);

            throw;
        }
        catch (InventoryStoreUnavailableException exception)
        {
            metrics.RecordReservationOperation(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordReservationFailure(OperationName, StoreUnavailable, InventoryErrorClass.Transient);
            // Mongo veya repository tarafındaki transient hata buraya düşer.
            logger.LogError(
                exception,
                "Confirm reservation failed due to inventory store unavailability. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                command.CorrelationId,
                command.ReservationId,
                "TransientMongoError",
                InventoryErrorClass.Transient);

            return new ConfirmReservationResult(false, StoreUnavailable, "Inventory store is unavailable.");
        }
        catch (Exception exception)
        {
            metrics.RecordReservationOperation(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordReservationFailure(OperationName, SystemError, InventoryErrorClass.System);
            logger.LogError(
                exception,
                "Confirm reservation failed with an unexpected system error. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                command.CorrelationId,
                command.ReservationId,
                "UnexpectedSystemError",
                InventoryErrorClass.System);

            return new ConfirmReservationResult(false, SystemError, "Confirm reservation failed due to an unexpected system error.");
        }
    }

    private void RecordResult(ConfirmReservationResult result, long startedAt, DateTime createdAt)
    {
        var duration = Stopwatch.GetElapsedTime(startedAt);

        if (result.Success)
        {
            metrics.RecordReservationOperation(OperationName, "success", duration);
            var timeToConfirmation = DateTime.UtcNow - createdAt;
            if (timeToConfirmation >= TimeSpan.Zero)
                metrics.RecordTimeToConfirmation(timeToConfirmation);

            return;
        }

        var errorClass = result.ErrorCode == ValidationFailure ? InventoryErrorClass.Validation : InventoryErrorClass.Business;
        metrics.RecordReservationOperation(OperationName, "failed", duration);
        metrics.RecordReservationFailure(OperationName, result.ErrorCode ?? SystemError, errorClass);
    }

    // Confirm isteği başlamadan önce zorunlu alanlar dolu mu diye bakar.
    // Bir hata varsa sonucu direkt döneriz, yoksa null dönüp akışa devam ederiz.
    private static ConfirmReservationResult? Validate(ConfirmReservationCommand command)
    {
        // ReservationId olmadan hangi rezervasyonu onaylayacağımızı bilemeyiz.
        if (string.IsNullOrWhiteSpace(command.ReservationId))
            return new ConfirmReservationResult(false, ValidationFailure, "Reservation ID is required.");

        // CorrelationId loglarda ve trace tarafında isteği takip etmek için lazım.
        if (string.IsNullOrWhiteSpace(command.CorrelationId))
            return new ConfirmReservationResult(false, ValidationFailure, "Correlation ID is required.");

        return null;
    }

    private static IReadOnlyCollection<string> CreateInventoryStockLockKeys(IEnumerable<ReservationItem> items)
    {
        // Aynı SKU/depo için tek lock alınır; sıralama deadlock riskini azaltır.
        return items
            .Select(item => $"inventory:{item.Sku}:{item.WarehouseId}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static ReservationItem[] AggregateReservationItems(IEnumerable<ReservationItem> items)
    {
        // Reservation içinde aynı SKU/depo tekrar ederse tek satır gibi işleriz.
        return items
            .GroupBy(item => new { item.Sku, item.WarehouseId })
            .Select(group => new ReservationItem(
                group.Key.Sku,
                group.Key.WarehouseId,
                group.Sum(item => item.Quantity)))
            .ToArray();
    }
}
