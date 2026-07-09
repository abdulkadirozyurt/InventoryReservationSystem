using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Serializers;

namespace InventoryService.Domain.Reservations;

// temporary stock keeping record for an order
// BsonClassMap.SetCreator ile Mongo driver parametresiz ctor kullanir; public ctor ise invariant korur (Id/OrderId bos olamaz, expiresAt future, en az 1 item).
public sealed class Reservation
{
    // BsonClassMap.RegisterClassMap + SetCreator ile Mongo driver'in Reflection ile deserialize ettigi nesne garantili private ctor kullanir.
    static Reservation()
    {
        if (!BsonClassMap.IsClassMapRegistered(typeof(Reservation)))
        {
            BsonClassMap.RegisterClassMap<Reservation>(classMap =>
            {
                classMap.AutoMap();
                classMap.SetCreator(() => new Reservation());
            });
        }
    }

    // Sadece MongoDB deserialization icin. Public constructor invariant'lari korur, domain disindan cagrilmaz.
    private Reservation()
    {
    }

    public Reservation(string newReservationId, string newOrderId, List<ReservationItem> newItems, DateTime newExpiresAt)
    {
        if (newItems is null)
            throw new ArgumentNullException(nameof(newItems));

        var itemList = newItems.ToList();

        if (string.IsNullOrWhiteSpace(newReservationId))
            throw new ArgumentException("Reservation ID cannot be empty.", nameof(newReservationId));

        if (string.IsNullOrWhiteSpace(newOrderId))
            throw new ArgumentException("Order ID cannot be empty.", nameof(newOrderId));

        if (itemList.Count == 0)
            throw new ArgumentException("Reservation must contain at least one item.", nameof(newItems));

        if (newExpiresAt <= DateTime.UtcNow)
            throw new ArgumentException("Expiration date must be in the future.", nameof(newExpiresAt));

        ReservationId = newReservationId;
        OrderId = newOrderId;
        Items = itemList;
        Status = ReservationStatus.Pending;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
        ExpiresAt = newExpiresAt;
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