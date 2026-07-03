namespace InventoryService.Application.Reservations.Abstractions;

public interface IDistributedLockHandle : IAsyncDisposable
{
    IReadOnlyCollection<string> LockKeys { get; }
}