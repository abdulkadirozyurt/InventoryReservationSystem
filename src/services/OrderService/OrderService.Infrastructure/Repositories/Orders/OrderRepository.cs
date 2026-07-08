using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using OrderService.Application.Orders.Abstractions;
using OrderService.Application.Orders.Exceptions;
using OrderService.Domain.Orders;
using OrderService.Infrastructure.Mongo;

namespace OrderService.Infrastructure.Repositories.Orders;

public sealed class OrderRepository(
    IMongoDatabase database,
    IMongoSessionProvider mongoSessionProvider,
    IOptions<MongoDbOptions> options,
    ILogger<OrderRepository> logger) : IOrderRepository
{
    private readonly IMongoCollection<Order> _collection = database.GetCollection<Order>(options.Value.OrdersCollectionName);

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        try
        {
            var session = mongoSessionProvider.CurrentSession;
            if (session is not null)
            {
                await _collection.InsertOneAsync(session, order, cancellationToken: cancellationToken);
                return;
            }

            await _collection.InsertOneAsync(order, cancellationToken: cancellationToken);
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while adding order. OrderNumber: {OrderNumber}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}",
                order.OrderNumber,
                order.ReservationId,
                "TransientMongoError");

            throw new OrderStoreUnavailableException("Order store is unavailable while adding order", exception);
        }
    }

    public async Task<Order?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = Builders<Order>.Filter.Eq(order => order.OrderNumber, orderNumber);
            var session = mongoSessionProvider.CurrentSession;

            if (session is not null)
                return await _collection.Find(session, filter).FirstOrDefaultAsync(cancellationToken);

            return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while retrieving order. OrderNumber: {OrderNumber}, ErrorCategory: {ErrorCategory}",
                orderNumber,
                "TransientMongoError");

            throw new OrderStoreUnavailableException("Order store is unavailable while retrieving order", exception);
        }
    }

    public async Task<Order?> GetByReservationIdAsync(string reservationId, CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = Builders<Order>.Filter.Eq(order => order.ReservationId, reservationId);
            var session = mongoSessionProvider.CurrentSession;

            if (session is not null)
                return await _collection.Find(session, filter).FirstOrDefaultAsync(cancellationToken);

            return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while retrieving order by reservation. ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}",
                reservationId,
                "TransientMongoError");

            throw new OrderStoreUnavailableException("Order store is unavailable while retrieving order by reservation", exception);
        }
    }

    public async Task UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = Builders<Order>.Filter.Eq(existingOrder => existingOrder.OrderNumber, order.OrderNumber);
            var session = mongoSessionProvider.CurrentSession;
            ReplaceOneResult result;

            if (session is not null)
            {
                result = await _collection.ReplaceOneAsync(
                    session,
                    filter,
                    order,
                    cancellationToken: cancellationToken);
            }
            else
            {
                result = await _collection.ReplaceOneAsync(filter, order, cancellationToken: cancellationToken);
            }

            if (result.MatchedCount == 0)
            {
                logger.LogWarning(
                    "MongoDB order update matched no documents. OrderNumber: {OrderNumber}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}",
                    order.OrderNumber,
                    order.ReservationId,
                    "OrderUpdateNotMatched");

                throw new OrderStoreUnavailableException(
                    "Order store is unavailable while updating order",
                    new InvalidOperationException("Order update matched no documents."));
            }
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while updating order. OrderNumber: {OrderNumber}, ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}",
                order.OrderNumber,
                order.ReservationId,
                "TransientMongoError");

            throw new OrderStoreUnavailableException("Order store is unavailable while updating order", exception);
        }
    }
}
