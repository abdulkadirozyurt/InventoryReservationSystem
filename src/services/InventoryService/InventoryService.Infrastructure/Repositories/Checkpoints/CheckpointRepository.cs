using InventoryService.Application.Inventory.Exceptions;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Domain.Checkpoints;
using InventoryService.Infrastructure.Mongo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace InventoryService.Infrastructure.Repositories.Checkpoints;

public sealed class CheckpointRepository(
    IMongoDatabase database,
    IMongoSessionProvider mongoSessionProvider,
    IOptions<MongoDbOptions> options,
    ILogger<CheckpointRepository> logger) : ICheckpointRepository
{
    private readonly IMongoCollection<Checkpoint> _collection = database.GetCollection<Checkpoint>(options.Value.CheckpointsCollectionName);

    public async Task<Checkpoint?> GetByNameAsync(string jobName, CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = Builders<Checkpoint>.Filter.Eq(c => c.JobName, jobName);
            var session = mongoSessionProvider.CurrentSession;
            if (session is not null)
            {
                return await _collection.Find(session, filter).FirstOrDefaultAsync(cancellationToken);
            }

            return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while retrieving checkpoint. JobName: {JobName}, ErrorCategory: {ErrorCategory}",
                jobName,
                "TransientMongoError");
            throw new InventoryStoreUnavailableException("Inventory store is unavailable while retrieving checkpoint", exception);
        }
    }

    public async Task SaveAsync(Checkpoint checkpoint, CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = Builders<Checkpoint>.Filter.Eq(c => c.JobName, checkpoint.JobName);
            var replaceOptions = new ReplaceOptions { IsUpsert = true };
            var session = mongoSessionProvider.CurrentSession;
            if (session is not null)
            {
                await _collection.ReplaceOneAsync(session, filter, checkpoint, replaceOptions, cancellationToken);
                return;
            }

            await _collection.ReplaceOneAsync(filter, checkpoint, replaceOptions, cancellationToken);
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while saving checkpoint. JobName: {JobName}, ErrorCategory: {ErrorCategory}",
                checkpoint.JobName,
                "TransientMongoError");
            throw new InventoryStoreUnavailableException("Inventory store is unavailable while saving checkpoint", exception);
        }
    }
}
