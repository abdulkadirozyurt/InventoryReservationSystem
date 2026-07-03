namespace InventoryService.Infrastructure.Mongo;

public sealed class MongoCollectionInitializer(
    InventoryItemsCollectionInitializer inventoryItemsInitializer,
    ReservationsCollectionInitializer reservationsInitializer)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await inventoryItemsInitializer.InitializeAsync(cancellationToken);
        await reservationsInitializer.InitializeAsync(cancellationToken);
    }
}
