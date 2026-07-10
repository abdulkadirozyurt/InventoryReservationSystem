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
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
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
    public DateTime CreatedAt { get; private set; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; private set; }



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
        UpdatedAt = DateTime.UtcNow;
    }

    public void Release(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity to release must be greater than zero.", nameof(quantity));

        if (quantity > QuantityReserved)
            throw new InvalidOperationException("Not enough quantity reserved to release.");

        QuantityReserved -= quantity;
        QuantityAvailable += quantity;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Confirm(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity to confirm must be greater than zero.", nameof(quantity));
        if (quantity > QuantityReserved)
            throw new InvalidOperationException("Not enough quantity reserved to confirm.");

        QuantityReserved -= quantity;
        UpdatedAt = DateTime.UtcNow;
    }

    public void IncreaseStock(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity to increase must be greater than zero.", nameof(quantity));

        QuantityAvailable += quantity;
        UpdatedAt = DateTime.UtcNow;
    }

    public void DecreaseStock(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity to decrease must be greater than zero.", nameof(quantity));
        if (QuantityAvailable - quantity < 0)
            throw new InvalidOperationException("Quantity available cannot be negative.");

        QuantityAvailable -= quantity;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RestoreQuantities(int quantityAvailable, int quantityReserved)
    {
        if (quantityAvailable < 0)
            throw new ArgumentException("Quantity available cannot be negative.", nameof(quantityAvailable));
        if (quantityReserved < 0)
            throw new ArgumentException("Quantity reserved cannot be negative.", nameof(quantityReserved));

        QuantityAvailable = quantityAvailable;
        QuantityReserved = quantityReserved;
        UpdatedAt = DateTime.UtcNow;
    }
}

