using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Inventory.Exceptions;
using InventoryService.Application.Inventory.Results;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Domain.InventoryTransactions;
using Microsoft.Extensions.Logging;

namespace InventoryService.Application.Inventory.Services;

public sealed class InventoryStockAdjustmentService(
    IInventoryItemRepository inventoryItemRepository,
    IInventoryTransactionRepository inventoryTransactionRepository,
    IInventoryUnitOfWork unitOfWork,
    IDistributedLockService distributedLockService,
    ILogger<InventoryStockAdjustmentService> logger)
{
    private const string ValidationFailure = "VALIDATION_ERROR";
    private const string StockNotFound = "STOCK_NOT_FOUND";
    private const string InsufficientStock = "INSUFFICIENT_STOCK";
    private const string LockTimeout = "LOCK_TIMEOUT";
    private const string InventoryStoreUnavailable = "INVENTORY_STORE_UNAVAILABLE";
    private const string SystemError = "SYSTEM_ERROR";

    // Lock çok uzun kalmasın diye süre veriyoruz, yoksa bozuk akış stoku kilitli bırakabilir.
    private static readonly TimeSpan LockExpiry = TimeSpan.FromSeconds(30);

    // Lock hemen alınamazsa biraz bekliyoruz, çünkü aynı SKU üzerinde başka işlem olabilir.
    private static readonly TimeSpan LockWaitTimeout = TimeSpan.FromSeconds(5);

    public Task<StockAdjustmentResult> IncreaseAsync(
        string sku,
        string warehouseId,
        int quantity,
        string reason,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // Artırma işleminde quantity aynen pozitif kalıyor.
        return AdjustAsync(sku, warehouseId, quantity, reason, correlationId, isIncrease: true, cancellationToken);
    }

    public Task<StockAdjustmentResult> DecreaseAsync(
        string sku,
        string warehouseId,
        int quantity,
        string reason,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // Azaltma işleminde de dışarıdan quantity pozitif gelir, yönü burada belirliyoruz.
        return AdjustAsync(sku, warehouseId, quantity, reason, correlationId, isIncrease: false, cancellationToken);
    }

    private async Task<StockAdjustmentResult> AdjustAsync(
        string sku,
        string warehouseId,
        int quantity,
        string reason,
        string correlationId,
        bool isIncrease,
        CancellationToken cancellationToken)
    {
        // Önce basit alanları kontrol ediyoruz, gereksiz lock ve db işi yapmayalım.
        var validationResult = Validate(sku, warehouseId, quantity, reason, correlationId);
        if (validationResult is not null)
            return validationResult;

        // Reserve/Release ile aynı stok satırını korumak için aynı lock key formatını kullanıyoruz.
        var lockKey = $"inventory:{sku}:{warehouseId}";

        try
        {
            // Redis lock almadan stok değiştirmiyoruz, yoksa aynı anda gelen işlemler sayıları bozabilir.
            await using var lockHandle = await distributedLockService.AcquireAsync(
                new[] { lockKey },
                LockExpiry,
                LockWaitTimeout,
                cancellationToken);

            StockAdjustmentResult? transactionResult = null;

            // Stok update ve audit aynı transaction içinde olsun, biri yazılıp diğeri kalmasın.
            await unitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
            {
                // İlgili SKU+depo kaydını buluyoruz, yoksa düzeltilecek stok yok demektir.
                var inventoryItem = await inventoryItemRepository.GetBySkuAndWarehouseAsync(sku, warehouseId, transactionCancellationToken);
                if (inventoryItem is null)
                {
                    logger.LogWarning(
                        "Stock adjustment failed because inventory item was not found. CorrelationId: {CorrelationId}, Sku: {Sku}, WarehouseId: {WarehouseId}, ErrorCategory: {ErrorCategory}",
                        correlationId,
                        sku,
                        warehouseId,
                        StockNotFound);

                    transactionResult = Failure(StockNotFound, "Inventory item was not found.", sku, warehouseId);
                    return;
                }

                // Audit için available delta değerini burada netleştiriyoruz.
                var quantityAvailableDelta = isIncrease ? quantity : -quantity;

                if (isIncrease)
                {
                    // Artırmada sadece available artar, reserved tarafına dokunmuyoruz.
                    inventoryItem.IncreaseStock(quantity);
                }
                else
                {
                    // Azaltmada eksi stoka düşmeyi burada erken yakalıyoruz, domain de yine koruyor.
                    if (quantity > inventoryItem.QuantityAvailable)
                    {
                        logger.LogWarning(
                            "Stock adjustment failed because available stock is insufficient. CorrelationId: {CorrelationId}, Sku: {Sku}, WarehouseId: {WarehouseId}, Quantity: {Quantity}, QuantityAvailable: {QuantityAvailable}, ErrorCategory: {ErrorCategory}",
                            correlationId,
                            sku,
                            warehouseId,
                            quantity,
                            inventoryItem.QuantityAvailable,
                            InsufficientStock);

                        transactionResult = Failure(InsufficientStock, "Insufficient stock available.", sku, warehouseId);
                        return;
                    }

                    // Azaltmada available azalır, reserved yine aynı kalır.
                    inventoryItem.DecreaseStock(quantity);
                }

                // Değişen inventory item kaydını transaction içinde yazıyoruz.
                await inventoryItemRepository.UpdateAsync(inventoryItem, transactionCancellationToken);

                // Admin düzeltmesi audit olmadan tamam sayılmaz, o yüzden aynı transaction içinde yazılıyor.
                var transaction = new InventoryTransaction(
                    sku,
                    warehouseId,
                    InventoryTransactionType.AdjustStock,
                    quantityAvailableDelta,
                    0,
                    correlationId,
                    null,
                    null,
                    reason);

                // Audit kaydı sonradan incelenebilsin diye kalıcı olarak ekleniyor.
                await inventoryTransactionRepository.AddAsync(transaction, transactionCancellationToken);

                logger.LogInformation(
                    "Stock adjustment completed. CorrelationId: {CorrelationId}, Sku: {Sku}, WarehouseId: {WarehouseId}, QuantityAvailableDelta: {QuantityAvailableDelta}, QuantityAvailable: {QuantityAvailable}, QuantityReserved: {QuantityReserved}, Reason: {Reason}",
                    correlationId,
                    sku,
                    warehouseId,
                    quantityAvailableDelta,
                    inventoryItem.QuantityAvailable,
                    inventoryItem.QuantityReserved,
                    reason);

                // Başarı sonucunda güncel stok sayılarını dönüyoruz, caller tekrar sorgu yapmak zorunda kalmasın.
                transactionResult = new StockAdjustmentResult(
                    true,
                    null,
                    null,
                    sku,
                    warehouseId,
                    inventoryItem.QuantityAvailable,
                    inventoryItem.QuantityReserved);
            }, cancellationToken);

            // Normalde transactionResult set edilir, set edilmezse beklenmeyen durum sayıyoruz.
            return transactionResult ?? Failure(SystemError, "Stock adjustment failed due to an unexpected transaction result.", sku, warehouseId);
        }
        catch (TimeoutException exception)
        {
            // Lock süresinde alınamazsa stokla oynamıyoruz, güvenli şekilde hata dönüyoruz.
            logger.LogWarning(
                exception,
                "Stock adjustment failed while waiting for inventory lock. CorrelationId: {CorrelationId}, Sku: {Sku}, WarehouseId: {WarehouseId}, LockKey: {LockKey}, ErrorCategory: {ErrorCategory}",
                correlationId,
                sku,
                warehouseId,
                lockKey,
                LockTimeout);

            return Failure(LockTimeout, "Could not acquire inventory lock in time.", sku, warehouseId);
        }
        catch (OperationCanceledException exception)
        {
            // İptal geldiyse başarı gibi göstermiyoruz, üst katman iptali bilsin diye tekrar fırlatıyoruz.
            logger.LogWarning(
                exception,
                "Stock adjustment was cancelled. CorrelationId: {CorrelationId}, Sku: {Sku}, WarehouseId: {WarehouseId}",
                correlationId,
                sku,
                warehouseId);

            throw;
        }
        catch (InventoryStoreUnavailableException exception)
        {
            // Mongo tarafı geçici sorun çıkardıysa client'a kontrollü hata dönüyoruz.
            logger.LogError(
                exception,
                "Stock adjustment failed due to inventory store unavailability. CorrelationId: {CorrelationId}, Sku: {Sku}, WarehouseId: {WarehouseId}, ErrorCategory: {ErrorCategory}",
                correlationId,
                sku,
                warehouseId,
                "TransientMongoError");

            return Failure(InventoryStoreUnavailable, "Inventory store is unavailable.", sku, warehouseId);
        }
        catch (Exception exception)
        {
            // Bilmediğimiz sistem hatasını yutmuyoruz, loglayıp üst katmana bırakıyoruz.
            logger.LogError(
                exception,
                "Stock adjustment failed with an unexpected system error. CorrelationId: {CorrelationId}, Sku: {Sku}, WarehouseId: {WarehouseId}, ErrorCategory: {ErrorCategory}",
                correlationId,
                sku,
                warehouseId,
                "UnexpectedSystemError");

            throw;
        }
    }

    private static StockAdjustmentResult? Validate(string sku, string warehouseId, int quantity, string reason, string correlationId)
    {
        // SKU boşsa hangi stok değişecek belli olmaz.
        if (string.IsNullOrWhiteSpace(sku))
            return Failure(ValidationFailure, "SKU is required.", sku, warehouseId);

        // Depo boşsa multi-warehouse dünyasında yanlış stok satırına dokunabiliriz.
        if (string.IsNullOrWhiteSpace(warehouseId))
            return Failure(ValidationFailure, "Warehouse ID is required.", sku, warehouseId);

        // Quantity sıfır/eksi olamaz, işlem yönünü Increase/Decrease metodu belirliyor.
        if (quantity <= 0)
            return Failure(ValidationFailure, "Quantity must be greater than zero.", sku, warehouseId);

        // Reason audit için şart, admin düzeltmesinin sebebi sonradan görülmeli.
        if (string.IsNullOrWhiteSpace(reason))
            return Failure(ValidationFailure, "Reason is required.", sku, warehouseId);

        // Correlation id loglarda akışı takip etmek için lazım.
        if (string.IsNullOrWhiteSpace(correlationId))
            return Failure(ValidationFailure, "Correlation ID is required.", sku, warehouseId);

        return null;
    }

    private static StockAdjustmentResult Failure(string errorCode, string errorMessage, string sku, string warehouseId)
    {
        // Hatalarda stok sayıları bilinmiyor, bu yüzden 0 dönüyoruz.
        return new StockAdjustmentResult(false, errorCode, errorMessage, sku, warehouseId, 0, 0);
    }
}
