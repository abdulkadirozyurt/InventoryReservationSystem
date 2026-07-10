using InventoryService.Infrastructure.Mongo;
using Microsoft.Extensions.Options;

namespace InventoryService.Infrastructure.CollectionInitializers;

public sealed class MongoCollectionInitializer(
    InventoryItemsCollectionInitializer inventoryItemsInitializer,
    ReservationsCollectionInitializer reservationsInitializer,
    InventoryTransactionsCollectionInitializer inventoryTransactionsInitializer,
    CheckpointsCollectionInitializer checkpointsInitializer,
    DeadLetterQueueCollectionInitializer deadLetterQueueInitializer,
    InventorySnapshotsCollectionInitializer inventorySnapshotsInitializer,
    InventorySeedDataService inventorySeedDataService,
    IOptions<SeedDataOptions> seedDataOptions)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await inventoryItemsInitializer.InitializeAsync(cancellationToken);
        await reservationsInitializer.InitializeAsync(cancellationToken);
        await inventoryTransactionsInitializer.InitializeAsync(cancellationToken);
        await checkpointsInitializer.InitializeAsync(cancellationToken);
        await deadLetterQueueInitializer.InitializeAsync(cancellationToken);
        await inventorySnapshotsInitializer.InitializeAsync(cancellationToken);

        if (seedDataOptions.Value.Enabled)
        {
            await inventorySeedDataService.SeedAsync(cancellationToken);
        }
    }
}
