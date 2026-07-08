using Grpc.Core;
using InventoryReservationSystem.Contracts.Inventory;
using InventoryService.Application.Inventory.Queries;
using InventoryService.Application.Reservations.Commands.Reserve;

namespace InventoryService.API.Grpc;

public sealed class InventoryGrpcService(
    GetStockQueryHandler getStockQueryHandler,
    ReserveBatchCommandHandler reserveBatchCommandHandler) : InventoryReservations.InventoryReservationsBase
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
