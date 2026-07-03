using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace InventoryService.Domain.InventoryTransactions;

public sealed class InventoryTransaction
{
    private InventoryTransaction()
    {
        Sku = null!;
        WarehouseId = null!;
        CorrelationId = null!;
    }

    public InventoryTransaction(
        string sku,
        string warehouseId,
        InventoryTransactionType type,
        int quantityAvailableDelta,
        int quantityReservedDelta,
        string correlationId,
        string? reservationId,
        string? orderId,
        string? reason)
    {
        if (string.IsNullOrWhiteSpace(sku))
            throw new ArgumentException("SKU cannot be empty.", nameof(sku));
        if (string.IsNullOrWhiteSpace(warehouseId))
            throw new ArgumentException("Warehouse ID cannot be empty.", nameof(warehouseId));
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("Correlation ID cannot be empty.", nameof(correlationId));
        if (!Enum.IsDefined(type))
            throw new ArgumentException("Invalid inventory transaction type.", nameof(type));
        if (quantityAvailableDelta == 0 && quantityReservedDelta == 0)
            throw new ArgumentException("At least one inventory quantity delta must be non-zero.");
        if (RequiresReason(type) && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason is required for this inventory transaction type.", nameof(reason));


        Sku = sku;
        WarehouseId = warehouseId;
        Type = type;
        QuantityAvailableDelta = quantityAvailableDelta;
        QuantityReservedDelta = quantityReservedDelta;
        CorrelationId = correlationId;
        ReservationId = string.IsNullOrWhiteSpace(reservationId) ? null : reservationId;
        OrderId = string.IsNullOrWhiteSpace(orderId) ? null : orderId;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; private set; }

    [BsonElement("sku")]
    public string Sku { get; private set; }

    [BsonElement("warehouseId")]
    public string WarehouseId { get; private set; }

    [BsonElement("type")]
    [BsonRepresentation(BsonType.String)]
    public InventoryTransactionType Type { get; private set; }

    [BsonElement("quantityAvailableDelta")]
    public int QuantityAvailableDelta { get; private set; }

    [BsonElement("quantityReservedDelta")]
    public int QuantityReservedDelta { get; private set; }

    [BsonElement("correlationId")]
    public string CorrelationId { get; private set; }

    [BsonElement("reservationId")]
    public string? ReservationId { get; private set; }

    [BsonElement("orderId")]
    public string? OrderId { get; private set; }

    [BsonElement("reason")]
    public string? Reason { get; private set; }

    [BsonElement("createdAt")]
    public DateTimeOffset CreatedAt { get; private set; }


    private static bool RequiresReason(InventoryTransactionType type)
    {
        return type is InventoryTransactionType.AdjustStock
            or InventoryTransactionType.Rebalance
            or InventoryTransactionType.SnapshotRestore;
    }
}
