using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace InventoryService.Domain.Reservations;

// temporary stock keeping record for an order
public sealed class Reservation
{
    private Reservation()
    {
    }

    [BsonConstructor]
    public Reservation(string reservationId, string orderId, List<ReservationItem> items, DateTimeOffset expiresAt)
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

        if (expiresAt <= DateTimeOffset.UtcNow)
            throw new ArgumentException("Expiration date must be in the future.", nameof(expiresAt));

        ReservationId = reservationId;
        OrderId = orderId;
        Items = itemList;
        Status = ReservationStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
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
    public DateTimeOffset CreatedAt { get; private set; }

    [BsonElement("expiresAt")]
    public DateTimeOffset ExpiresAt { get; private set; }

    [BsonElement("updatedAt")]
    public DateTimeOffset UpdatedAt { get; private set; }

    [BsonElement("items")]
    public List<ReservationItem> Items { get; private set; } = [];


    public void Confirm()
    {
        EnsurePending();

        Status = ReservationStatus.Confirmed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Release()
    {
        EnsurePending();

        Status = ReservationStatus.Released;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Expire()
    {
        EnsurePending();

        Status = ReservationStatus.Expired;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void EnsurePending()
    {
        if (Status != ReservationStatus.Pending)
            throw new InvalidOperationException("Reservation can only be confirmed if it is in pending status.");
    }
}