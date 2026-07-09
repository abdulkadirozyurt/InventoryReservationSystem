using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace OrderService.Infrastructure.InventoryGrpc;

/// <summary>
/// Executes InventoryService gRPC calls through retry and circuit breaker policies.
/// </summary>
public sealed class InventoryGrpcResilienceExecutor
{
    private readonly ResiliencePipeline pipeline;

    public InventoryGrpcResilienceExecutor(
        IOptions<InventoryGrpcResilienceOptions> options,
        ILogger<InventoryGrpcResilienceExecutor> logger)
    {
        var settings = options.Value;

        // Sadece geçici saydığımız gRPC hatalarını tekrar deniyoruz.
        // Örneğin servis kısa süreliğine kapalıysa istek hemen tamamen başarısız olmuyor.
        var transientGrpcErrors = new PredicateBuilder()
            .Handle<RpcException>(exception => IsTransient(exception.StatusCode));

        var timeoutOptions = new TimeoutStrategyOptions
        {
            Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds),
            OnTimeout = arguments =>
            {
                // Timeout tek gRPC denemesine üst sınır koyar. Böylece yavaş servis HTTP isteğini süresiz tutamaz.
                // Bu hata retry tarafından transient kabul edilir ve ayarlıysa yeni deneme yapılır.
                logger.LogWarning(
                    "InventoryService gRPC call timed out after {Timeout}. Attempt: {AttemptNumber}.",
                    arguments.Timeout,
                    arguments.Context.Properties.TryGetValue(
                        new ResiliencePropertyKey<int>("AttemptNumber"),
                        out var attemptNumber) ? attemptNumber : 0);

                return default;
            }
        };

        var retryErrors = new PredicateBuilder()
            .Handle<RpcException>(exception => IsTransient(exception.StatusCode))
            .Handle<TimeoutRejectedException>();

        var retryOptions = new RetryStrategyOptions
        {
            ShouldHandle = retryErrors,
            MaxRetryAttempts = settings.RetryCount,
            Delay = TimeSpan.FromMilliseconds(settings.RetryBaseDelayMilliseconds),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            OnRetry = arguments =>
            {
                // Hangi tekrar denemesinin yapıldığını logdan görebilmek için yazıyoruz.
                logger.LogWarning(
                    arguments.Outcome.Exception,
                    "InventoryService gRPC call failed. Retry attempt {RetryAttempt} will run after {RetryDelay}.",
                    arguments.AttemptNumber + 1,
                    arguments.RetryDelay);

                return default;
            }
        };

        var circuitBreakerOptions = new CircuitBreakerStrategyOptions
        {
            ShouldHandle = retryErrors,
            FailureRatio = settings.CircuitBreakerFailureRatio,
            MinimumThroughput = settings.CircuitBreakerMinimumThroughput,
            SamplingDuration = TimeSpan.FromSeconds(settings.CircuitBreakerSamplingDurationSeconds),
            BreakDuration = TimeSpan.FromSeconds(settings.CircuitBreakerBreakDurationSeconds),
            OnOpened = arguments =>
            {
                // Hata oranı yükselince devre açılır ve InventoryService'e bir süre istek gönderilmez.
                logger.LogError(
                    arguments.Outcome.Exception,
                    "InventoryService gRPC circuit opened for {BreakDuration}.",
                    arguments.BreakDuration);

                return default;
            },
            OnHalfOpened = _ =>
            {
                // Bekleme süresi bitince servisin düzelip düzelmediğini anlamak için bir deneme yapılır.
                logger.LogInformation(
                    "InventoryService gRPC circuit is half-open. A trial call is allowed.");

                return default;
            },
            OnClosed = _ =>
            {
                // Deneme başarılı olursa devre kapanır ve normal çağrılar tekrar devam eder.
                logger.LogInformation(
                    "InventoryService gRPC circuit closed. Calls are running normally again.");

                return default;
            }
        };

        // Circuit breaker dışarıda durur. Böylece retry işlemlerinin tamamı bittikten sonra
        // sonuç tek bir başarısız çağrı olarak circuit breaker tarafından değerlendirilir.
        pipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(circuitBreakerOptions)
            .AddRetry(retryOptions)
            .AddTimeout(timeoutOptions)
            .Build();
    }

    /// <summary>
    /// Executes a gRPC operation through the configured resilience pipeline.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        return await pipeline.ExecuteAsync(
            async token => await operation(token),
            cancellationToken);
    }

    private static bool IsTransient(StatusCode statusCode)
    {
        // Bu durumlar çoğunlukla kısa süreli servis veya ağ problemlerini anlatır.
        // İş kuralı hatalarını burada saymıyoruz; onları tekrar denemek sonucu değiştirmez.
        // Service discovery/HTTP client bağlantı hatasını iptal ederek gRPC katmanına Cancelled taşıyabilir.
        // Caller'ın kendi CancellationToken'ı zaten pipeline'a ayrı verildiği için buradaki RpcException.Cancelled
        // dependency bağlantı hatası olarak değerlendirilir ve retry/circuit breaker hesabına katılır.
        return statusCode is StatusCode.Unavailable
            or StatusCode.Cancelled
            or StatusCode.DeadlineExceeded
            or StatusCode.ResourceExhausted;
    }
}
