using MongoDB.Driver;

namespace OrderService.Infrastructure.Mongo;

public interface IMongoSessionProvider
{
    IClientSessionHandle? CurrentSession { get; set; }
}
