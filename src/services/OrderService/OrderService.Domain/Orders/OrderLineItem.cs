namespace OrderService.Domain.Orders;

public sealed class OrderLineItem
{
    private OrderLineItem()
    {
        Sku = null!;
        WarehouseId = null!;
    }

    public OrderLineItem(string sku, string warehouseId, int requestedQuantity)
    {
        if (string.IsNullOrWhiteSpace(sku))
            throw new ArgumentException("SKU cannot be empty.", nameof(sku));
        if (string.IsNullOrWhiteSpace(warehouseId))
            throw new ArgumentException("Warehouse ID cannot be empty.", nameof(warehouseId));
        if (requestedQuantity <= 0)
            throw new ArgumentException("Requested quantity must be greater than zero.", nameof(requestedQuantity));

        Sku = sku;
        WarehouseId = warehouseId;
        RequestedQuantity = requestedQuantity;
        ReservedQuantity = 0;
    }

    public string Sku { get; private set; }
    public string WarehouseId { get; private set; }
    public int RequestedQuantity { get; private set; }
    public int ReservedQuantity { get; private set; }

    public void SetReservedQuantity(int reservedQuantity)
    {
        if (reservedQuantity < 0)
            throw new ArgumentException("Reserved quantity cannot be negative.", nameof(reservedQuantity));
        if (reservedQuantity > RequestedQuantity)
            throw new ArgumentException("Reserved quantity cannot exceed requested quantity.", nameof(reservedQuantity));

        ReservedQuantity = reservedQuantity;
    }
}
