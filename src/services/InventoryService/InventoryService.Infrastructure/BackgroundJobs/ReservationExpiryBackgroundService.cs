using System.Text.Json;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Application.Reservations.Commands.ReleaseBatch;
using InventoryService.Domain.Checkpoints;
using InventoryService.Domain.DeadLetterQueue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InventoryService.Infrastructure.BackgroundJobs;

// InventoryService'un kendi kendini temizleme motoru. OrderService veritabanina dokunmaz, sadece kendi Reservation koleksiyonunu tarar.
// Checkpoint ile crash-restart senaryolarinda kaldigi yerden devam eder, duplicate release uretmez.
public sealed class ReservationExpiryBackgroundService : BackgroundService
{
    private const string JobName = "ReservationExpiry";
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ReservationExpiryBackgroundService> _logger;
    private readonly ExpiryWorkerOptions _options;

    public ReservationExpiryBackgroundService(
        IServiceProvider serviceProvider,
        IOptions<ExpiryWorkerOptions> options,
        ILogger<ReservationExpiryBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Reservation Expiry Background Service is starting.");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.IntervalSeconds));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessExpiredReservationsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing reservation expiry.");
            }
        }

        _logger.LogInformation("Reservation Expiry Background Service is stopping.");
    }

    public async Task ProcessExpiredReservationsAsync(CancellationToken cancellationToken)
    {
        var correlationId = $"job-expiry-{Guid.NewGuid():N}";
        _logger.LogInformation(
            "Starting reservation expiry scan. JobName: {JobName}, CorrelationId: {CorrelationId}",
            JobName,
            correlationId);

        using var scope = _serviceProvider.CreateScope();
        var reservationRepository = scope.ServiceProvider.GetRequiredService<IReservationRepository>();
        var checkpointRepository = scope.ServiceProvider.GetRequiredService<ICheckpointRepository>();
        var releaseBatchCommandHandler = scope.ServiceProvider.GetRequiredService<ReleaseBatchCommandHandler>();
        var dlqRepository = scope.ServiceProvider.GetRequiredService<IDeadLetterQueueRepository>();

        // 1. Get current checkpoint
        var checkpoint = await checkpointRepository.GetByNameAsync(JobName, cancellationToken);
        DateTime? cursorTimestamp = checkpoint?.CursorTimestamp;
        string? lastReservationId = checkpoint?.LastReservationId;

        // 2. Fetch pending expired reservations
        var now = DateTime.UtcNow;
        var expiredReservations = await reservationRepository.GetExpiredPendingReservationsAsync(
            now,
            cursorTimestamp,
            lastReservationId,
            _options.BatchSize,
            cancellationToken);

        if (expiredReservations.Count == 0)
        {
            _logger.LogInformation(
                "No expired pending reservations found. JobName: {JobName}, CorrelationId: {CorrelationId}",
                JobName,
                correlationId);
            return;
        }

        _logger.LogInformation(
            "Found {Count} expired pending reservations to process. JobName: {JobName}, CorrelationId: {CorrelationId}",
            expiredReservations.Count,
            JobName,
            correlationId);

        foreach (var reservation in expiredReservations)
        {
            var reservationId = reservation.ReservationId;
            var orderId = reservation.OrderId;

            _logger.LogInformation(
                "Processing reservation expiry. JobName: {JobName}, CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, OrderId: {OrderId}",
                JobName,
                correlationId,
                reservationId,
                orderId);

            // Expiry ReleaseBatch reuse: ayni ReleaseBatchCommandHandler kullanilir, IsExpiry=true ile reservation Expired state'e gecer, audit "Expired" yazilir.
            var command = new ReleaseBatchCommand(
                reservationId,
                reservation.Items.Select(item => new ReleaseBatchItemCommand(item.Sku, item.WarehouseId, item.Quantity)).ToArray(),
                correlationId,
                IsExpiry: true);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            bool releaseSuccess = false;
            string? errorMessage = null;
            string? errorCode = null;

            try
            {
                var result = await releaseBatchCommandHandler.HandleAsync(command, cancellationToken);
                releaseSuccess = result.Success;
                errorMessage = result.ErrorMessage;
                errorCode = result.ErrorCode;

                if (!releaseSuccess)
                {
                    _logger.LogWarning(
                        "ReleaseBatchCommandHandler returned failure. JobName: {JobName}, CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}, ErrorMessage: {ErrorMessage}",
                        JobName,
                        correlationId,
                        reservationId,
                        errorCode,
                        errorMessage);
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                errorCode = ex.GetType().Name;
                _logger.LogError(
                    ex,
                    "Exception during ReleaseBatchCommandHandler execution. JobName: {JobName}, CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}",
                    JobName,
                    correlationId,
                    reservationId,
                    errorCode);
            }
            finally
            {
                stopwatch.Stop();
            }

            if (releaseSuccess)
            {
                _logger.LogInformation(
                    "Successfully expired and released reservation. JobName: {JobName}, CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, OrderId: {OrderId}, ElapsedMs: {ElapsedMs}",
                    JobName,
                    correlationId,
                    reservationId,
                    orderId,
                    stopwatch.ElapsedMilliseconds);
            }
            else
            {
                var payloadJson = JsonSerializer.Serialize(command);
                var dlqRecord = new DeadLetterRecord(
                    operationType: "ExpiryRelease",
                    reason: errorMessage ?? "Unknown release failure",
                    errorCategory: errorCode ?? "UnknownError",
                    correlationId: correlationId,
                    reservationId: reservationId,
                    orderId: orderId,
                    retryCount: 0,
                    payloadSnapshot: payloadJson);

                int retryCount;
                try
                {
                    retryCount = await dlqRepository.UpsertFailureAsync(dlqRecord, cancellationToken);
                }
                catch (Exception dlqEx)
                {
                    _logger.LogError(
                        dlqEx,
                        "Failed to upsert DLQ record. JobName: {JobName}, CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, OrderId: {OrderId}",
                        JobName,
                        correlationId,
                        reservationId,
                        orderId);
                    // Do not advance checkpoint if both release and DLQ upsert failed to ensure we retry.
                    throw;
                }

                var maxRetryCount = Math.Max(1, _options.MaxRetryCount);

                // retryCount < maxRetryCount => bir sonraki scan cycle'inda tekrar dene, checkpoint ilerletme (return).
                // retryCount >= maxRetryCount => quarantine, checkpoint ilerlet, bir daha isleme.
                if (retryCount < maxRetryCount)
                {
                    _logger.LogWarning(
                        "Release failure tracked for retry. JobName: {JobName}, CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, OrderId: {OrderId}, RetryCount: {RetryCount}, MaxRetryCount: {MaxRetryCount}, ErrorCategory: {ErrorCategory}",
                        JobName,
                        correlationId,
                        reservationId,
                        orderId,
                        retryCount,
                        maxRetryCount,
                        errorCode ?? "UnknownError");
                    return;
                }

                _logger.LogWarning(
                    "Quarantined reservation to DLQ after retries. JobName: {JobName}, CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, OrderId: {OrderId}, RetryCount: {RetryCount}, MaxRetryCount: {MaxRetryCount}, ErrorCategory: {ErrorCategory}",
                    JobName,
                    correlationId,
                    reservationId,
                    orderId,
                    retryCount,
                    maxRetryCount,
                    errorCode ?? "UnknownError");
            }

            // Update/save checkpoint after success or final DLQ quarantine only.
            if (checkpoint == null)
            {
                checkpoint = new Checkpoint(JobName, reservation.ExpiresAt, reservationId);
            }
            else
            {
                checkpoint.Update(reservation.ExpiresAt, reservationId);
            }

            try
            {
                await checkpointRepository.SaveAsync(checkpoint, cancellationToken);
            }
            catch (Exception checkpointEx)
            {
                _logger.LogError(
                    checkpointEx,
                    "Failed to save checkpoint. JobName: {JobName}, CorrelationId: {CorrelationId}, ReservationId: {ReservationId}",
                    JobName,
                    correlationId,
                    reservationId);
                // Propagate exception to stop the batch execution from advancing/losing cursor state
                throw;
            }
        }

        _logger.LogInformation(
            "Completed reservation expiry scan. JobName: {JobName}, CorrelationId: {CorrelationId}, NextCheckpointCursor: {Cursor}",
            JobName,
            correlationId,
            checkpoint?.CursorTimestamp);
    }
}
