using InventoryReservationSystem.Contracts.Inventory;
using Microsoft.Extensions.Options;
using OrderService.Application.Orders.Abstractions;
using OrderService.Infrastructure.InventoryGrpc;

namespace OrderService.Infrastructure.Services;

public sealed class InventoryReservationService(
    InventoryReservations.InventoryReservationsClient client,
    InventoryGrpcResilienceExecutor resilienceExecutor,
    IOptions<InventoryGrpcResilienceOptions> options) : IInventoryReservationService
{
    public async Task<InventoryReservationResult> ReserveBatchAsync(
        string orderId,
        IReadOnlyCollection<InventoryReservationItem> items,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var request = new ReserveBatchRequest
        {
            Metadata = new RequestMetadata { CorrelationId = correlationId },
            OrderId = orderId
        };

        request.Items.AddRange(items.Select(item => new ReservationItem
        {
            Sku = item.Sku,
            WarehouseId = item.WarehouseId,
            Quantity = item.Quantity
        }));

        // Bu çağrı InventoryService'e gider. Geçici gRPC hatalarında Polly retry/circuit breaker devreye girer.
        var response = await ExecuteWithResiliencePipeline(
            token => client.ReserveBatchAsync(request, deadline: CreateGrpcDeadline(), cancellationToken: token).ResponseAsync,
            cancellationToken);

        return new InventoryReservationResult(
            response.Success,
            response.ReservationId,
            response.Failures.Select(failure => new InventoryReservationFailure(
                failure.Sku,
                failure.WarehouseId,
                failure.ErrorCode,
                failure.Reason)).ToArray());
    }

    public async Task<InventoryReservationOperationResult> ReleaseBatchAsync(
        string reservationId,
        IReadOnlyCollection<InventoryReservationItem> items,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var request = new ReleaseBatchRequest
        {
            Metadata = new RequestMetadata { CorrelationId = correlationId },
            ReservationId = reservationId
        };

        request.Items.AddRange(items.Select(item => new ReservationItem
        {
            Sku = item.Sku,
            WarehouseId = item.WarehouseId,
            Quantity = item.Quantity
        }));

        // Release işlemi de geçici servis ve ağ hatalarına karşı aynı ortak pipeline'ı kullanır.
        var response = await ExecuteWithResiliencePipeline(
            token => client.ReleaseBatchAsync(request, deadline: CreateGrpcDeadline(), cancellationToken: token).ResponseAsync,
            cancellationToken);
        return new InventoryReservationOperationResult(response.Success, response.ErrorCode, response.ErrorMessage);
    }

    public async Task<InventoryReservationOperationResult> ConfirmReservationAsync(
        string reservationId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var request = new ConfirmReservationRequest
        {
            Metadata = new RequestMetadata { CorrelationId = correlationId },
            ReservationId = reservationId
        };

        // Confirm çağrısı başarısız olduğunda transient hatalar ortak pipeline tarafından yönetilir.
        var response = await ExecuteWithResiliencePipeline(
            token => client.ConfirmReservationAsync(request, deadline: CreateGrpcDeadline(), cancellationToken: token).ResponseAsync,
            cancellationToken);
        return new InventoryReservationOperationResult(response.Success, response.ErrorCode, response.ErrorMessage);
    }

    private async Task<TResponse> ExecuteWithResiliencePipeline<TResponse>(
        Func<CancellationToken, Task<TResponse>> operation,
        CancellationToken cancellationToken = default)
    {
        return await resilienceExecutor.ExecuteAsync(
            token => operation(token),
            cancellationToken);
    }

    private DateTime CreateGrpcDeadline()
    {
        // Polly timeout token'ı keser; gRPC bazen bunu Cancelled olarak raporlar.
        // Deadline verirsek gRPC kendi timeout'unu bilir ve DeadlineExceeded status'u üretir.
        return DateTime.UtcNow.AddSeconds(options.Value.TimeoutSeconds);
    }
}
