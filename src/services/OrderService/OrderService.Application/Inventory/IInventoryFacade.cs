namespace OrderService.Application.Inventory;

/// <summary>
/// Browser-friendly inventory operations facade.
/// OrderService forwards every operation to InventoryService over gRPC and never reads Inventory-owned storage directly.
/// </summary>
public interface IInventoryFacade
{
    Task<IReadOnlyList<InventoryCatalogueItem>> ListInventoryItemsAsync(
        InventoryCatalogueQuery request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<GetStockResult> GetStockAsync(
        GetStockRequestDto request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<StockAdjustmentResult> IncreaseStockAsync(
        StockAdjustmentRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<StockAdjustmentResult> DecreaseStockAsync(
        StockAdjustmentRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<InventoryOperationResult> TransferStockAsync(
        TransferStockRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<CreateSnapshotResult> CreateSnapshotAsync(
        CreateSnapshotRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<InventoryOperationResult> RestoreSnapshotAsync(
        RestoreSnapshotRequest request,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public sealed record InventoryCatalogueQuery(string? Search, string? Sku, string? WarehouseId);

public sealed record InventoryCatalogueItem(
    string Sku,
    string WarehouseId,
    int QuantityAvailable,
    int QuantityReserved);

public sealed record GetStockRequestDto(string Sku, string? WarehouseId);

public sealed record StockAdjustmentRequest(string Sku, string WarehouseId, int Quantity, string Reason);

public sealed record TransferStockRequest(
    string Sku,
    string SourceWarehouseId,
    string TargetWarehouseId,
    int Quantity,
    string Reason);

public sealed record CreateSnapshotRequest(string RequestedBy);

public sealed record RestoreSnapshotRequest(string SnapshotId, string RequestedBy);

public sealed record InventoryOperationResult(bool Success, string? ErrorCode, string? ErrorMessage);

public sealed record GetStockResult(
    string Sku,
    string? WarehouseId,
    int QuantityAvailable,
    int QuantityReserved,
    bool Found,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record StockAdjustmentResult(
    bool Success,
    string Sku,
    string WarehouseId,
    int QuantityAvailable,
    int QuantityReserved,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record CreateSnapshotResult(
    bool Success,
    string? SnapshotId,
    string? ErrorCode,
    string? ErrorMessage);
