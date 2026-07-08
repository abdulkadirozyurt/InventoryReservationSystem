namespace OrderService.Infrastructure.Mongo;

public sealed class MongoDbOptions
{
    public string DatabaseName { get; set; } = string.Empty;
    public string OrdersCollectionName { get; set; } = string.Empty;
    public string OrderHistoryCollectionName { get; set; } = string.Empty;
}
