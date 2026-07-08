namespace OrderService.Domain.Orders;

public sealed class OrderHistory
{
    private OrderHistory()
    {
        OrderNumber = string.Empty;
        CorrelationId = string.Empty;
        Reason = string.Empty;
    }

    public OrderHistory(
        string orderNumber,
        OrderStatus? fromStatus,
        OrderStatus toStatus,
        string correlationId,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
            throw new ArgumentException("Order number cannot be empty.", nameof(orderNumber));
        if (fromStatus.HasValue && !Enum.IsDefined(fromStatus.Value))
            throw new ArgumentException("From status must be defined if provided.", nameof(fromStatus));
        if (!Enum.IsDefined(toStatus))
            throw new ArgumentException("To status must be defined.", nameof(toStatus));
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("Correlation ID cannot be empty.", nameof(correlationId));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason cannot be empty.", nameof(reason));

        OrderNumber = orderNumber;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        CorrelationId = correlationId;
        Reason = reason;
        ChangedAt = DateTime.UtcNow;
    }

    public string Id { get; private set; } = string.Empty;
    public string OrderNumber { get; private set; }
    public OrderStatus? FromStatus { get; private set; }
    public OrderStatus ToStatus { get; private set; }
    public DateTime ChangedAt { get; private set; }
    public string CorrelationId { get; private set; }
    public string Reason { get; private set; }
}
