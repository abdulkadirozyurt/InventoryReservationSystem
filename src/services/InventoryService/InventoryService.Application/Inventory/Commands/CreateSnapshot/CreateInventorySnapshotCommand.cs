namespace InventoryService.Application.Inventory.Commands.CreateSnapshot;

public sealed record CreateInventorySnapshotCommand(
    string RequestedBy,
    string CorrelationId);
