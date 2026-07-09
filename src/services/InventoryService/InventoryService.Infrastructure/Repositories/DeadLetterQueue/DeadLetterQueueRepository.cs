using InventoryService.Application.Inventory.Exceptions;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Domain.DeadLetterQueue;
using InventoryService.Infrastructure.Mongo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace InventoryService.Infrastructure.Repositories.DeadLetterQueue;

public sealed class DeadLetterQueueRepository(
    IMongoDatabase database,
    IMongoSessionProvider mongoSessionProvider,
    IOptions<MongoDbOptions> options,
    ILogger<DeadLetterQueueRepository> logger) : IDeadLetterQueueRepository
{
    private readonly IMongoCollection<DeadLetterRecord> _collection = database.GetCollection<DeadLetterRecord>(options.Value.DeadLetterQueueCollectionName);

    public async Task<int> UpsertFailureAsync(DeadLetterRecord record, CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = Builders<DeadLetterRecord>.Filter.Eq(dlq => dlq.OperationType, record.OperationType) &
                         Builders<DeadLetterRecord>.Filter.Eq(dlq => dlq.ReservationId, record.ReservationId) &
                         Builders<DeadLetterRecord>.Filter.Eq(dlq => dlq.OrderId, record.OrderId);

            var now = DateTime.UtcNow;
            // FindOneAndUpdate + IsUpsert + Inc: tek atomik islemde kaydi olusturur veya gunceller, retry sayacini bir artirir.
            // Bu pattern race condition'u onler - iki job ayni anda calissa retryCount cift artmaz/kaybolmaz.
            var update = Builders<DeadLetterRecord>.Update
                .SetOnInsert(dlq => dlq.OperationType, record.OperationType)
                .SetOnInsert(dlq => dlq.ReservationId, record.ReservationId)
                .SetOnInsert(dlq => dlq.OrderId, record.OrderId)
                .SetOnInsert(dlq => dlq.CreatedAt, record.CreatedAt)
                .Set(dlq => dlq.Reason, record.Reason)
                .Set(dlq => dlq.ErrorCategory, record.ErrorCategory)
                .Set(dlq => dlq.CorrelationId, record.CorrelationId)
                .Set(dlq => dlq.PayloadSnapshot, record.PayloadSnapshot)
                .Set(dlq => dlq.UpdatedAt, now)
                .Inc(dlq => dlq.RetryCount, 1);

            var options = new FindOneAndUpdateOptions<DeadLetterRecord>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            };

            var session = mongoSessionProvider.CurrentSession;
            DeadLetterRecord updated;
            if (session is not null)
            {
                updated = await _collection.FindOneAndUpdateAsync(session, filter, update, options, cancellationToken);
            }
            else
            {
                updated = await _collection.FindOneAndUpdateAsync(filter, update, options, cancellationToken);
            }

            return updated.RetryCount;
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while upserting DLQ record. CorrelationId: {CorrelationId}, ReservationId: {ReservationId}, OrderId: {OrderId}, ErrorCategory: {ErrorCategory}",
                record.CorrelationId,
                record.ReservationId,
                record.OrderId,
                "TransientMongoError");
            throw new InventoryStoreUnavailableException("Inventory store is unavailable while upserting DLQ record", exception);
        }
    }
}
