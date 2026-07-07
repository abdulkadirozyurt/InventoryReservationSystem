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
}
