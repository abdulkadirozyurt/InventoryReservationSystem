using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace InventoryService.Domain.DeadLetterQueue;

public sealed class DeadLetterRecord
{
    private DeadLetterRecord()
    {
        OperationType = null!;
        Reason = null!;
        ErrorCategory = null!;
        CorrelationId = null!;
    }

    public DeadLetterRecord(
        string operationType,
        string reason,
        string errorCategory,
        string correlationId,
        string? reservationId,
        string? orderId,
        int retryCount,
        string? payloadSnapshot)
    {
        OperationType = operationType;
        Reason = reason;
        ErrorCategory = errorCategory;
        CorrelationId = correlationId;
        ReservationId = reservationId;
        OrderId = orderId;
        RetryCount = retryCount;
        PayloadSnapshot = payloadSnapshot;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; private set; }
    public string OperationType { get; private set; }
    public string Reason { get; private set; }
    public string ErrorCategory { get; private set; }
    public string CorrelationId { get; private set; }
    public string? ReservationId { get; private set; }
    public string? OrderId { get; private set; }
    public int RetryCount { get; private set; }
    public string? PayloadSnapshot { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
}
