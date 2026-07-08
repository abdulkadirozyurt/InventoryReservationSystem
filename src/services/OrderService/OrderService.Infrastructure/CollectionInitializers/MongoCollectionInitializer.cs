namespace OrderService.Infrastructure.CollectionInitializers;

public sealed class MongoCollectionInitializer(
    OrdersCollectionInitializer ordersInitializer,
    OrderHistoryCollectionInitializer orderHistoryInitializer)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await ordersInitializer.InitializeAsync(cancellationToken);
        await orderHistoryInitializer.InitializeAsync(cancellationToken);
    }
}
