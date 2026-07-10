using Grpc.Core;
using InventoryReservationSystem.Contracts.Inventory;
using Microsoft.Extensions.Options;
using OrderService.Application.Inventory;
using OrderService.Infrastructure.InventoryGrpc;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace OrderService.Infrastructure.Inventory;

/// <summary>
/// Forwards browser-facing inventory operations to InventoryService over gRPC.
/// OrderService does not access InventoryService MongoDB or Redis directly.
/// </summary>
public sealed class InventoryFacade(
    InventoryReservations.InventoryReservationsClient client,
    InventoryGrpcResilienceExecutor resilienceExecutor,
    IOptions<InventoryGrpcResilienceOptions> options) : IInventoryFacade
{
    public async Task<IReadOnlyList<InventoryCatalogueItem>> ListInventoryItemsAsync(
        InventoryCatalogueQuery request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var grpcRequest = new ListInventoryItemsRequest
        {
            Metadata = CreateMetadata(correlationId),
            Search = request.Search ?? string.Empty,
            Sku = request.Sku ?? string.Empty,
            WarehouseId = request.WarehouseId ?? string.Empty
        };

        var response = await ExecuteWithResiliencePipeline(
            token => client.ListInventoryItemsAsync(grpcRequest, deadline: CreateGrpcDeadline(), cancellationToken: token).ResponseAsync,
            cancellationToken);

        return response.Items
            .Select(item => new InventoryCatalogueItem(
                item.Sku,
                item.WarehouseId,
                item.QuantityAvailable,
                item.QuantityReserved))
            .ToArray();
    }

    public async Task<GetStockResult> GetStockAsync(
        GetStockRequestDto request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var grpcRequest = new GetStockRequest
        {
            Metadata = CreateMetadata(correlationId),
            Sku = request.Sku,
            WarehouseId = request.WarehouseId ?? string.Empty
        };

        return await ExecuteGrpcOperationAsync(
            token => client.GetStockAsync(grpcRequest, deadline: CreateGrpcDeadline(), cancellationToken: token).ResponseAsync,
            response => new GetStockResult(
                response.Sku,
                string.IsNullOrWhiteSpace(response.WarehouseId) ? null : response.WarehouseId,
                response.QuantityAvailable,
                response.QuantityReserved,
                response.Found,
                response.ErrorCode,
                response.ErrorMessage),
            unavailableFailure: new GetStockResult(
                request.Sku,
                request.WarehouseId,
                0,
                0,
                false,
                "InventoryServiceUnavailable",
                "InventoryService is unavailable. Please retry shortly."),
            timeoutFailure: new GetStockResult(
                request.Sku,
                request.WarehouseId,
                0,
                0,
                false,
                "InventoryServiceTimeout",
                "InventoryService request timed out."),
            grpcFailure: new GetStockResult(
                request.Sku,
                request.WarehouseId,
                0,
                0,
                false,
                "InventoryServiceGrpcError",
                "InventoryService gRPC call failed."),
            cancellationToken);
    }

    public async Task<StockAdjustmentResult> IncreaseStockAsync(
        StockAdjustmentRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var grpcRequest = new IncreaseStockRequest
        {
            Metadata = CreateMetadata(correlationId),
            Sku = request.Sku,
            WarehouseId = request.WarehouseId,
            Quantity = request.Quantity,
            Reason = request.Reason
        };

        return await ExecuteGrpcOperationAsync(
            token => client.IncreaseStockAsync(grpcRequest, deadline: CreateGrpcDeadline(), cancellationToken: token).ResponseAsync,
            MapStockAdjustment,
            unavailableFailure: MakeStockAdjustmentFailure(request, "InventoryServiceUnavailable", "InventoryService is unavailable. Please retry shortly."),
            timeoutFailure: MakeStockAdjustmentFailure(request, "InventoryServiceTimeout", "InventoryService request timed out."),
            grpcFailure: MakeStockAdjustmentFailure(request, "InventoryServiceGrpcError", "InventoryService gRPC call failed."),
            cancellationToken);
    }

    public async Task<StockAdjustmentResult> DecreaseStockAsync(
        StockAdjustmentRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var grpcRequest = new DecreaseStockRequest
        {
            Metadata = CreateMetadata(correlationId),
            Sku = request.Sku,
            WarehouseId = request.WarehouseId,
            Quantity = request.Quantity,
            Reason = request.Reason
        };

        return await ExecuteGrpcOperationAsync(
            token => client.DecreaseStockAsync(grpcRequest, deadline: CreateGrpcDeadline(), cancellationToken: token).ResponseAsync,
            MapStockAdjustment,
            unavailableFailure: MakeStockAdjustmentFailure(request, "InventoryServiceUnavailable", "InventoryService is unavailable. Please retry shortly."),
            timeoutFailure: MakeStockAdjustmentFailure(request, "InventoryServiceTimeout", "InventoryService request timed out."),
            grpcFailure: MakeStockAdjustmentFailure(request, "InventoryServiceGrpcError", "InventoryService gRPC call failed."),
            cancellationToken);
    }

    public async Task<InventoryOperationResult> TransferStockAsync(
        TransferStockRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var grpcRequest = new RebalanceWarehouseRequest
        {
            Metadata = CreateMetadata(correlationId),
            Sku = request.Sku,
            SourceWarehouseId = request.SourceWarehouseId,
            TargetWarehouseId = request.TargetWarehouseId,
            Quantity = request.Quantity,
            Reason = request.Reason
        };

        return await ExecuteGrpcOperationAsync(
            token => client.RebalanceWarehouseAsync(grpcRequest, deadline: CreateGrpcDeadline(), cancellationToken: token).ResponseAsync,
            response => new InventoryOperationResult(response.Success, response.ErrorCode, response.ErrorMessage),
            unavailableFailure: new InventoryOperationResult(false, "InventoryServiceUnavailable", "InventoryService is unavailable. Please retry shortly."),
            timeoutFailure: new InventoryOperationResult(false, "InventoryServiceTimeout", "InventoryService request timed out."),
            grpcFailure: new InventoryOperationResult(false, "InventoryServiceGrpcError", "InventoryService gRPC call failed."),
            cancellationToken);
    }

    public async Task<CreateSnapshotResult> CreateSnapshotAsync(
        CreateSnapshotRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var grpcRequest = new CreateInventorySnapshotRequest
        {
            Metadata = CreateMetadata(correlationId),
            RequestedBy = request.RequestedBy
        };

        return await ExecuteGrpcOperationAsync(
            token => client.CreateInventorySnapshotAsync(grpcRequest, deadline: CreateGrpcDeadline(), cancellationToken: token).ResponseAsync,
            response => new CreateSnapshotResult(response.Success, response.SnapshotId, response.ErrorCode, response.ErrorMessage),
            unavailableFailure: new CreateSnapshotResult(false, null, "InventoryServiceUnavailable", "InventoryService is unavailable. Please retry shortly."),
            timeoutFailure: new CreateSnapshotResult(false, null, "InventoryServiceTimeout", "InventoryService request timed out."),
            grpcFailure: new CreateSnapshotResult(false, null, "InventoryServiceGrpcError", "InventoryService gRPC call failed."),
            cancellationToken);
    }

    public async Task<InventoryOperationResult> RestoreSnapshotAsync(
        RestoreSnapshotRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var grpcRequest = new RestoreInventorySnapshotRequest
        {
            Metadata = CreateMetadata(correlationId),
            SnapshotId = request.SnapshotId,
            RequestedBy = request.RequestedBy
        };

        return await ExecuteGrpcOperationAsync(
            token => client.RestoreInventorySnapshotAsync(grpcRequest, deadline: CreateGrpcDeadline(), cancellationToken: token).ResponseAsync,
            response => new InventoryOperationResult(response.Success, response.ErrorCode, response.ErrorMessage),
            unavailableFailure: new InventoryOperationResult(false, "InventoryServiceUnavailable", "InventoryService is unavailable. Please retry shortly."),
            timeoutFailure: new InventoryOperationResult(false, "InventoryServiceTimeout", "InventoryService request timed out."),
            grpcFailure: new InventoryOperationResult(false, "InventoryServiceGrpcError", "InventoryService gRPC call failed."),
            cancellationToken);
    }

    private static StockAdjustmentResult MapStockAdjustment(StockAdjustmentResponse response)
    {
        return new StockAdjustmentResult(
            response.Success,
            response.Sku,
            response.WarehouseId,
            response.QuantityAvailable,
            response.QuantityReserved,
            response.ErrorCode,
            response.ErrorMessage);
    }

    private static StockAdjustmentResult MakeStockAdjustmentFailure(
        StockAdjustmentRequest request,
        string errorCode,
        string errorMessage)
    {
        return new StockAdjustmentResult(
            false,
            request.Sku,
            request.WarehouseId,
            0,
            0,
            errorCode,
            errorMessage);
    }

    private async Task<TResult> ExecuteGrpcOperationAsync<TResponse, TResult>(
        Func<CancellationToken, Task<TResponse>> operation,
        Func<TResponse, TResult> successMapper,
        TResult unavailableFailure,
        TResult timeoutFailure,
        TResult grpcFailure,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await ExecuteWithResiliencePipeline(operation, cancellationToken);
            return successMapper(response);
        }
        catch (BrokenCircuitException)
        {
            return unavailableFailure;
        }
        catch (TimeoutRejectedException)
        {
            return timeoutFailure;
        }
        catch (RpcException)
        {
            return grpcFailure;
        }
    }

    private static RequestMetadata CreateMetadata(string correlationId)
    {
        return new RequestMetadata { CorrelationId = correlationId };
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
