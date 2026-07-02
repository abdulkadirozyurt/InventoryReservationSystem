namespace InventoryService.Domain.Reservations;

public sealed class ReservationItem
{
    private ReservationItem()
    {
        Sku = null!;
        WarehouseId = null!;
    }
    public ReservationItem(string sku, string warehouseId, int quantity)
    {
        if (string.IsNullOrWhiteSpace(sku))
            throw new ArgumentException("SKU cannot be empty.", nameof(sku));
        if (string.IsNullOrWhiteSpace(warehouseId))
            throw new ArgumentException("Warehouse ID cannot be empty.", nameof(warehouseId));
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be greater than zero.", nameof(quantity));

        Sku = sku;
        WarehouseId = warehouseId;
        Quantity = quantity;
    }

    public string Sku { get; private set; }
    public string WarehouseId { get; private set; }
    public int Quantity { get; private set; }
}