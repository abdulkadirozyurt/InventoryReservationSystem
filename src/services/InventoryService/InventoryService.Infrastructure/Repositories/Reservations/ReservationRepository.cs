using InventoryService.Application.Inventory.Exceptions;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Application.Reservations.Results.Reconciliation;
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
        catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // MongoDB'deki unique orderId index'i aynı order için ikinci reservation kaydına izin vermez.
            // Bu hata database arızası değildir; iki paralel isteğin aynı order'ı kaydetmeye çalıştığını gösterir.
            // Özel exception fırlatarak handler'ın transaction rollback sonrası mevcut reservation'ı döndürmesini sağlıyoruz.
            logger.LogWarning(
                exception,
                "MongoDB rejected a duplicate reservation. ReservationId: {ReservationId}, OrderId: {OrderId}, ErrorCategory: {ErrorCategory}",
                reservation.ReservationId,
                reservation.OrderId,
                "DuplicateReservation");

            throw new DuplicateReservationException(
                "A reservation already exists for this order",
                exception);
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

    public async Task<Reservation?> GetByReservationIdAsync(string reservationId, CancellationToken cancellationToken = default)
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

    public async Task<Reservation?> GetByOrderIdAsync(string orderId, CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = Builders<Reservation>.Filter.Eq(
                reservation => reservation.OrderId,
                orderId);

            // Mongo transaction açıksa aynı session üzerinden okuyoruz.
            // Böylece reservation kontrolü transaction'ın geri kalanıyla aynı bağlamda çalışıyor.
            var session = mongoSessionProvider.CurrentSession;

            if (session is not null)
            {
                return await _collection
                    .Find(session, filter)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            return await _collection
                .Find(filter)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while retrieving reservation by order. OrderId: {OrderId}, ErrorCategory: {ErrorCategory}",
                orderId,
                "TransientMongoError");

            throw new InventoryStoreUnavailableException(
                "Inventory store is unavailable while retrieving reservation by order",
                exception);
        }
    }

    public async Task UpdateAsync(Reservation reservation, CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = Builders<Reservation>.Filter.Eq(existingReservation => existingReservation.ReservationId, reservation.ReservationId);

            var session = mongoSessionProvider.CurrentSession;
            ReplaceOneResult result;

            if (session is not null)
            {
                result = await _collection.ReplaceOneAsync(
                    session,
                    filter,
                    reservation,
                    cancellationToken: cancellationToken);
            }
            else
            {
                result = await _collection.ReplaceOneAsync(filter, reservation, cancellationToken: cancellationToken);
            }

            // Update sessizce 0 document etkilerse release başarılı sanılır; bu yüzden explicit hata fırlatılır.
            if (result.MatchedCount == 0)
            {
                logger.LogWarning(
                    "MongoDB reservation update matched no documents. ReservationId: {ReservationId}, OrderId: {OrderId}, ErrorCategory: {ErrorCategory}",
                    reservation.ReservationId,
                    reservation.OrderId,
                    "ReservationUpdateNotMatched");

                throw new InventoryStoreUnavailableException(
                    "Inventory store is unavailable while updating reservation",
                    new InvalidOperationException("Reservation update matched no documents."));
            }
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

    public async Task<List<Reservation>> GetExpiredPendingReservationsAsync(
        DateTime now,
        DateTime? cursorTimestamp,
        string? lastReservationId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var builder = Builders<Reservation>.Filter;
            var filter = builder.Eq(r => r.Status, ReservationStatus.Pending) &
                         builder.Lte(r => r.ExpiresAt, now);

            // Composite cursor: ExpiresAt + ReservationId ile deterministic pagination.
            // Skip/offset kullanilmaz cunku expire olan kayitlar silinmez, job restart'inda ayni kayit atlanmaz/tekrar islenmez.
            if (cursorTimestamp.HasValue && !string.IsNullOrEmpty(lastReservationId))
            {
                var cursorFilter = builder.Or(
                    builder.Gt(r => r.ExpiresAt, cursorTimestamp.Value),
                    builder.And(
                        builder.Eq(r => r.ExpiresAt, cursorTimestamp.Value),
                        builder.Gt(r => r.ReservationId, lastReservationId)
                    )
                );
                filter = builder.And(filter, cursorFilter);
            }

            var sort = Builders<Reservation>.Sort
                .Ascending(r => r.ExpiresAt)
                .Ascending(r => r.ReservationId);

            var session = mongoSessionProvider.CurrentSession;
            if (session is not null)
            {
                return await _collection.Find(session, filter)
                    .Sort(sort)
                    .Limit(limit)
                    .ToListAsync(cancellationToken);
            }

            return await _collection.Find(filter)
                .Sort(sort)
                .Limit(limit)
                .ToListAsync(cancellationToken);
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while querying expired reservations. ErrorCategory: {ErrorCategory}",
                "TransientMongoError");

            throw new InventoryStoreUnavailableException("Inventory store is unavailable while querying expired reservations", exception);
        }
    }

    public async Task<IReadOnlyCollection<ExpectedReservedQuantitySnapshot>> GetExpectedReservedQuantityBySkuWarehouseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = Builders<Reservation>.Filter.Eq(r => r.Status, ReservationStatus.Pending);

            List<Reservation> pendingReservations;
            var session = mongoSessionProvider.CurrentSession;
            if (session is not null)
            {
                pendingReservations = await _collection.Find(session, filter).ToListAsync(cancellationToken);
            }
            else
            {
                pendingReservations = await _collection.Find(filter).ToListAsync(cancellationToken);
            }

            // Architecture decisions:
            // 1. Group by SKU+warehouse because inventory reserved quantity is stored at that exact physical stock granularity.
            // 2. Keep this report-only: reconciliation must expose drift, not correct it while live reserve/release transactions may be running.
            // 3. Keep this InventoryService-owned: order correlation is limited to stored OrderId values; no OrderService database/API read is needed for this MVP.
            var result = pendingReservations
                .SelectMany(r => r.Items.Select(item => new { Reservation = r, Item = item }))
                .GroupBy(x => (x.Item.Sku, x.Item.WarehouseId))
                .Select(group => new ExpectedReservedQuantitySnapshot(
                    Sku: group.Key.Sku,
                    WarehouseId: group.Key.WarehouseId,
                    ExpectedReservedQuantity: group.Sum(x => x.Item.Quantity),
                    ReservationIds: group.Select(x => x.Reservation.ReservationId).Distinct().ToList(),
                    OrderIds: group.Select(x => x.Reservation.OrderId).Distinct().ToList()
                ))
                .ToList();

            return result;
        }
        catch (MongoException exception)
        {
            logger.LogError(
                exception,
                "MongoDB failed while retrieving expected reserved quantity by SKU and warehouse. ErrorCategory: {ErrorCategory}",
                "TransientMongoError");

            throw new InventoryStoreUnavailableException("Inventory store is unavailable while retrieving expected reserved quantity by SKU and warehouse", exception);
        }
    }
}
