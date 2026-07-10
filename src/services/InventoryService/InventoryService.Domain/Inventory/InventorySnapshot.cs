using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace InventoryService.Domain.Inventory;

public sealed class InventorySnapshot
{
    private InventorySnapshot()
    {
        SnapshotId = null!;
        RequestedBy = null!;
        CorrelationId = null!;
        Items = null!;
    }

    public InventorySnapshot(
        string snapshotId,
        DateTime createdAt,
        string requestedBy,
        string correlationId,
        List<SnapshotItem> items)
    {
        if (string.IsNullOrWhiteSpace(snapshotId))
            throw new ArgumentException("Snapshot ID cannot be empty.", nameof(snapshotId));
        if (string.IsNullOrWhiteSpace(requestedBy))
            throw new ArgumentException("Requested by cannot be empty.", nameof(requestedBy));
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("Correlation ID cannot be empty.", nameof(correlationId));
        ArgumentNullException.ThrowIfNull(items);

        SnapshotId = snapshotId;
        CreatedAt = createdAt;
        RequestedBy = requestedBy;
        CorrelationId = correlationId;
        Items = items;
    }

    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; private set; }

    [BsonElement("snapshotId")]
    public string SnapshotId { get; private set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; private set; }

    [BsonElement("requestedBy")]
    public string RequestedBy { get; private set; }

    [BsonElement("correlationId")]
    public string CorrelationId { get; private set; }

    [BsonElement("items")]
    public List<SnapshotItem> Items { get; private set; }
}

public sealed class SnapshotItem
{
    private SnapshotItem()
    {
        Sku = null!;
        WarehouseId = null!;
    }

    public SnapshotItem(string sku, string warehouseId, int quantityAvailable, int quantityReserved)
    {
        if (string.IsNullOrWhiteSpace(sku))
            throw new ArgumentException("SKU cannot be empty.", nameof(sku));
        if (string.IsNullOrWhiteSpace(warehouseId))
            throw new ArgumentException("Warehouse ID cannot be empty.", nameof(warehouseId));
        if (quantityAvailable < 0)
            throw new ArgumentException("Quantity available cannot be negative.", nameof(quantityAvailable));
        if (quantityReserved < 0)
            throw new ArgumentException("Quantity reserved cannot be negative.", nameof(quantityReserved));

        Sku = sku;
        WarehouseId = warehouseId;
        QuantityAvailable = quantityAvailable;
        QuantityReserved = quantityReserved;
    }

    [BsonElement("sku")]
    public string Sku { get; private set; }

    [BsonElement("warehouseId")]
    public string WarehouseId { get; private set; }

    [BsonElement("quantityAvailable")]
    public int QuantityAvailable { get; private set; }

    [BsonElement("quantityReserved")]
    public int QuantityReserved { get; private set; }
}
