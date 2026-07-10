using InventoryService.Application.Inventory.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InventoryService.Application.Inventory.Services;

// LowStockAlertService: Stok değiştiren işlemler sonrası SKU+depo bazında QuantityAvailable <= eşik kontrolü yapar.
// Bildirim altyapısı kurmuyoruz; Faz 5.5 için aranabilir structured warning log yeterli.
public sealed class LowStockAlertService(IOptions<LowStockThresholdOptions> options, ILogger<LowStockAlertService> logger)
{
    private readonly int _threshold = options.Value.Threshold;

    public void Check(string operation, string correlationId, string sku, string warehouseId, int quantityAvailable)
    {
        if (quantityAvailable > _threshold)
            return;

        logger.LogWarning(
            "Low stock alert triggered. CorrelationId: {CorrelationId}, Sku: {Sku}, WarehouseId: {WarehouseId}, QuantityAvailable: {QuantityAvailable}, Threshold: {Threshold}, Operation: {Operation}",
            correlationId,
            sku,
            warehouseId,
            quantityAvailable,
            _threshold,
            operation);
    }
}
