using InventoryService.Domain.Inventory;

namespace InventoryService.Application.Inventory.Abstractions;

public interface IInventoryItemRepository
{
    /// <summary>
    /// Gets all inventory items for the specified SKU across warehouses.
    /// </summary>
    /// <param name="sku">The SKU to query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching inventory items, or an empty list when no stock exists for the SKU.</returns>
    Task<IReadOnlyList<InventoryItem>> GetBySkuAsync(string sku, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the inventory item for the specified SKU and warehouse.
    /// </summary>
    /// <param name="sku">The SKU to query.</param>
    /// <param name="warehouseId">The warehouse identifier to query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching inventory item, or null when no stock exists for the SKU and warehouse.</returns>
    Task<InventoryItem?> GetBySkuAndWarehouseAsync(string sku, string warehouseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists changes made to an existing inventory item.
    /// </summary>
    /// <param name="inventoryItem">The inventory item to update.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task UpdateAsync(InventoryItem inventoryItem, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new inventory item.
    /// </summary>
    /// <param name="inventoryItem">The inventory item to add.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task AddAsync(InventoryItem inventoryItem, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a read-only point-in-time view of actual reserved quantities by SKU+warehouse.
    /// Reconciliation uses this snapshot only for reporting drift; it must not mutate stock or acquire write locks.
    /// </summary>
    Task<IReadOnlyDictionary<(string Sku, string WarehouseId), int>> GetReservedQuantitySnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all inventory items across all SKUs and warehouses.
    /// </summary>
    Task<IReadOnlyList<InventoryItem>> GetAllAsync(CancellationToken cancellationToken = default);
}
