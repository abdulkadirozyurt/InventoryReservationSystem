using MongoDB.Driver;

namespace InventoryService.Infrastructure.Mongo;

public sealed class MongoSessionProvider : IMongoSessionProvider
{
    public IClientSessionHandle? CurrentSession { get; set; }
}
