using Grpc.Core;
using InventoryReservationSystem.Contracts.Inventory;
using InventoryService.Application.Inventory.Queries;

namespace InventoryService.API.Grpc;

public sealed class InventoryGrpcService(GetStockQueryHandler getStockQueryHandler) : InventoryReservations.InventoryReservationsBase
{
    public override Task<ReserveBatchResponse> ReserveBatch(ReserveBatchRequest request, ServerCallContext context)
    {
        var response = new ReserveBatchResponse
        {
            Success = true,
            ReservationId = Guid.CreateVersion7().ToString("N"),
            Metadata = CreateMetadata(request.Metadata)
        };

        return Task.FromResult(response);
    }

    public override Task<ConfirmReservationResponse> ConfirmReservation(ConfirmReservationRequest request, ServerCallContext context)
    {
        var response = new ConfirmReservationResponse
        {
            Success = true,
            Metadata = CreateMetadata(request.Metadata)
        };

        return Task.FromResult(response);
    }

    public override Task<ReleaseBatchResponse> ReleaseBatch(ReleaseBatchRequest request, ServerCallContext context)
    {
        var response = new ReleaseBatchResponse
        {
            Success = true,
            Metadata = CreateMetadata(request.Metadata)
        };

        return Task.FromResult(response);
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

    public override Task<StockAdjustmentResponse> IncreaseStock(IncreaseStockRequest request, ServerCallContext context)
    {
        var response = new StockAdjustmentResponse
        {
            Metadata = CreateMetadata(request.Metadata),
            Success = true,
            Sku = request.Sku,
            WarehouseId = request.WarehouseId,
            QuantityAvailable = 0,
            QuantityReserved = 0
        };
        return Task.FromResult(response);
    }

    public override Task<StockAdjustmentResponse> DecreaseStock(DecreaseStockRequest request, ServerCallContext context)
    {
        var response = new StockAdjustmentResponse
        {
            Metadata = CreateMetadata(request.Metadata),
            Success = true,
            Sku = request.Sku,
            WarehouseId = request.WarehouseId,
            QuantityAvailable = 0,
            QuantityReserved = 0
        };

        return Task.FromResult(response);
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

    private static ResponseMetadata CreateMetadata(RequestMetadata? metadata)
    {
        return new ResponseMetadata
        {
            CorrelationId = metadata?.CorrelationId ?? string.Empty,
            TraceId = metadata?.TraceId ?? string.Empty
        };
    }
}
