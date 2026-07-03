namespace InventoryService.Domain.InventoryTransactions;

public enum InventoryTransactionType
{
    Reserve = 1,
    Release = 2,
    Confirm = 3,
    AdjustStock = 4,
    Rebalance = 5,
    SnapshotRestore = 6
}
