using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Inventory.Exceptions;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Application.Reservations.Results.Release;
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
    ILogger<ReleaseBatchCommandHandler> logger)
{
    // Response error code'ları API contract'a sabit ve aranabilir hata döndürmek için tutulur.
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
        // Basit input hataları lock/DB işlemine girmeden dönülür.
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
            // İlk okuma lock anahtarlarını belirlemek ve hızlı state kararı vermek içindir.
            var reservation = await reservationRepository.GetByReservationIdAsync(command.ReservationId, cancellationToken);

            // Reservation yoksa stok üzerinde hiçbir işlem yapılmaz.
            if (reservation is null)
            {
                logger.LogWarning(
                    "Release batch failed because reservation was not found. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}",
                    command.CorrelationId,
                    command.ReservationId,
                    ReservationNotFound);

                return new ReleaseBatchResult(false, ReservationNotFound, "Reservation not found.");
            }

            // Release idempotent olmalı; aynı rezervasyon ikinci kez stok değiştirmemeli.
            if (reservation.Status is ReservationStatus.Released or ReservationStatus.Expired)
            {
                logger.LogInformation(
                    "Release batch skipped because reservation was already released. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, Status: {Status}, ErrorCategory: {ErrorCategory}",
                    command.CorrelationId,
                    command.ReservationId,
                    reservation.Status,
                    "IdempotentRelease");

                return new ReleaseBatchResult(true, null, null);
            }

            // Confirmed rezervasyon release edilemez; stok artık satılmış kabul edilir.
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

            // Request item'ları sadece doğrulama içindir; gerçek release kaynağı reservation kaydıdır.
            if (command.Items.Count > 0 && !ItemsMatchReservation(command.Items, reservation.Items))
            {
                logger.LogWarning(
                    "Release batch failed because request items do not match reservation items. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}",
                    command.CorrelationId,
                    command.ReservationId,
                    ItemMismatch);

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
                        null);

                    await inventoryTransactionRepository.AddAsync(transaction, transactionCancellationToken);
                }

                currentReservation.Release();
                await reservationRepository.UpdateAsync(currentReservation, transactionCancellationToken);

                // Stok, audit ve reservation status aynı transaction içinde başarıyla tamamlandı.
                transactionResult = new ReleaseBatchResult(true, null, null);
            }, cancellationToken);

            // Defensive fallback: callback hiç sonuç yazmazsa belirsiz başarı dönmeyelim.
            return transactionResult ?? new ReleaseBatchResult(false, SystemError, "Release batch failed due to an unexpected transaction result.");
        }
        catch (TimeoutException exception)
        {
            // Redis lock zamanında alınamazsa stok işlemine hiç başlanmaz.
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
            // Repository katmanı Mongo transient hatalarını bu exception ile yukarı taşır.
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
            // Beklenmeyen hatalarda detay loglanır ama dışarı genel sistem hatası döner.
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
        // ReservationId olmadan hangi kaydın release edileceği bilinemez.
        if (string.IsNullOrWhiteSpace(command.ReservationId))
            return new ReleaseBatchResult(false, ValidationFailure, "Reservation ID is required.");

        // CorrelationId log/trace takibi için zorunlu tutulur.
        if (string.IsNullOrWhiteSpace(command.CorrelationId))
            return new ReleaseBatchResult(false, ValidationFailure, "Correlation ID is required.");

        // Items boş olabilir; reservation kaydı zaten gerçek item listesini taşır.
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
