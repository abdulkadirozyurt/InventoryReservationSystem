using InventoryService.Domain.InventoryTransactions;
using InventoryService.Domain.Reservations;

namespace InventoryService.Infrastructure.CollectionInitializers.SeedData;

internal static class InventorySeedCatalog
{
    public static IReadOnlyCollection<SeedInventoryItem> InventoryItems { get; } =
    [
        new("SKU-001", "WH-1", 100, 5),
        new("SKU-001", "WH-2", 50, 0),

        new("SKU-002", "WH-1", 50, 10),
        new("SKU-002", "WH-3", 30, 0),

        new("SKU-003", "WH-1", 3, 2),
        new("SKU-003", "WH-2", 2, 0),

        new("SKU-004", "WH-1", 0, 0),
        new("SKU-004", "WH-2", 40, 0),

        new("SKU-005", "WH-1", 20, 0),
        new("SKU-005", "WH-3", 15, 0),
        new("SKU-005", "WH-5", 5, 0),

        new("SKU-006", "WH-1", 80, 0),

        new("SKU-007", "WH-1", 0, 0),
        new("SKU-007", "WH-2", 0, 0),

        new("SKU-008", "WH-2", 60, 0),
        new("SKU-008", "WH-4", 25, 0),

        new("SKU-009", "WH-1", 10, 0),
        new("SKU-009", "WH-2", 10, 0),
        new("SKU-009", "WH-3", 10, 0),
        new("SKU-009", "WH-4", 10, 0),
        new("SKU-009", "WH-5", 10, 0),

        new("SKU-010", "WH-3", 2, 0),
        new("SKU-010", "WH-5", 4, 0),
    ];

    public static IReadOnlyCollection<SeedReservation> Reservations { get; } =
    [
        new(
            "res-seed-pending-001",
            "order-seed-pending-001",
            ReservationStatus.Pending.ToString(),
            TimeSpan.FromHours(2),
            [
                new("SKU-001", "WH-1", 5),
                new("SKU-002", "WH-1", 10),
                new("SKU-003", "WH-1", 2),
            ]),
        new(
            "res-seed-expired-002",
            "order-seed-expired-002",
            ReservationStatus.Expired.ToString(),
            TimeSpan.FromHours(-2),
            [
                new("SKU-001", "WH-1", 2),
            ]),
        new(
            "res-seed-confirmed-003",
            "order-seed-confirmed-003",
            ReservationStatus.Confirmed.ToString(),
            TimeSpan.FromHours(1),
            [
                new("SKU-008", "WH-2", 4),
            ]),
    ];

    public static IReadOnlyCollection<SeedInventoryTransaction> InventoryTransactions { get; } =
    [
        .. BuildInitialStockTransactions(),
        new("SKU-001", "WH-1", InventoryTransactionType.Reserve.ToString(), -5, 5, "corr-seed-pending-001", "res-seed-pending-001", "order-seed-pending-001"),
        new("SKU-002", "WH-1", InventoryTransactionType.Reserve.ToString(), -10, 10, "corr-seed-pending-001", "res-seed-pending-001", "order-seed-pending-001"),
        new("SKU-003", "WH-1", InventoryTransactionType.Reserve.ToString(), -2, 2, "corr-seed-pending-001", "res-seed-pending-001", "order-seed-pending-001"),
        new("SKU-001", "WH-1", InventoryTransactionType.Reserve.ToString(), -2, 2, "corr-seed-expired-002", "res-seed-expired-002", "order-seed-expired-002"),
        new("SKU-001", "WH-1", InventoryTransactionType.Release.ToString(), 2, -2, "corr-seed-expired-002-release", "res-seed-expired-002", "order-seed-expired-002"),
        new("SKU-008", "WH-2", InventoryTransactionType.Reserve.ToString(), -4, 4, "corr-seed-confirmed-003", "res-seed-confirmed-003", "order-seed-confirmed-003"),
        new("SKU-008", "WH-2", InventoryTransactionType.Confirm.ToString(), 0, -4, "corr-seed-confirmed-003-confirm", "res-seed-confirmed-003", "order-seed-confirmed-003"),
    ];

    private static IEnumerable<SeedInventoryTransaction> BuildInitialStockTransactions()
    {
        foreach (var item in InventoryItems)
        {
            var reservationOffset = item.QuantityReserved;
            if (item.Sku == "SKU-008" && item.WarehouseId == "WH-2")
            {
                reservationOffset = 4;
            }

            yield return new SeedInventoryTransaction(
                item.Sku,
                item.WarehouseId,
                InventoryTransactionType.AdjustStock.ToString(),
                item.QuantityAvailable + reservationOffset,
                0,
                $"corr-seed-initial-load-{item.Sku}-{item.WarehouseId}",
                Reason: "Initial demo inventory seed");
        }
    }
}
