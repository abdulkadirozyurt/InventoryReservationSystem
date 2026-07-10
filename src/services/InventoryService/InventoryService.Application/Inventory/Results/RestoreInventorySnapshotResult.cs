namespace InventoryService.Application.Inventory.Results;

public sealed record RestoreInventorySnapshotResult(
    bool Success,
    string? ErrorCode,
    string? ErrorMessage);
