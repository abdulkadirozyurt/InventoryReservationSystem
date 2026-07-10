using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Inventory.Exceptions;
using InventoryService.Domain.Inventory;
using InventoryService.Infrastructure.Mongo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace InventoryService.Infrastructure.Repositories.Inventory;

public sealed class InventorySnapshotRepository(
    IMongoDatabase database,
    IMongoSessionProvider mongoSessionProvider,
    IOptions<MongoDbOptions> options,
    ILogger<InventorySnapshotRepository> logger) : IInventorySnapshotRepository
{
    private readonly IMongoCollection<InventorySnapshot> _collection = database.GetCollection<InventorySnapshot>(options.Value.InventorySnapshotsCollectionName);

    public async Task AddAsync(InventorySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        try
        {
            var session = mongoSessionProvider.CurrentSession;
            if (session != null)
            {
                await _collection.InsertOneAsync(session, snapshot, cancellationToken: cancellationToken);
            }
            else
            {
                await _collection.InsertOneAsync(snapshot, cancellationToken: cancellationToken);
            }
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while adding inventory snapshot. SnapshotId: {SnapshotId}, ErrorCategory: {ErrorCategory}",
                snapshot.SnapshotId,
                "TransientMongoError");
            throw new InventoryStoreUnavailableException("Inventory store is unavailable while adding snapshot", exception);
        }
    }

    public async Task<InventorySnapshot?> GetByIdAsync(string snapshotId, CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = Builders<InventorySnapshot>.Filter.Eq(s => s.SnapshotId, snapshotId);
            var session = mongoSessionProvider.CurrentSession;

            if (session != null)
            {
                return await _collection.Find(session, filter).FirstOrDefaultAsync(cancellationToken);
            }

            return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while getting inventory snapshot by ID. SnapshotId: {SnapshotId}, ErrorCategory: {ErrorCategory}",
                snapshotId,
                "TransientMongoError");
            throw new InventoryStoreUnavailableException("Inventory store is unavailable while querying snapshot", exception);
        }
    }
}
