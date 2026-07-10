using InventoryService.Infrastructure.CollectionInitializers;
using InventoryService.Infrastructure.Mongo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using NSubstitute;
using Xunit;

namespace InventoryService.UnitTests;

public sealed class InventorySeedDataServiceTests
{
    [Fact]
    public async Task SeedAsync_UpsertsInventoryReservationsAndTransactions()
    {
        var database = Substitute.For<IMongoDatabase>();
        var collection = Substitute.For<IMongoCollection<BsonDocument>>();
        var logger = Substitute.For<ILogger<InventorySeedDataService>>();
        var options = Options.Create(new MongoDbOptions
        {
            InventoryItemsCollectionName = "inventory-items",
            ReservationsCollectionName = "reservations",
            InventoryTransactionsCollectionName = "inventory-transactions"
        });

        database.GetCollection<BsonDocument>(Arg.Any<string>(), Arg.Any<MongoCollectionSettings>())
            .Returns(collection);

        var service = new InventorySeedDataService(database, options, logger);

        await service.SeedAsync(CancellationToken.None);

        database.Received(1).GetCollection<BsonDocument>("inventory-items", Arg.Any<MongoCollectionSettings>());
        database.Received(1).GetCollection<BsonDocument>("reservations", Arg.Any<MongoCollectionSettings>());
        database.Received(1).GetCollection<BsonDocument>("inventory-transactions", Arg.Any<MongoCollectionSettings>());

        await collection.Received().UpdateOneAsync(
            Arg.Any<FilterDefinition<BsonDocument>>(),
            Arg.Any<UpdateDefinition<BsonDocument>>(),
            Arg.Is<UpdateOptions>(updateOptions => updateOptions.IsUpsert),
            Arg.Any<CancellationToken>());
    }
}
