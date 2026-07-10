namespace InventoryService.Infrastructure.Mongo;

public sealed class MongoDbOptions
{
    public string DatabaseName { get; set; }
    public string InventoryItemsCollectionName { get; set; }
    public string ReservationsCollectionName { get; set; }
    public string InventoryTransactionsCollectionName { get; set; }
    public string CheckpointsCollectionName { get; set; } = "checkpoints";
    public string DeadLetterQueueCollectionName { get; set; } = "dead-letter-queue";
    public string InventorySnapshotsCollectionName { get; set; } = "inventory-snapshots";
}
