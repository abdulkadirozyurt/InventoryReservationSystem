using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Serializers;

namespace InventoryService.Domain.Reservations;

// temporary stock keeping record for an order
public sealed class Reservation
{
    private Reservation()
    {
    }

    public Reservation(string reservationId, string orderId, List<ReservationItem> items, DateTime expiresAt)
    {
        if (items is null)
            throw new ArgumentNullException(nameof(items));

        var itemList = items.ToList();

        if (string.IsNullOrWhiteSpace(reservationId))
            throw new ArgumentException("Reservation ID cannot be empty.", nameof(reservationId));

        if (string.IsNullOrWhiteSpace(orderId))
            throw new ArgumentException("Order ID cannot be empty.", nameof(orderId));

        if (itemList.Count == 0)
            throw new ArgumentException("Reservation must contain at least one item.", nameof(items));

        if (expiresAt <= DateTime.UtcNow)
            throw new ArgumentException("Expiration date must be in the future.", nameof(expiresAt));

        ReservationId = reservationId;
        OrderId = orderId;
        Items = itemList;
        Status = ReservationStatus.Pending;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
        ExpiresAt = expiresAt;
    }

    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; private set; } = string.Empty;

    [BsonElement("reservationId")]
    public string ReservationId { get; private set; } = string.Empty;

    [BsonElement("orderId")]
    public string OrderId { get; private set; } = string.Empty;

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public ReservationStatus Status { get; private set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; private set; }

    [BsonElement("expiresAt")]
    public DateTime ExpiresAt { get; private set; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; private set; }

    [BsonElement("items")]
    public List<ReservationItem> Items { get; private set; } = [];


    public void Confirm()
    {
        EnsurePending();

        Status = ReservationStatus.Confirmed;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Release()
    {
        EnsurePending();

        Status = ReservationStatus.Released;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Expire()
    {
        EnsurePending();

        Status = ReservationStatus.Expired;
        UpdatedAt = DateTime.UtcNow;
    }

    private void EnsurePending()
    {
        if (Status != ReservationStatus.Pending)
            throw new InvalidOperationException("Reservation can only be confirmed if it is in pending status.");
    }
}