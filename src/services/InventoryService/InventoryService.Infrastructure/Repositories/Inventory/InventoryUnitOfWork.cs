using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Infrastructure.Mongo;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace InventoryService.Infrastructure.Repositories.Inventory;

public sealed class InventoryUnitOfWork(
    IMongoClient mongoClient,
    IMongoSessionProvider mongoSessionProvider,
    ILogger<InventoryUnitOfWork> logger) : IInventoryUnitOfWork
{
    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        using var session = await mongoClient.StartSessionAsync(cancellationToken: cancellationToken);

        // Mevcut DI scope içindeki aktif MongoDB session'ını tutar.
        // UnitOfWork transaction başlattığında bunu set eder; repository'ler aynı Mongo transaction'ına katılmak için buradan okur.
        mongoSessionProvider.CurrentSession = session;

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
        finally
        {
            mongoSessionProvider.CurrentSession = null;
        }
    }
}
