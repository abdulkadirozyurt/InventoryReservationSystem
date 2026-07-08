using Grpc.Core;
using InventoryReservationSystem.Contracts.Inventory;
using InventoryService.Application.Inventory.Commands.DecreaseStock;
using InventoryService.Application.Inventory.Commands.IncreaseStock;
using InventoryService.Application.Inventory.Queries;
using InventoryService.Application.Inventory.Results;
using InventoryService.Application.Reservations.Commands.ConfirmReservation;
using InventoryService.Application.Reservations.Commands.ReleaseBatch;
using InventoryService.Application.Reservations.Commands.ReserveBatch;

namespace InventoryService.API.Grpc;

public sealed class InventoryGrpcService(
    GetStockQueryHandler getStockQueryHandler,
    ReserveBatchCommandHandler reserveBatchCommandHandler,
    ReleaseBatchCommandHandler releaseBatchCommandHandler,
    ConfirmReservationCommandHandler confirmReservationCommandHandler,
    IncreaseStockCommandHandler increaseStockCommandHandler,
    DecreaseStockCommandHandler decreaseStockCommandHandler) : InventoryReservations.InventoryReservationsBase
{
    public override async Task<ReserveBatchResponse> ReserveBatch(ReserveBatchRequest request, ServerCallContext context)
    {
        // map grpc request to c# command
        var command = new ReserveBatchCommand(
            request.OrderId,
            request.Items.Select(item => new ReserveBatchItemCommand(item.Sku, item.WarehouseId, item.Quantity)).ToArray(),
            request.Metadata?.CorrelationId ?? string.Empty);

        // get the result from the command handler
        var result = await reserveBatchCommandHandler.HandleAsync(command, context.CancellationToken);

        // map the result to grpc response
        var response = new ReserveBatchResponse
        {
            Success = result.Success,
            ReservationId = result.ReservationId ?? string.Empty,
            Metadata = CreateMetadata(request.Metadata)
        };

        // add c# result failures to grpc response failures
        response.Failures.AddRange(result.Failures.Select(failure => new ReservationFailure
        {
            Sku = failure.Sku,
            WarehouseId = failure.WarehouseId,
            ErrorCode = failure.ErrorCode,
            Reason = failure.Reason
        }));

        return response;
    }

    public override async Task<ConfirmReservationResponse> ConfirmReservation(ConfirmReservationRequest request, ServerCallContext context)
    {        
        var command = new ConfirmReservationCommand(
            request.ReservationId,
            request.Metadata?.CorrelationId ?? string.Empty);

        // Asıl iş handler içinde: idempotency, lock, transaction ve audit.
        var result = await confirmReservationCommandHandler.HandleAsync(command, context.CancellationToken);

        // Handler sonucunu proto response modeline çeviriyoruz.
        var response = new ConfirmReservationResponse
        {
            Metadata = CreateMetadata(request.Metadata),
            Success = result.Success,
            ErrorCode = result.ErrorCode ?? string.Empty,
            ErrorMessage = result.ErrorMessage ?? string.Empty
        };

        return response;
    }

    public override async Task<ReleaseBatchResponse> ReleaseBatch(ReleaseBatchRequest request, ServerCallContext context)
    {
        // gRPC contract tipi API sınırında kalır; Application katmanına command modeli gider.
        var command = new ReleaseBatchCommand(
            request.ReservationId,
            request.Items.Select(item => new ReleaseBatchItemCommand(item.Sku, item.WarehouseId, item.Quantity)).ToArray(),
            request.Metadata?.CorrelationId ?? string.Empty);

        // İş kuralı handler içindedir: idempotency, lock, transaction, audit ve status update.
        var result = await releaseBatchCommandHandler.HandleAsync(command, context.CancellationToken);

        // Application result proto response'a çevrilir; correlation metadata response'ta korunur.
        var response = new ReleaseBatchResponse
        {
            Success = result.Success,
            ErrorCode = result.ErrorCode ?? string.Empty,
            ErrorMessage = result.ErrorMessage ?? string.Empty,
            Metadata = CreateMetadata(request.Metadata)
        };

        return response;
    }

    public override async Task<GetStockResponse> GetStock(GetStockRequest request, ServerCallContext context)
    {
        var correlationId = request.Metadata?.CorrelationId ?? string.Empty;
        var warehouseId = string.IsNullOrWhiteSpace(request.WarehouseId)
                ? null
                : request.WarehouseId;

        var query = new GetStockQuery(request.Sku, warehouseId, correlationId);

        var stockResult = await getStockQueryHandler.HandleAsync(query, context.CancellationToken);

        return new GetStockResponse
        {
            Metadata = CreateMetadata(request.Metadata),
            Sku = stockResult.Sku,
            WarehouseId = stockResult.WarehouseId ?? string.Empty,
            QuantityAvailable = stockResult.QuantityAvailable,
            QuantityReserved = stockResult.QuantityReserved,
            Found = stockResult.Found,
            ErrorCode = stockResult.ErrorCode ?? string.Empty,
            ErrorMessage = stockResult.ErrorMessage ?? string.Empty
        };
    }

    public override async Task<StockAdjustmentResponse> IncreaseStock(IncreaseStockRequest request, ServerCallContext context)
    {
        // gRPC request direkt application command'a çevriliyor, iş kuralı burada kalmasın diye.
        var command = new IncreaseStockCommand(
            request.Sku,
            request.WarehouseId,
            request.Quantity,
            request.Reason,
            request.Metadata?.CorrelationId ?? string.Empty);

        // Handler lock, transaction ve audit işlerini hallediyor; servis sadece aracı gibi davranıyor.
        var result = await increaseStockCommandHandler.HandleAsync(command, context.CancellationToken);

        // Application sonucu proto response'a çevriliyor, client gRPC formatında cevap bekliyor.
        return ToStockAdjustmentResponse(result, request.Metadata);
    }

    public override async Task<StockAdjustmentResponse> DecreaseStock(DecreaseStockRequest request, ServerCallContext context)
    {
        // Azaltma isteğini ayrı command'a çeviriyoruz, artırma ile karışmasın.
        var command = new DecreaseStockCommand(
            request.Sku,
            request.WarehouseId,
            request.Quantity,
            request.Reason,
            request.Metadata?.CorrelationId ?? string.Empty);

        // Asıl stok azaltma akışı handler tarafında çalışıyor.
        var result = await decreaseStockCommandHandler.HandleAsync(command, context.CancellationToken);

        // Hata veya başarı fark etmez, ortak response mapper ile dönüyoruz.
        return ToStockAdjustmentResponse(result, request.Metadata);
    }

    public override Task<RebalanceWarehouseResponse> RebalanceWarehouse(RebalanceWarehouseRequest request, ServerCallContext context)
    {
        var response = new RebalanceWarehouseResponse
        {
            Metadata = CreateMetadata(request.Metadata),
            Success = true
        };

        return Task.FromResult(response);
    }

    public override Task<CreateInventorySnapshotResponse> CreateInventorySnapshot(CreateInventorySnapshotRequest request, ServerCallContext context)
    {
        var response = new CreateInventorySnapshotResponse
        {
            Metadata = CreateMetadata(request.Metadata),
            Success = true,
            SnapshotId = Guid.CreateVersion7().ToString("N")
        };

        return Task.FromResult(response);
    }

    public override Task<RestoreInventorySnapshotResponse> RestoreInventorySnapshot(RestoreInventorySnapshotRequest request, ServerCallContext context)
    {
        var response = new RestoreInventorySnapshotResponse
        {
            Metadata = CreateMetadata(request.Metadata),
            Success = true
        };

        return Task.FromResult(response);
    }

    private static StockAdjustmentResponse ToStockAdjustmentResponse(StockAdjustmentResult result, RequestMetadata? metadata)
    {
        // Metadata'yı da geri koyuyoruz, çağrıyı log ve trace tarafında takip etmek kolay olsun.
        return new StockAdjustmentResponse
        {
            Metadata = CreateMetadata(metadata),
            Success = result.Success,
            Sku = result.Sku,
            WarehouseId = result.WarehouseId,
            QuantityAvailable = result.QuantityAvailable,
            QuantityReserved = result.QuantityReserved,
            ErrorCode = result.ErrorCode ?? string.Empty,
            ErrorMessage = result.ErrorMessage ?? string.Empty
        };
    }

    private static ResponseMetadata CreateMetadata(RequestMetadata? metadata)
    {
        return new ResponseMetadata
        {
            CorrelationId = metadata?.CorrelationId ?? string.Empty,
            TraceId = metadata?.TraceId ?? string.Empty
        };
    }
}
