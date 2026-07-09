using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Application.Reservations.Results.Reconciliation;
using InventoryService.Domain.DeadLetterQueue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InventoryService.Infrastructure.BackgroundJobs;

public enum ReconciliationMismatchType
{
    ExpectedReservedMissingInInventory,
    InventoryReservedWithoutPendingReservations,
    ReservedQuantityMismatch
}

/// <summary>
/// Background service that periodically audits and reconciles expected reserved quantities against actual counts.
/// It logs any discrepancies detected and flags errors to the Dead Letter Queue.
/// </summary>
public sealed class InventoryReconciliationBackgroundService : BackgroundService
{
    private const string JobName = "InventoryReconciliation";
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InventoryReconciliationBackgroundService> _logger;
    private readonly ReconciliationWorkerOptions _options;

    public InventoryReconciliationBackgroundService(
        IServiceProvider serviceProvider,
        IOptions<ReconciliationWorkerOptions> options,
        ILogger<InventoryReconciliationBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Inventory Reconciliation Background Service is starting.");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.IntervalSeconds));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessReconciliationAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected exception in Inventory Reconciliation Background Service execution.");
            }
        }

        _logger.LogInformation("Inventory Reconciliation Background Service is stopping.");
    }

    public async Task ProcessReconciliationAsync(CancellationToken cancellationToken)
    {
        // Why report-only: Discrepancies are reported / alerted via structured logs (no automated write corrections)
        // to avoid risk of race conditions or automated data corruption in production.
        // Why no OrderService DB/API coupling in MVP: Keep services isolated and respect DDD aggregate boundaries.
        // Why grouping by SKU+warehouse: Match the granularity of actual inventory reservation units (SKU inside a warehouse).
        var correlationId = $"job-recon-{Guid.NewGuid()}";

        using var scope = _serviceProvider.CreateScope();
        var reservationRepository = scope.ServiceProvider.GetRequiredService<IReservationRepository>();
        var inventoryItemRepository = scope.ServiceProvider.GetRequiredService<IInventoryItemRepository>();
        var deadLetterQueueRepository = scope.ServiceProvider.GetRequiredService<IDeadLetterQueueRepository>();

        try
        {
            var expectedTasks = reservationRepository.GetExpectedReservedQuantityBySkuWarehouseAsync(cancellationToken);
            var actualTasks = inventoryItemRepository.GetReservedQuantitySnapshotAsync(cancellationToken);

            await Task.WhenAll(expectedTasks, actualTasks);

            var expected = expectedTasks.Result;
            var actual = actualTasks.Result;

            var expectedMap = expected.ToDictionary(x => (x.Sku, x.WarehouseId));
            var allKeys = new HashSet<(string Sku, string WarehouseId)>(expectedMap.Keys);
            foreach (var key in actual.Keys)
            {
                allKeys.Add(key);
            }

            int checkedCount = 0;
            int mismatchCount = 0;

            foreach (var key in allKeys)
            {
                checkedCount++;
                expectedMap.TryGetValue(key, out var expectedSnapshot);
                actual.TryGetValue(key, out int actualQty);

                int expectedQty = expectedSnapshot?.ExpectedReservedQuantity ?? 0;

                if (expectedQty != actualQty)
                {
                    mismatchCount++;
                    string mismatchType = (expectedQty > 0 && actualQty == 0)
                        ? ReconciliationMismatchType.ExpectedReservedMissingInInventory.ToString()
                        : (expectedQty == 0 && actualQty > 0)
                            ? ReconciliationMismatchType.InventoryReservedWithoutPendingReservations.ToString()
                            : ReconciliationMismatchType.ReservedQuantityMismatch.ToString();

                    var reservationIds = expectedSnapshot?.ReservationIds ?? Array.Empty<string>();
                    var orderIds = expectedSnapshot?.OrderIds ?? Array.Empty<string>();

                    _logger.LogWarning(
                        "Inventory reconciliation mismatch found. JobName: {JobName}, CorrelationId: {CorrelationId}, Sku: {Sku}, WarehouseId: {WarehouseId}, ExpectedReservedQuantity: {ExpectedReservedQuantity}, ActualReservedQuantity: {ActualReservedQuantity}, MismatchType: {MismatchType}, ReservationIds: {ReservationIds}, OrderIds: {OrderIds}",
                        JobName,
                        correlationId,
                        key.Sku,
                        key.WarehouseId,
                        expectedQty,
                        actualQty,
                        mismatchType,
                        reservationIds,
                        orderIds);
                }
            }

            _logger.LogInformation(
                "Inventory reconciliation scan report: {CheckedCount} sku-warehouse pairs checked, {MismatchCount} mismatches detected. JobName: {JobName}, CorrelationId: {CorrelationId}",
                checkedCount,
                mismatchCount,
                JobName,
                correlationId);

            if (mismatchCount == 0)
            {
                _logger.LogInformation(
                    "Inventory reconciliation completed successfully. No mismatches found. JobName: {JobName}, CorrelationId: {CorrelationId}",
                    JobName,
                    correlationId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Exception during InventoryReconciliation execution. JobName: {JobName}, CorrelationId: {CorrelationId}",
                JobName,
                correlationId);

            try
            {
                // DLQ(Dead letter queue) is for job execution failures only. Data drift is already visible through mismatch logs and should not be retried like a poisoned message.
                var record = new DeadLetterRecord(
                    operationType: "Reconciliation",
                    reason: ex.Message,
                    errorCategory: ex.GetType().Name,
                    correlationId: correlationId,
                    reservationId: null,
                    orderId: null,
                    retryCount: 0,
                    payloadSnapshot: null);

                await deadLetterQueueRepository.UpsertFailureAsync(record, cancellationToken);
            }
            catch (Exception dlqEx)
            {
                _logger.LogError(
                    dlqEx,
                    "Failed to save reconciliation failure to DLQ. JobName: {JobName}, CorrelationId: {CorrelationId}",
                    JobName,
                    correlationId);
            }
        }
    }
}
