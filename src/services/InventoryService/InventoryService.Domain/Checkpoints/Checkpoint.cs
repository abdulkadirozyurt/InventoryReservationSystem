using MongoDB.Bson.Serialization.Attributes;

namespace InventoryService.Domain.Checkpoints;

public sealed class Checkpoint
{
    private Checkpoint()
    {
        JobName = null!;
    }

    public Checkpoint(string jobName, DateTime? cursorTimestamp, string? lastReservationId)
    {
        if (string.IsNullOrWhiteSpace(jobName))
            throw new ArgumentException("Job name cannot be empty.", nameof(jobName));

        JobName = jobName;
        CursorTimestamp = cursorTimestamp;
        LastReservationId = lastReservationId;
        UpdatedAt = DateTime.UtcNow;
    }

    [BsonId]
    public string JobName { get; private set; }
    public DateTime? CursorTimestamp { get; private set; }
    public string? LastReservationId { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public void Update(DateTime? cursorTimestamp, string? lastReservationId)
    {
        CursorTimestamp = cursorTimestamp;
        LastReservationId = lastReservationId;
        UpdatedAt = DateTime.UtcNow;
    }
}
