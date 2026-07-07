using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace InventoryService.Domain.Inventory;

public sealed class InventoryItem
{
    private InventoryItem()
    {
        Sku = null!;
        WarehouseId = null!;
    }

    public InventoryItem(string sku, string warehouseId, int quantityAvailable)
    {
        if (string.IsNullOrWhiteSpace(sku))
            throw new ArgumentException("SKU cannot be empty.", nameof(sku));

        if (string.IsNullOrWhiteSpace(warehouseId))
            throw new ArgumentException("Warehouse ID cannot be empty.", nameof(warehouseId));

        if (quantityAvailable < 0)
            throw new ArgumentException("Quantity available cannot be negative.", nameof(quantityAvailable));


        Sku = sku;
        WarehouseId = warehouseId;
        QuantityAvailable = quantityAvailable;
        QuantityReserved = 0;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; private set; }

    [BsonElement("sku")]
    public string Sku { get; private set; }

    [BsonElement("warehouseId")]
    public string WarehouseId { get; private set; }

    [BsonElement("quantityAvailable")]
    public int QuantityAvailable { get; private set; }

    [BsonElement("quantityReserved")]
    public int QuantityReserved { get; private set; }

    [BsonElement("createdAt")]
    public DateTimeOffset CreatedAt { get; private set; }

    [BsonElement("updatedAt")]
    public DateTimeOffset UpdatedAt { get; private set; }



    public void Reserve(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("Quantity to reserve must be greater than zero.", nameof(quantity));
        }
        if (quantity > QuantityAvailable)
        {
            throw new InvalidOperationException("Not enough quantity available to reserve.");
        }
        QuantityAvailable -= quantity;
        QuantityReserved += quantity;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Release(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity to release must be greater than zero.", nameof(quantity));

        if (quantity > QuantityReserved)
            throw new InvalidOperationException("Not enough quantity reserved to release.");

        QuantityReserved -= quantity;
        QuantityAvailable += quantity;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Confirm(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity to confirm must be greater than zero.", nameof(quantity));
        if (quantity > QuantityReserved)
            throw new InvalidOperationException("Not enough quantity reserved to confirm.");

        QuantityReserved -= quantity;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

