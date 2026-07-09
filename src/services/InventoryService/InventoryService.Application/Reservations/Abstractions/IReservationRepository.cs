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


    /// <summary>
    /// Gets a reservation by its public reservation identifier.
    /// </summary>
    /// <param name="reservationId">The reservation identifier to query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching reservation, or null when no reservation exists.</returns>
    Task<Reservation?> GetByReservationIdAsync(string reservationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a reservation by the order identifier that created it.
    /// </summary>
    /// <param name="orderId">The order identifier to query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching reservation, or null when no reservation exists.</returns>
    Task<Reservation?> GetByOrderIdAsync(string orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists changes made to an existing reservation.
    /// </summary>
    /// <param name="reservation">The reservation to update.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task UpdateAsync(Reservation reservation, CancellationToken cancellationToken = default);
}