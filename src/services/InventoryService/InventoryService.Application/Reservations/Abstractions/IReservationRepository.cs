using InventoryService.Domain.Reservations;

namespace InventoryService.Application.Reservations.Abstractions;

public interface IReservationRepository
{
    /// <summary>
    /// Persists a new reservation record.
    /// </summary>
    /// <param name="reservation">The reservation to add.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task AddAsync(Reservation reservation, CancellationToken cancellationToken = default);
}