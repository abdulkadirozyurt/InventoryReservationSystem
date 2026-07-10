using OrderService.Application.Inventory;

namespace OrderService.API.Endpoints;

/// <summary>
/// Browser-facing inventory facade. All operations forward to InventoryService over gRPC.
/// OrderService never queries InventoryService MongoDB or Redis directly.
/// </summary>
public static class InventoryEndpoints
{
    private const int MaxQuantity = 1_000_000;

    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/inventory").WithTags("Inventory");

        group.MapGet("/items", ListInventoryItemsAsync)
            .WithName("InventoryListItems");

        group.MapGet("/stock", GetStockAsync)
            .WithName("InventoryGetStock");

        group.MapPost("/stock/increase", IncreaseStockAsync)
            .WithName("InventoryIncreaseStock");

        group.MapPost("/stock/decrease", DecreaseStockAsync)
            .WithName("InventoryDecreaseStock");

        group.MapPost("/transfers", TransferStockAsync)
            .WithName("InventoryTransferStock");

        group.MapPost("/snapshots", CreateSnapshotAsync)
            .WithName("InventoryCreateSnapshot");

        group.MapPost("/snapshots/{snapshotId}/restore", RestoreSnapshotAsync)
            .WithName("InventoryRestoreSnapshot");

        return app;
    }

    private static async Task<IResult> ListInventoryItemsAsync(
        string? search,
        string? sku,
        string? warehouseId,
        IInventoryFacade facade,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await facade.ListInventoryItemsAsync(
            new InventoryCatalogueQuery(
                string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                string.IsNullOrWhiteSpace(sku) ? null : sku.Trim(),
                string.IsNullOrWhiteSpace(warehouseId) ? null : warehouseId.Trim()),
            GetCorrelationId(context),
            cancellationToken);

        return Results.Ok(result.Select(item => new InventoryCatalogueItemDto(
            item.Sku,
            item.WarehouseId,
            item.QuantityAvailable,
            item.QuantityReserved)).ToArray());
    }

    private static async Task<IResult> GetStockAsync(
        string? sku,
        string? warehouseId,
        IInventoryFacade facade,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return BadRequest("SkuRequired", "SKU is required.");
        }

        var result = await facade.GetStockAsync(
            new GetStockRequestDto(sku.Trim(), string.IsNullOrWhiteSpace(warehouseId) ? null : warehouseId.Trim()),
            GetCorrelationId(context),
            cancellationToken);

        return Results.Ok(new GetStockResponseDto(
            result.Sku,
            result.WarehouseId,
            result.QuantityAvailable,
            result.QuantityReserved,
            result.Found,
            result.ErrorCode,
            result.ErrorMessage));
    }

    private static async Task<IResult> IncreaseStockAsync(
        StockAdjustmentRequestDto? request,
        IInventoryFacade facade,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var validation = ValidateAdjustment(request);
        if (validation is not null)
        {
            return validation;
        }

        var result = await facade.IncreaseStockAsync(
            new StockAdjustmentRequest(
                request!.Sku.Trim(),
                request.WarehouseId.Trim(),
                request.Quantity,
                request.Reason.Trim()),
            GetCorrelationId(context),
            cancellationToken);

        return Results.Ok(MapStockAdjustment(result));
    }

    private static async Task<IResult> DecreaseStockAsync(
        StockAdjustmentRequestDto? request,
        IInventoryFacade facade,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var validation = ValidateAdjustment(request);
        if (validation is not null)
        {
            return validation;
        }

        var result = await facade.DecreaseStockAsync(
            new StockAdjustmentRequest(
                request!.Sku.Trim(),
                request.WarehouseId.Trim(),
                request.Quantity,
                request.Reason.Trim()),
            GetCorrelationId(context),
            cancellationToken);

        return Results.Ok(MapStockAdjustment(result));
    }

    private static async Task<IResult> TransferStockAsync(
        TransferStockRequestDto? request,
        IInventoryFacade facade,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest("RequestRequired", "Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            return BadRequest("SkuRequired", "SKU is required.");
        }

        if (string.IsNullOrWhiteSpace(request.SourceWarehouseId))
        {
            return BadRequest("SourceWarehouseRequired", "Source warehouse is required.");
        }

        if (string.IsNullOrWhiteSpace(request.TargetWarehouseId))
        {
            return BadRequest("TargetWarehouseRequired", "Target warehouse is required.");
        }

        if (string.Equals(request.SourceWarehouseId.Trim(), request.TargetWarehouseId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("WarehousesMustDiffer", "Source and target warehouses must be different.");
        }

        if (request.Quantity <= 0)
        {
            return BadRequest("QuantityInvalid", "Quantity must be greater than zero.");
        }

        if (request.Quantity > MaxQuantity)
        {
            return BadRequest("QuantityTooLarge", $"Quantity exceeds the maximum allowed value of {MaxQuantity}.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest("ReasonRequired", "Reason is required.");
        }

        var result = await facade.TransferStockAsync(
            new TransferStockRequest(
                request.Sku.Trim(),
                request.SourceWarehouseId.Trim(),
                request.TargetWarehouseId.Trim(),
                request.Quantity,
                request.Reason.Trim()),
            GetCorrelationId(context),
            cancellationToken);

        return Results.Ok(new InventoryOperationResponseDto(result.Success, result.ErrorCode, result.ErrorMessage));
    }

    private static async Task<IResult> CreateSnapshotAsync(
        CreateSnapshotRequestDto? request,
        IInventoryFacade facade,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest("RequestRequired", "Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(request.RequestedBy))
        {
            return BadRequest("RequestedByRequired", "Requested by is required.");
        }

        var result = await facade.CreateSnapshotAsync(
            new CreateSnapshotRequest(request.RequestedBy.Trim()),
            GetCorrelationId(context),
            cancellationToken);

        return Results.Ok(new CreateSnapshotResponseDto(
            result.Success,
            result.SnapshotId,
            result.ErrorCode,
            result.ErrorMessage));
    }

    private static async Task<IResult> RestoreSnapshotAsync(
        string snapshotId,
        RestoreSnapshotRequestDto? request,
        IInventoryFacade facade,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(snapshotId))
        {
            return BadRequest("SnapshotIdRequired", "Snapshot ID is required.");
        }

        if (request is null)
        {
            return BadRequest("RequestRequired", "Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(request.RequestedBy))
        {
            return BadRequest("RequestedByRequired", "Requested by is required.");
        }

        var result = await facade.RestoreSnapshotAsync(
            new RestoreSnapshotRequest(snapshotId.Trim(), request.RequestedBy.Trim()),
            GetCorrelationId(context),
            cancellationToken);

        return Results.Ok(new InventoryOperationResponseDto(result.Success, result.ErrorCode, result.ErrorMessage));
    }

    private static IResult? ValidateAdjustment(StockAdjustmentRequestDto? request)
    {
        if (request is null)
        {
            return BadRequest("RequestRequired", "Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            return BadRequest("SkuRequired", "SKU is required.");
        }

        if (string.IsNullOrWhiteSpace(request.WarehouseId))
        {
            return BadRequest("WarehouseRequired", "Warehouse is required.");
        }

        if (request.Quantity <= 0)
        {
            return BadRequest("QuantityInvalid", "Quantity must be greater than zero.");
        }

        if (request.Quantity > MaxQuantity)
        {
            return BadRequest("QuantityTooLarge", $"Quantity exceeds the maximum allowed value of {MaxQuantity}.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest("ReasonRequired", "Reason is required.");
        }

        return null;
    }

    private static StockAdjustmentResponseDto MapStockAdjustment(StockAdjustmentResult result)
    {
        return new StockAdjustmentResponseDto(
            result.Success,
            result.Sku,
            result.WarehouseId,
            result.QuantityAvailable,
            result.QuantityReserved,
            result.ErrorCode,
            result.ErrorMessage);
    }

    private static IResult BadRequest(string code, string message)
    {
        return Results.BadRequest(new ErrorResponse(code, message));
    }

    private static string GetCorrelationId(HttpContext context)
    {
        return context.Items[Extensions.CorrelationIdItemName]?.ToString()
            ?? Guid.CreateVersion7().ToString("N");
    }
}

public sealed record InventoryCatalogueItemDto(
    string Sku,
    string WarehouseId,
    int QuantityAvailable,
    int QuantityReserved);

public sealed record StockAdjustmentRequestDto(string Sku, string WarehouseId, int Quantity, string Reason);

public sealed record TransferStockRequestDto(
    string Sku,
    string SourceWarehouseId,
    string TargetWarehouseId,
    int Quantity,
    string Reason);

public sealed record CreateSnapshotRequestDto(string RequestedBy);

public sealed record RestoreSnapshotRequestDto(string RequestedBy);

public sealed record GetStockResponseDto(
    string Sku,
    string? WarehouseId,
    int QuantityAvailable,
    int QuantityReserved,
    bool Found,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record StockAdjustmentResponseDto(
    bool Success,
    string Sku,
    string WarehouseId,
    int QuantityAvailable,
    int QuantityReserved,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record InventoryOperationResponseDto(bool Success, string? ErrorCode, string? ErrorMessage);

public sealed record CreateSnapshotResponseDto(bool Success, string? SnapshotId, string? ErrorCode, string? ErrorMessage);

public sealed record ErrorResponse(string ErrorCode, string Message);
