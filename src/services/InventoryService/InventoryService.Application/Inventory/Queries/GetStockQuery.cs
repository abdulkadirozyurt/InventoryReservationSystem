namespace InventoryService.Application.Inventory.Queries;

public sealed record GetStockQuery(
    string Sku,
    string? WarehouseId,
    string CorrelationId);