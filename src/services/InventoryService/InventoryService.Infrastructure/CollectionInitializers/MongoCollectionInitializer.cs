namespace InventoryService.Infrastructure.CollectionInitializers;

public sealed class MongoCollectionInitializer(
    InventoryItemsCollectionInitializer inventoryItemsInitializer,
    ReservationsCollectionInitializer reservationsInitializer,
    InventoryTransactionsCollectionInitializer inventoryTransactionsInitializer)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await inventoryItemsInitializer.InitializeAsync(cancellationToken);
        await reservationsInitializer.InitializeAsync(cancellationToken);
        await inventoryTransactionsInitializer.InitializeAsync(cancellationToken);
    }
}
