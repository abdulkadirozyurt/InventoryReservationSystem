namespace InventoryService.Application.Inventory.Commands.RestoreSnapshot;

public sealed record RestoreInventorySnapshotCommand(
    string SnapshotId,
    string RequestedBy,
    string CorrelationId);
