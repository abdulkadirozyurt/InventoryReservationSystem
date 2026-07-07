using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Inventory.Exceptions;
using InventoryService.Domain.InventoryTransactions;
using InventoryService.Infrastructure.Mongo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace InventoryService.Infrastructure.Repositories.Inventory;

public sealed class InventoryTransactionRepository(
    IMongoDatabase database,
    IMongoSessionProvider mongoSessionProvider,
    IOptions<MongoDbOptions> options,
    ILogger<InventoryTransactionRepository> logger) : IInventoryTransactionRepository
{
    private readonly IMongoCollection<InventoryTransaction> _collection =
        database.GetCollection<InventoryTransaction>(options.Value.InventoryTransactionsCollectionName);

    public async Task AddAsync(InventoryTransaction transaction, CancellationToken cancellationToken = default)
    {
        try
        {
            var session = mongoSessionProvider.CurrentSession;
            if (session is not null)
            {
                await _collection.InsertOneAsync(session, transaction, cancellationToken: cancellationToken);
                return;
            }

            await _collection.InsertOneAsync(transaction, cancellationToken: cancellationToken);
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while adding inventory transaction. Sku: {Sku}, WarehouseId: {WarehouseId}, Type: {TransactionType}, ReservationId: {ReservationId}, OrderId: {OrderId}, ErrorCategory: {ErrorCategory}",
                transaction.Sku,
                transaction.WarehouseId,
                transaction.Type,
                transaction.ReservationId,
                transaction.OrderId,
                "TransientMongoError");
            throw new InventoryStoreUnavailableException("Inventory store is unavailable while adding inventory transaction", exception);
        }

    }
}
