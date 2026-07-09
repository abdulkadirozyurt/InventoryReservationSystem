using System.Diagnostics;
using System.Text.Json;
using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Inventory.Exceptions;
using InventoryService.Application.Observability;
using InventoryService.Application.Observability.Abstractions;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Application.Reservations.Results.Release;
using InventoryService.Domain.DeadLetterQueue;
using InventoryService.Domain.InventoryTransactions;
using InventoryService.Domain.Reservations;
using Microsoft.Extensions.Logging;

namespace InventoryService.Application.Reservations.Commands.ReleaseBatch;

// ReleaseBatch, daha önce rezerve edilmiş stokları geri açar.
// Ana kural: kaynak client request'i değil, MongoDB'deki Reservation kaydıdır.
public sealed class ReleaseBatchCommandHandler(
    IInventoryItemRepository inventoryItemRepository,
    IInventoryTransactionRepository inventoryTransactionRepository,
    IReservationRepository reservationRepository,
    IInventoryUnitOfWork inventoryUnitOfWork,
    IDistributedLockService distributedLockService,
    IInventoryServiceMetrics metrics,
    ILogger<ReleaseBatchCommandHandler> logger,
    IDeadLetterQueueRepository deadLetterQueueRepository)
{
    // Response error code'ları API contract'a sabit ve aranabilir hata döndürmek için tutulur.
    private const string OperationName = "release_batch";
    private const string ValidationFailure = "VALIDATION_ERROR";
    private const string ReservationNotFound = "RESERVATION_NOT_FOUND";
    private const string InvalidReservationState = "INVALID_RESERVATION_STATE";
    private const string ItemMismatch = "ITEM_MISMATCH";
    private const string StockNotFound = "STOCK_NOT_FOUND";
    private const string ReservedStockInsufficient = "RESERVED_STOCK_INSUFFICIENT";
    private const string LockTimeout = "LOCK_TIMEOUT";
    private const string InventoryStoreUnavailable = "INVENTORY_STORE_UNAVAILABLE";
    private const string SystemError = "SYSTEM_ERROR";

    // Lock TTL: process ölürse lock sonsuza kadar kalmasın.
    // Wait timeout: aynı SKU/depo üzerinde sonsuz beklemeyelim.
    private static readonly TimeSpan LockExpiry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LockWaitTimeout = TimeSpan.FromSeconds(5);

    public async Task<ReleaseBatchResult> HandleAsync(ReleaseBatchCommand command, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();

        // Basit input hataları lock/DB işlemine girmeden dönülür.
        var validationResult = Validate(command);

        if (validationResult is not null)
        {
            metrics.RecordReservationOperation(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordReservationFailure(OperationName, ValidationFailure, InventoryErrorClass.Validation);
            logger.LogWarning(
                "Release batch validation failed. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                command.CorrelationId,
                command.ReservationId,
                ValidationFailure,
                InventoryErrorClass.Validation);

            if (!command.IsExpiry)
            {
                await UpsertDlqAsync(command, null, ValidationFailure, "Release batch validation failed.", cancellationToken);
            }

            return validationResult;
        }

        string? orderId = null;

        try
        {
            // İlk okuma lock anahtarlarını belirlemek ve hızlı state kararı vermek içindir.
            var reservation = await reservationRepository.GetByReservationIdAsync(command.ReservationId, cancellationToken);

            // Reservation yoksa stok üzerinde hiçbir işlem yapılmaz.
            if (reservation is null)
            {
                metrics.RecordReservationOperation(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
                metrics.RecordReservationFailure(OperationName, ReservationNotFound, InventoryErrorClass.Business);
                logger.LogWarning(
                    "Release batch failed because reservation was not found. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                    command.CorrelationId,
                    command.ReservationId,
                    ReservationNotFound,
                    InventoryErrorClass.Business);

                if (!command.IsExpiry)
                {
                    await UpsertDlqAsync(command, null, ReservationNotFound, "Reservation not found.", cancellationToken);
                }

                return new ReleaseBatchResult(false, ReservationNotFound, "Reservation not found.");
            }

            orderId = reservation.OrderId;

            // Release idempotent olmalı; aynı rezervasyon ikinci kez stok değiştirmemeli.
            if (reservation.Status is ReservationStatus.Released or ReservationStatus.Expired)
            {
                metrics.RecordReservationOperation(OperationName, "idempotent", Stopwatch.GetElapsedTime(startedAt));
                logger.LogInformation(
                    "Release batch skipped because reservation was already released. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, Status: {Status}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                    command.CorrelationId,
                    command.ReservationId,
                    reservation.Status,
                    "IdempotentRelease",
                    InventoryErrorClass.Business);

                return new ReleaseBatchResult(true, null, null);
            }

            // Confirmed rezervasyon release edilemez; stok artık satılmış kabul edilir.
            if (reservation.Status is not ReservationStatus.Pending)
            {
                metrics.RecordReservationOperation(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
                metrics.RecordReservationFailure(OperationName, InvalidReservationState, InventoryErrorClass.Business);
                logger.LogWarning(
                    "Release batch failed because reservation state is invalid. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, Status: {Status}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                    command.CorrelationId,
                    command.ReservationId,
                    reservation.Status,
                    InvalidReservationState,
                    InventoryErrorClass.Business);

                if (!command.IsExpiry)
                {
                    await UpsertDlqAsync(command, orderId, InvalidReservationState, "Reservation must be pending to release.", cancellationToken);
                }

                return new ReleaseBatchResult(false, InvalidReservationState, "Reservation must be pending to release.");
            }

            // Request item'ları sadece doğrulama içindir; gerçek release kaynağı reservation kaydıdır.
            if (command.Items.Count > 0 && !ItemsMatchReservation(command.Items, reservation.Items))
            {
                metrics.RecordReservationOperation(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
                metrics.RecordReservationFailure(OperationName, ItemMismatch, InventoryErrorClass.Business);
                logger.LogWarning(
                    "Release batch failed because request items do not match reservation items. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                    command.CorrelationId,
                    command.ReservationId,
                    ItemMismatch,
                    InventoryErrorClass.Business);

                if (!command.IsExpiry)
                {
                    await UpsertDlqAsync(command, orderId, ItemMismatch, "Request items do not match reservation items.", cancellationToken);
                }

                return new ReleaseBatchResult(false, ItemMismatch, "Request items do not match reservation items.");
            }

            // Lock key'ler DB'deki reservation item'larından üretilir; client input'una güvenilmez.
            var releaseItems = AggregateReservationItems(reservation.Items);
            var inventoryStockLockKeys = CreateInventoryStockLockKeys(releaseItems);

            // Aynı SKU/depo için paralel reserve/release/confirm işlemleri sıraya alınır.
            await using var lockHandle = await distributedLockService.AcquireAsync(
                inventoryStockLockKeys,
                LockExpiry,
                LockWaitTimeout,
                cancellationToken);

            // UnitOfWork callback'i değer döndüremediği için sonuç dış değişkende tutulur.
            ReleaseBatchResult? transactionResult = null;

            await inventoryUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
            {
                // Lock alındıktan sonra state tekrar okunur; bekleme sırasında başka request release etmiş olabilir.
                var currentReservation = await reservationRepository.GetByReservationIdAsync(command.ReservationId, transactionCancellationToken);

                // Transaction içindeki tekrar okuma başarısızsa mutation yapılmaz.
                if (currentReservation is null)
                {
                    transactionResult = new ReleaseBatchResult(false, ReservationNotFound, "Reservation not found.");
                    return;
                }

                // Lock beklerken başka request release etmişse başarılı no-op döneriz.
                if (currentReservation.Status is ReservationStatus.Released or ReservationStatus.Expired)
                {
                    transactionResult = new ReleaseBatchResult(true, null, null);
                    return;
                }

                // Pending dışındaki state'ler stok geri açma için güvenli değildir.
                if (currentReservation.Status is not ReservationStatus.Pending)
                {
                    transactionResult = new ReleaseBatchResult(false, InvalidReservationState, "Reservation must be pending to release.");
                    return;
                }

                // Lock beklerken reservation içeriği değişirse eski request ile devam etmeyiz.
                if (!ItemsMatchReservation(command.Items, currentReservation.Items))
                {
                    transactionResult = new ReleaseBatchResult(false, ItemMismatch, "Request items do not match reservation items.");
                    return;
                }

                // Önce tüm stok satırları doğrulanır; sonra update yapılır. Böylece partial update riski azalır.
                var currentReleaseItems = AggregateReservationItems(currentReservation.Items);
                var inventoryItems = new List<Domain.Inventory.InventoryItem>();

                foreach (var releaseItem in currentReleaseItems)
                {
                    // Her reservation item'ı karşılığında inventory satırı bulunmalı.
                    var inventoryItem = await inventoryItemRepository.GetBySkuAndWarehouseAsync(
                        releaseItem.Sku,
                        releaseItem.WarehouseId,
                        transactionCancellationToken);

                    if (inventoryItem is null)
                    {
                        transactionResult = new ReleaseBatchResult(false, StockNotFound, "Stock was not found.");
                        return;
                    }

                    // Data drift varsa reserved negatif olmasın diye mutation öncesi kontrol edilir.
                    if (inventoryItem.QuantityReserved < releaseItem.Quantity)
                    {
                        transactionResult = new ReleaseBatchResult(
                            false,
                            ReservedStockInsufficient,
                            "Reserved stock is insufficient to release.");

                        return;
                    }

                    inventoryItems.Add(inventoryItem);
                }

                foreach (var releaseItem in currentReleaseItems)
                {
                    // Doğrulanan inventory satırı bulunur ve domain metodu ile stok geri açılır.
                    var inventoryItem = inventoryItems.Single(item =>
                        string.Equals(item.Sku, releaseItem.Sku, StringComparison.Ordinal) &&
                        string.Equals(item.WarehouseId, releaseItem.WarehouseId, StringComparison.Ordinal));

                    // Release: reserved azalır, available artar.
                    inventoryItem.Release(releaseItem.Quantity);

                    await inventoryItemRepository.UpdateAsync(inventoryItem, transactionCancellationToken);

                    // Release işlemi için inventory transaction kaydı oluşturulur; stok hareketi trace edilebilir olur.
                    var transaction = new InventoryTransaction(
                        releaseItem.Sku,
                        releaseItem.WarehouseId,
                        InventoryTransactionType.Release,
                        releaseItem.Quantity,
                        -releaseItem.Quantity,
                        command.CorrelationId,
                        currentReservation.ReservationId,
                        currentReservation.OrderId,
                        // Expiry vs Release audit sebebi ayni branch'te karar verilir.
                        command.IsExpiry ? "Expired" : "Released");

                    await inventoryTransactionRepository.AddAsync(transaction, transactionCancellationToken);
                }

                // IsExpiry true => Expire() status = Expired, audit "Expired" (background job).
                // IsExpiry false => Release() status = Released, audit "Released" (client cancel/manual).
                if (command.IsExpiry)
                {
                    currentReservation.Expire();
                }
                else
                {
                    currentReservation.Release();
                }
                await reservationRepository.UpdateAsync(currentReservation, transactionCancellationToken);

                // Stok, audit ve reservation status aynı transaction içinde başarıyla tamamlandı.
                transactionResult = new ReleaseBatchResult(true, null, null);
            }, cancellationToken);

            // Defensive fallback: callback hiç sonuç yazmazsa belirsiz başarı dönmeyelim.
            var result = transactionResult ?? new ReleaseBatchResult(false, SystemError, "Release batch failed due to an unexpected transaction result.");
            RecordResult(result, startedAt);

            if (!result.Success && !command.IsExpiry)
            {
                await UpsertDlqAsync(command, orderId, result.ErrorCode ?? SystemError, result.ErrorMessage ?? "Release batch transaction failed.", cancellationToken);
            }

            return result;
        }
        catch (TimeoutException exception)
        {
            metrics.RecordReservationOperation(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordReservationFailure(OperationName, LockTimeout, InventoryErrorClass.Timeout);
            // Redis lock zamanında alınamazsa stok işlemine hiç başlanmaz.
            logger.LogWarning(
                exception,
                "Release batch failed while waiting for inventory locks. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                command.CorrelationId,
                command.ReservationId,
                LockTimeout,
                InventoryErrorClass.Timeout);

            if (!command.IsExpiry)
            {
                await UpsertDlqAsync(command, orderId, LockTimeout, "Timed out while waiting for inventory locks.", cancellationToken);
            }

            return new ReleaseBatchResult(false, LockTimeout, "Timed out while waiting for inventory locks.");
        }
        catch (OperationCanceledException exception)
        {
            // Cancellation caller'a geri fırlatılır; iptal edilmiş işlem başarı gibi gösterilmez.
            logger.LogWarning(
                exception,
                "Release batch was cancelled. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}",
                command.CorrelationId,
                command.ReservationId);

            throw;
        }
        catch (InventoryStoreUnavailableException exception)
        {
            metrics.RecordReservationOperation(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordReservationFailure(OperationName, InventoryStoreUnavailable, InventoryErrorClass.Transient);
            // Repository katmanı Mongo transient hatalarını bu exception ile yukarı taşır.
            logger.LogError(
                exception,
                "Release batch failed due to inventory store unavailability. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                command.CorrelationId,
                command.ReservationId,
                "TransientMongoError",
                InventoryErrorClass.Transient);

            if (!command.IsExpiry)
            {
                await UpsertDlqAsync(command, orderId, InventoryStoreUnavailable, "Release batch failed due to inventory store unavailability.", cancellationToken);
            }

            return new ReleaseBatchResult(false, InventoryStoreUnavailable, "Inventory store is unavailable.");
        }
        catch (Exception exception)
        {
            metrics.RecordReservationOperation(OperationName, "failed", Stopwatch.GetElapsedTime(startedAt));
            metrics.RecordReservationFailure(OperationName, SystemError, InventoryErrorClass.System);
            // Beklenmeyen hatalarda detay loglanır ama dışarı genel sistem hatası döner.
            logger.LogError(
                exception,
                "Release batch failed with an unexpected system error. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}, ErrorClass: {ErrorClass}",
                command.CorrelationId,
                command.ReservationId,
                "UnexpectedSystemError",
                InventoryErrorClass.System);

            if (!command.IsExpiry)
            {
                await UpsertDlqAsync(command, orderId, SystemError, "Release batch failed due to an unexpected system error.", cancellationToken);
            }

            return new ReleaseBatchResult(false, SystemError, "Release batch failed due to an unexpected system error.");
        }
    }

    private async Task UpsertDlqAsync(
        ReleaseBatchCommand command,
        string? orderId,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var payloadJson = JsonSerializer.Serialize(command);
            var dlqRecord = new DeadLetterRecord(
                operationType: "CancelRelease",
                reason: errorMessage,
                errorCategory: errorCode,
                correlationId: command.CorrelationId,
                reservationId: command.ReservationId,
                orderId: orderId,
                retryCount: 0,
                payloadSnapshot: payloadJson);

            await deadLetterQueueRepository.UpsertFailureAsync(dlqRecord, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to write CancelRelease to DLQ database in ReleaseBatchCommandHandler. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}",
                command.CorrelationId,
                command.ReservationId);
        }
    }

    private void RecordResult(ReleaseBatchResult result, long startedAt)
    {
        var duration = Stopwatch.GetElapsedTime(startedAt);

        if (result.Success)
        {
            metrics.RecordReservationOperation(OperationName, "success", duration);
            return;
        }

        var errorClass = result.ErrorCode == ValidationFailure ? InventoryErrorClass.Validation : InventoryErrorClass.Business;
        metrics.RecordReservationOperation(OperationName, "failed", duration);
        metrics.RecordReservationFailure(OperationName, result.ErrorCode ?? SystemError, errorClass);
    }

    private static ReleaseBatchResult? Validate(ReleaseBatchCommand command)
    {
        // ReservationId olmadan hangi kaydın release edileceği bilinemez.
        if (string.IsNullOrWhiteSpace(command.ReservationId))
            return new ReleaseBatchResult(false, ValidationFailure, "Reservation ID is required.");

        // CorrelationId log/trace takibi için zorunlu tutulur.
        if (string.IsNullOrWhiteSpace(command.CorrelationId))
            return new ReleaseBatchResult(false, ValidationFailure, "Correlation ID is required.");

        // ReleaseBatch contract'ı items[] taşır; boş liste belirsiz "hepsini release et" anlamına gelmesin.
        if (command.Items.Count == 0)
            return new ReleaseBatchResult(false, ValidationFailure, "Items are required.");

        // Item değerleri DB kaydıyla ayrıca eşleştirilecek; burada sadece temel shape doğrulanır.
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
        // Aynı SKU/depo için tek lock alınır; sıralama deadlock riskini azaltır.
        return requestedItems
            .Select(requestedItem => $"inventory:{requestedItem.Sku}:{requestedItem.WarehouseId}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static ReleaseBatchItemCommand[] AggregateRequestedItems(IEnumerable<ReleaseBatchItemCommand> items)
    {
        // Client aynı SKU/depo'yu birden fazla satırda gönderebilir; business olarak tek satıra indirilir.
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
        // Reservation içindeki tekrar eden SKU/depo satırları da aynı şekilde normalize edilir.
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
        // Karşılaştırma aggregate edilmiş listeler üzerinden yapılır; satır bölünmesi false mismatch üretmesin.
        var aggregatedRequestedItems = AggregateRequestedItems(requestedItems);
        var aggregatedReservationItems = AggregateReservationItems(reservationItems);

        // Farklı sayıda SKU/depo varsa release isteği reservation ile aynı değildir.
        if (aggregatedRequestedItems.Length != aggregatedReservationItems.Length)
            return false;

        foreach (var requestedItem in aggregatedRequestedItems)
        {
            // SKU ve warehouse birebir aynı olmalı; case-sensitive karşılaştırma mevcut lock/key mantığıyla uyumludur.
            var matchingReservationItem = aggregatedReservationItems.FirstOrDefault(reservationItem =>
                    string.Equals(reservationItem.Sku, requestedItem.Sku, StringComparison.Ordinal) &&
                    string.Equals(reservationItem.WarehouseId, requestedItem.WarehouseId, StringComparison.Ordinal));

            if (matchingReservationItem is null)
                return false;

            // Quantity farklıysa client farklı stok release etmeye çalışıyor demektir.
            if (matchingReservationItem.Quantity != requestedItem.Quantity)
                return false;
        }

        return true;
    }

}
