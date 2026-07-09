namespace OrderService.Infrastructure.InventoryGrpc;

/// <summary>
/// Represents the resilience settings used for gRPC calls to InventoryService.
///
/// These values are read from appsettings.json, so retry and circuit breaker
/// behavior can be changed without changing code.
/// </summary>
public sealed class InventoryGrpcResilienceOptions
{
    /// <summary>
    /// Name of the configuration section in appsettings.json.
    /// </summary>
    public const string SectionName = "InventoryGrpcResilience";

    /// <summary>
    /// Maximum number of seconds allowed for one InventoryService gRPC attempt.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 3;

    /// <summary>
    /// Number of extra attempts after the first gRPC call fails.
    /// </summary>
    public int RetryCount { get; init; } = 3;

    /// <summary>
    /// Delay before the first retry attempt, in milliseconds.
    /// Later retry delays grow from this base value.
    /// </summary>
    public int RetryBaseDelayMilliseconds { get; init; } = 200;

    /// <summary>
    /// Minimum number of gRPC calls required during the sampling window
    /// before the circuit breaker can open.
    /// </summary>
    public int CircuitBreakerMinimumThroughput { get; init; } = 5;

    /// <summary>
    /// Time window, in seconds, used by the circuit breaker to inspect calls.
    /// </summary>
    public int CircuitBreakerSamplingDurationSeconds { get; init; } = 30;

    /// <summary>
    /// Failure percentage that opens the circuit breaker.
    ///
    /// 0.5 means 50 percent of calls failed.
    /// </summary>
    public double CircuitBreakerFailureRatio { get; init; } = 0.5;

    /// <summary>
    /// Number of seconds the circuit breaker stays open before allowing
    /// another trial call.
    /// </summary>
    public int CircuitBreakerBreakDurationSeconds { get; init; } = 15;
}
