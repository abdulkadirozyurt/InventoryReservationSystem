namespace OrderService.Infrastructure.Idempotency;

public sealed class IdempotencyEntry
{
    public required string State { get; init; }
    public required string RequestHash { get; init; }
    public int? StatusCode { get; init; }
    public string? ResponseBody { get; init; }
    public string? ContentType { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public static class IdempotencyEntryStates
{
    public const string Processing = "Processing";
    public const string Completed = "Completed";
}
