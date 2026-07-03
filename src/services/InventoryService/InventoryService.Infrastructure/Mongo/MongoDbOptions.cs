namespace InventoryService.Infrastructure.Mongo;

public sealed class MongoDbOptions
{
    public string DatabaseName { get; set; } 
    public string InventoryItemsCollectionName { get; set; } 
    public string ReservationsCollectionName { get; set; } 
    public string InventoryTransactionsCollectionName { get; set; } 
}
