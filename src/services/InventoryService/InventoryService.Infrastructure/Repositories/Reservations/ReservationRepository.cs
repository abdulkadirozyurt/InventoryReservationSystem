using InventoryService.Application.Inventory.Exceptions;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Domain.Reservations;
using InventoryService.Infrastructure.Mongo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace InventoryService.Infrastructure.Repositories.Reservations;

public sealed class ReservationRepository(
    IMongoDatabase database,
    IMongoSessionProvider mongoSessionProvider,
    IOptions<MongoDbOptions> options,
    ILogger<ReservationRepository> logger) : IReservationRepository
{
    private readonly IMongoCollection<Reservation> _collection = database.GetCollection<Reservation>(options.Value.ReservationsCollectionName);

    public async Task AddAsync(Reservation reservation, CancellationToken cancellationToken = default)
    {
        try
        {
            var session = mongoSessionProvider.CurrentSession;
            if (session is not null)
            {
                await _collection.InsertOneAsync(session, reservation, cancellationToken: cancellationToken);
                return;
            }

            await _collection.InsertOneAsync(reservation, cancellationToken: cancellationToken);
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while adding reservation. ReservationId: {ReservationId}, OrderId: {OrderId}, ErrorCategory: {ErrorCategory}",
                reservation.ReservationId,
                reservation.OrderId,
                "TransientMongoError");
            throw new InventoryStoreUnavailableException("Inventory store is unavailable while adding reservation", exception);
        }
    }

    public async Task<Reservation?> GetByIdAsync(string reservationId, CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = Builders<Reservation>.Filter.Eq(reservation => reservation.ReservationId, reservationId);
            var session = mongoSessionProvider.CurrentSession;

            if (session is not null)
                return await _collection.Find(session, filter).FirstOrDefaultAsync(cancellationToken);

            return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while retrieving reservation. ReservationId: {ReservationId}, ErrorCategory: {ErrorCategory}",
                reservationId,
                "TransientMongoError");
            throw new InventoryStoreUnavailableException("Inventory store is unavailable while retrieving reservation", exception);
        }
    }

    public async Task UpdateAsync(Reservation reservation, CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = Builders<Reservation>.Filter.Eq(existingReservation => existingReservation.ReservationId, reservation.ReservationId);

            var session = mongoSessionProvider.CurrentSession;
            if (session is not null)
            {
                await _collection.ReplaceOneAsync(
                    session,
                    filter,
                    reservation,
                    cancellationToken: cancellationToken);

                return;
            }

            await _collection.ReplaceOneAsync(filter, reservation, cancellationToken: cancellationToken);
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while updating reservation. ReservationId: {ReservationId}, OrderId: {OrderId}, ErrorCategory: {ErrorCategory}",
                reservation.ReservationId,
                reservation.OrderId,
                "TransientMongoError");

            throw new InventoryStoreUnavailableException("Inventory store is unavailable while updating reservation", exception);
        }
    }
}
