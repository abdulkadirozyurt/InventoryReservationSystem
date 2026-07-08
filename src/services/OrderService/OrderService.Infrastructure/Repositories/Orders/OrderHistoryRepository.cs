using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using OrderService.Application.Orders.Abstractions;
using OrderService.Application.Orders.Exceptions;
using OrderService.Domain.Orders;
using OrderService.Infrastructure.Mongo;

namespace OrderService.Infrastructure.Repositories.Orders;

public sealed class OrderHistoryRepository(
    IMongoDatabase database,
    IMongoSessionProvider mongoSessionProvider,
    IOptions<MongoDbOptions> options,
    ILogger<OrderHistoryRepository> logger) : IOrderHistoryRepository
{
    private readonly IMongoCollection<OrderHistory> _collection = database.GetCollection<OrderHistory>(options.Value.OrderHistoryCollectionName);

    public async Task AddAsync(OrderHistory history, CancellationToken cancellationToken = default)
    {
        try
        {
            var session = mongoSessionProvider.CurrentSession;
            if (session is not null)
            {
                await _collection.InsertOneAsync(session, history, cancellationToken: cancellationToken);
                return;
            }

            await _collection.InsertOneAsync(history, cancellationToken: cancellationToken);
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while adding order history. OrderNumber: {OrderNumber}, CorrelationId: {CorrelationId}, ErrorCategory: {ErrorCategory}",
                history.OrderNumber,
                history.CorrelationId,
                "TransientMongoError");

            throw new OrderStoreUnavailableException("Order store is unavailable while adding order history", exception);
        }
    }

    public async Task<IReadOnlyCollection<OrderHistory>> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = Builders<OrderHistory>.Filter.Eq(history => history.OrderNumber, orderNumber);
            var sort = Builders<OrderHistory>.Sort.Ascending(history => history.ChangedAt);
            var session = mongoSessionProvider.CurrentSession;

            if (session is not null)
                return await _collection.Find(session, filter).Sort(sort).ToListAsync(cancellationToken);

            return await _collection.Find(filter).Sort(sort).ToListAsync(cancellationToken);
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while retrieving order history. OrderNumber: {OrderNumber}, ErrorCategory: {ErrorCategory}",
                orderNumber,
                "TransientMongoError");

            throw new OrderStoreUnavailableException("Order store is unavailable while retrieving order history", exception);
        }
    }
}
