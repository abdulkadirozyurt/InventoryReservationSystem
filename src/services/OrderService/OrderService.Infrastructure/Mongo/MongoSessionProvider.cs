using MongoDB.Driver;

namespace OrderService.Infrastructure.Mongo;

public sealed class MongoSessionProvider : IMongoSessionProvider
{
    public IClientSessionHandle? CurrentSession { get; set; }
}
