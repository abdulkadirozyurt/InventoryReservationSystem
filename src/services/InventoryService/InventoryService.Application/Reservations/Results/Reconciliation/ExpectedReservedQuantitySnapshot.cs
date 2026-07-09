namespace InventoryService.Application.Reservations.Results.Reconciliation;

/// <summary>
/// Read-only reconciliation projection. Carries expected reserved total plus source reservation/order ids for audit-friendly mismatch logs.
/// </summary>
public sealed record ExpectedReservedQuantitySnapshot(
    string Sku,
    string WarehouseId,
    int ExpectedReservedQuantity,
    IReadOnlyCollection<string> ReservationIds,
    IReadOnlyCollection<string> OrderIds);
