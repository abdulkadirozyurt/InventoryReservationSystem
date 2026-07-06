using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Application.Reservations.Results;
using Microsoft.Extensions.Logging;

namespace InventoryService.Application.Reservations.Commands;

public sealed class ReserveBatchCommandHandler(
    IInventoryUnitOfWork unitOfWork,
    IDistributedLockService distributedLockService,
    ILogger<ReserveBatchCommandHandler> logger)
{
    private const string ValidationFailure = "VALIDATION_ERROR";

    public async Task<ReserveBatchResult> HandleAsync(ReserveBatchCommand request, CancellationToken cancellationToken)
    {
        var failures = Validate(request);

        if (failures.Count > 0)
        {
            logger.LogWarning(
                "Reserve batch validation failed. CorrelationId: {CorrelationId}, FailureCount: {FailureCount}",
                request.CorrelationId,
                failures.Count);

            return new ReserveBatchResult(false, null, failures);
        }

        var lockKeys = CreateLockKeys(request);
        await using var lockHandle = await distributedLockService.AcquireAsync(
            lockKeys,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(5),
            cancellationToken);

        return new ReserveBatchResult(false, null, []);
    }

    private static List<ReserveBatchFailure> Validate(ReserveBatchCommand request)
    {
        var failures = new List<ReserveBatchFailure>();

        if (string.IsNullOrWhiteSpace(request.OrderId))
        {
            failures.Add(new ReserveBatchFailure(string.Empty, string.Empty, ValidationFailure, "Order ID is required."));
        }

        if (string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            failures.Add(new ReserveBatchFailure(string.Empty, string.Empty, ValidationFailure, "Correlation ID is required."));
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            failures.Add(new ReserveBatchFailure(string.Empty, string.Empty, ValidationFailure, "Items are required."));
        }

        if (request.Items is not null)
        {
            foreach (var item in request.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Sku))
                    failures.Add(new ReserveBatchFailure(item.Sku, item.WarehouseId, ValidationFailure, "SKU is required."));

                if (string.IsNullOrWhiteSpace(item.WarehouseId))
                    failures.Add(new ReserveBatchFailure(item.Sku, item.WarehouseId, ValidationFailure, "Warehouse ID is required."));

                if (item.Quantity <= 0)
                    failures.Add(new ReserveBatchFailure(item.Sku, item.WarehouseId, ValidationFailure, "Quantity must be greater than zero."));
            }
        }

        return failures;
    }

    private static IReadOnlyCollection<string> CreateLockKeys(ReserveBatchCommand request)
    {
        return request.Items
            .Select(item => $"inventory:{item.Sku}:{item.WarehouseId}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}