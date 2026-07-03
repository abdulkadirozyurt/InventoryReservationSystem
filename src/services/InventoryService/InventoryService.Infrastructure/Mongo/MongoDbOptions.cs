namespace InventoryService.Infrastructure.Mongo;

public sealed class MongoDbOptions
{
    public string DatabaseName { get; set; } = "inventory-service";
    public string InventoryItemsCollectionName { get; set; } = "InventoryItems";
    public string ReservationsCollectionName { get; set; } = "Reservations";
}
