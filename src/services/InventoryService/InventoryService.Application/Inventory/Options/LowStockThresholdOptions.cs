namespace InventoryService.Application.Inventory.Options;

// LowStockThresholdOptions: Stok eşik değeri konfigürasyonu.
// API appsettings.json altında "LowStockThreshold" section'ından okunur.
// Varsayılan eşik 10 birimdir; bu değer veya altındaki stok seviyeleri uyarı log'u üretir.
public sealed class LowStockThresholdOptions
{
    public const string SectionName = "LowStockThreshold";

    public int Threshold { get; set; } = 10;
}
