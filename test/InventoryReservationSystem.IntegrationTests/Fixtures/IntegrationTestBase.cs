using InventoryService.Domain.Inventory;
using InventoryService.Domain.InventoryTransactions;
using InventoryService.Domain.Reservations;
using MongoDB.Driver;
using Xunit;

namespace InventoryReservationSystem.IntegrationTests.Fixtures;

public abstract class IntegrationTestBase : IClassFixture<InventoryServiceFixture>
{
    protected InventoryServiceFixture Fixture { get; }

    protected IntegrationTestBase(InventoryServiceFixture fixture)
    {
        Fixture = fixture;
    }

    protected T Resolve<T>() where T : notnull
    {
        return Fixture.Resolve<T>();
    }

    protected async Task<InventoryItem?> GetInventoryItemAsync(string sku, string warehouseId)
    {
        var collection = Fixture.Database.GetCollection<InventoryItem>("inventoryItems");
        var filter = Builders<InventoryItem>.Filter.And(
            Builders<InventoryItem>.Filter.Eq(i => i.Sku, sku),
            Builders<InventoryItem>.Filter.Eq(i => i.WarehouseId, warehouseId));

        return await collection.Find(filter).FirstOrDefaultAsync();
    }

    protected async Task<Reservation?> GetReservationAsync(string orderId)
    {
        var collection = Fixture.Database.GetCollection<Reservation>("reservations");
        var filter = Builders<Reservation>.Filter.Eq(r => r.OrderId, orderId);

        return await collection.Find(filter).FirstOrDefaultAsync();
    }

    protected async Task<long> GetTransactionCountAsync(string sku, string warehouseId)
    {
        var collection = Fixture.Database.GetCollection<InventoryTransaction>("inventoryTransactions");
        var filter = Builders<InventoryTransaction>.Filter.And(
            Builders<InventoryTransaction>.Filter.Eq(t => t.Sku, sku),
            Builders<InventoryTransaction>.Filter.Eq(t => t.WarehouseId, warehouseId));

        return await collection.CountDocumentsAsync(filter);
    }
}
