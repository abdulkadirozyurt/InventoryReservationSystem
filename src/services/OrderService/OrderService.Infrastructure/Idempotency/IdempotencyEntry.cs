namespace OrderService.Infrastructure.Idempotency;

public sealed class IdempotencyEntry
{
    public required string State { get; init; }
    public required string RequestHash { get; init; }
    public int? StatusCode { get; init; }
    public string? ResponseBody { get; init; }
    public string? ContentType { get; set; }
    public DateTimeOffset? CreatedAtUTC { get; init; }
}

public static class IdempotencyEntryStates
{
    public const string Processing = "Processing";
    public const string Completed = "Completed";
}
