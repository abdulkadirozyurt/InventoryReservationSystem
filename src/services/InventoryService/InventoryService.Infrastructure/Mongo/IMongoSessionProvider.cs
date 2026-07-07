using MongoDB.Driver;

namespace InventoryService.Infrastructure.Mongo;

public interface IMongoSessionProvider
{
    IClientSessionHandle? CurrentSession { get; set; }
}
