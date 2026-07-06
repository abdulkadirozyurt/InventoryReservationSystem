using InventoryService.Application.Inventory.Abstractions;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace InventoryService.Infrastructure.Repositories.Inventory;

public sealed class InventoryUnitOfWork(
    IMongoClient mongoClient,
    ILogger<InventoryUnitOfWork> logger) : IInventoryUnitOfWork
{
    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        using var session = await mongoClient.StartSessionAsync(cancellationToken: cancellationToken);

        try
        {
            session.StartTransaction();

            await operation(cancellationToken);

            await session.CommitTransactionAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            if (session.IsInTransaction)
            {
                await session.AbortTransactionAsync(cancellationToken);
            }

            logger.LogError(
                exception,
                "MongoDB transaction failed and was rolled back. ErrorCategory: {ErrorCategory}",
                "MongoTransactionError");

            throw;
        }
    }
}
