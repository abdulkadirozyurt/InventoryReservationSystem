using InventoryService.Domain.Reservations;
using Xunit;

namespace InventoryService.UnitTests;

public class ReservationTests
{
    private readonly List<ReservationItem> _defaultItems = [new ReservationItem("SKU-1", "WH-1", 5)];

    [Theory]
    [InlineData(ReservationStatus.Confirmed)]
    [InlineData(ReservationStatus.Released)]
    [InlineData(ReservationStatus.Expired)]
    public void Confirm_WhenNotPending_ShouldThrowInvalidOperationException(ReservationStatus nonPendingStatus)
    {
        // Arrange
        var reservation = new Reservation("res-1", "order-1", _defaultItems, DateTime.UtcNow.AddMinutes(10));
        TransitionToStatus(reservation, nonPendingStatus);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => reservation.Confirm());
        Assert.Contains("pending status", exception.Message);
    }

    [Theory]
    [InlineData(ReservationStatus.Confirmed)]
    [InlineData(ReservationStatus.Released)]
    [InlineData(ReservationStatus.Expired)]
    public void Release_WhenNotPending_ShouldThrowInvalidOperationException(ReservationStatus nonPendingStatus)
    {
        // Arrange
        var reservation = new Reservation("res-1", "order-1", _defaultItems, DateTime.UtcNow.AddMinutes(10));
        TransitionToStatus(reservation, nonPendingStatus);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => reservation.Release());
        Assert.Contains("pending status", exception.Message);
    }

    [Theory]
    [InlineData(ReservationStatus.Confirmed)]
    [InlineData(ReservationStatus.Released)]
    [InlineData(ReservationStatus.Expired)]
    public void Expire_WhenNotPending_ShouldThrowInvalidOperationException(ReservationStatus nonPendingStatus)
    {
        // Arrange
        var reservation = new Reservation("res-1", "order-1", _defaultItems, DateTime.UtcNow.AddMinutes(10));
        TransitionToStatus(reservation, nonPendingStatus);

        // Act & Assert
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
