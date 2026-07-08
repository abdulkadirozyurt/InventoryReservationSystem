namespace OrderService.Domain.Orders;

public sealed class Order
{
    private Order()
    {
        OrderNumber = string.Empty;
        Items = [];
    }

    public Order(string orderNumber, List<OrderLineItem> items)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
            throw new ArgumentException("Order number cannot be empty.", nameof(orderNumber));
        if (items is null)
            throw new ArgumentNullException(nameof(items));

        var itemList = items.ToList();
        if (itemList.Count == 0)
            throw new ArgumentException("Order must contain at least one item.", nameof(items));

        OrderNumber = orderNumber;
        Items = itemList;
        Status = OrderStatus.Pending;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public string Id { get; private set; } = string.Empty;
    public string OrderNumber { get; private set; }
    public string? ReservationId { get; private set; }
    public OrderStatus Status { get; private set; }
    public List<OrderLineItem> Items { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public void AttachReservation(string reservationId)
    {
        if (string.IsNullOrWhiteSpace(reservationId))
            throw new ArgumentException("Reservation ID cannot be empty.", nameof(reservationId));
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException("Reservation can only be attached when order is in pending status.");

        ReservationId = reservationId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Confirm()
    {
        EnsurePending();

        Status = OrderStatus.Confirmed;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        EnsurePending();

        Status = OrderStatus.Cancelled;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Expire()
    {
        EnsurePending();

        Status = OrderStatus.Expired;
        UpdatedAt = DateTime.UtcNow;
    }

    private void EnsurePending()
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException("Order status can only transition from pending status.");
    }
}
