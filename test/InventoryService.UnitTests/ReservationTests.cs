using InventoryService.Domain.Reservations;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Xunit;

namespace InventoryService.UnitTests;

public class ReservationTests
{
    private readonly List<ReservationItem> _defaultItems = [new ReservationItem("SKU-1", "WH-1", 5)];

    [Fact]
    public void Constructor_WhenExpiresAtIsPast_ShouldThrowArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new Reservation("res-1", "order-1", _defaultItems, DateTime.UtcNow.AddMinutes(-1)));

        Assert.Contains("future", exception.Message);
    }

    [Fact]
    public void MongoDeserializer_WhenPersistedReservationIsExpired_ShouldBypassCreationInvariant()
    {
        var document = new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() },
            { "reservationId", "res-1" },
            { "orderId", "order-1" },
            { "status", "Pending" },
            { "createdAt", DateTime.UtcNow.AddMinutes(-20) },
            { "expiresAt", DateTime.UtcNow.AddMinutes(-10) },
            { "updatedAt", DateTime.UtcNow.AddMinutes(-20) },
            {
                "items", new BsonArray
                {
                    new BsonDocument
                    {
                        { "Sku", "SKU-1" },
                        { "WarehouseId", "WH-1" },
                        { "Quantity", 5 }
                    }
                }
            }
        };

        var reservation = BsonSerializer.Deserialize<Reservation>(document);

        Assert.Equal("res-1", reservation.ReservationId);
        Assert.Equal("order-1", reservation.OrderId);
        Assert.True(reservation.ExpiresAt <= DateTime.UtcNow);
        Assert.Equal(ReservationStatus.Pending, reservation.Status);
        Assert.Single(reservation.Items);
    }

    [Theory]
    [InlineData(ReservationStatus.Confirmed)]
    [InlineData(ReservationStatus.Released)]
    [InlineData(ReservationStatus.Expired)]
    public void Confirm_WhenNotPending_ShouldThrowInvalidOperationException(ReservationStatus nonPendingStatus)
    {
        var reservation = new Reservation("res-1", "order-1", _defaultItems, DateTime.UtcNow.AddMinutes(5));
        TransitionToStatus(reservation, nonPendingStatus);

        var exception = Assert.Throws<InvalidOperationException>(() => reservation.Confirm());
        Assert.Contains("pending status", exception.Message);
    }

    [Theory]
    [InlineData(ReservationStatus.Confirmed)]
    [InlineData(ReservationStatus.Released)]
    [InlineData(ReservationStatus.Expired)]
    public void Release_WhenNotPending_ShouldThrowInvalidOperationException(ReservationStatus nonPendingStatus)
    {
        var reservation = new Reservation("res-1", "order-1", _defaultItems, DateTime.UtcNow.AddMinutes(5));
        TransitionToStatus(reservation, nonPendingStatus);

        var exception = Assert.Throws<InvalidOperationException>(() => reservation.Release());
        Assert.Contains("pending status", exception.Message);
    }

    [Theory]
    [InlineData(ReservationStatus.Confirmed)]
    [InlineData(ReservationStatus.Released)]
    [InlineData(ReservationStatus.Expired)]
    public void Expire_WhenNotPending_ShouldThrowInvalidOperationException(ReservationStatus nonPendingStatus)
    {
        var reservation = new Reservation("res-1", "order-1", _defaultItems, DateTime.UtcNow.AddMinutes(5));
        TransitionToStatus(reservation, nonPendingStatus);

        var exception = Assert.Throws<InvalidOperationException>(() => reservation.Expire());
        Assert.Contains("pending status", exception.Message);
    }

    private static void TransitionToStatus(Reservation reservation, ReservationStatus status)
    {
        switch (status)
        {
            case ReservationStatus.Confirmed:
                reservation.Confirm();
                break;
            case ReservationStatus.Released:
                reservation.Release();
                break;
            case ReservationStatus.Expired:
                reservation.Expire();
                break;
        }
    }
}
