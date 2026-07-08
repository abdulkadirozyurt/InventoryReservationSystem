namespace OrderService.Application.Orders.Results;

public sealed record OrderItemResult(
    string Sku,
    string WarehouseId,
    int RequestedQuantity,
    int ReservedQuantity);
