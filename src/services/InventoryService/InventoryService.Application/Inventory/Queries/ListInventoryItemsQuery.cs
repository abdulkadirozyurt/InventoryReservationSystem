namespace InventoryService.Application.Inventory.Queries;

public sealed record ListInventoryItemsQuery(
    string? Search,
    string? Sku,
    string? WarehouseId,
    string CorrelationId);
