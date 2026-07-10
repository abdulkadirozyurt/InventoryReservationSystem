namespace InventoryService.Application.Inventory.Results;

public sealed record CreateInventorySnapshotResult(
    bool Success,
    string? ErrorCode,
    string? ErrorMessage,
    string? SnapshotId);
