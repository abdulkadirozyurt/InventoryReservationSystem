namespace InventoryService.Infrastructure.CollectionInitializers.SeedData;

internal sealed record SeedInventoryItem(
    string Sku,
    string WarehouseId,
    int QuantityAvailable,
    int QuantityReserved);

internal sealed record SeedReservation(
    string ReservationId,
    string OrderId,
    string Status,
    TimeSpan ExpiresIn,
    IReadOnlyCollection<SeedReservationItem> Items);

internal sealed record SeedReservationItem(
    string Sku,
    string WarehouseId,
    int Quantity);

internal sealed record SeedInventoryTransaction(
    string Sku,
    string WarehouseId,
    string Type,
    int QuantityAvailableDelta,
    int QuantityReservedDelta,
    string CorrelationId,
    string? ReservationId = null,
    string? OrderId = null,
    string? Reason = null);
