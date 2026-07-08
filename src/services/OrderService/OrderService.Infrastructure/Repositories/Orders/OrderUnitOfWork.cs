using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using OrderService.Application.Orders.Abstractions;
using OrderService.Infrastructure.Mongo;

namespace OrderService.Infrastructure.Repositories.Orders;

public sealed class OrderUnitOfWork(
    IMongoClient mongoClient,
    IMongoSessionProvider mongoSessionProvider,
    ILogger<OrderUnitOfWork> logger) : IOrderUnitOfWork
{
    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        using var session = await mongoClient.StartSessionAsync(cancellationToken: cancellationToken);

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
                await session.AbortTransactionAsync(CancellationToken.None);
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
