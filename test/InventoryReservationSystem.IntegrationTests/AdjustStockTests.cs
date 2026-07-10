using InventoryReservationSystem.IntegrationTests.Fixtures;
using InventoryService.Application.Inventory.Commands.DecreaseStock;
using InventoryService.Application.Inventory.Commands.IncreaseStock;
using InventoryService.Domain.InventoryTransactions;
using Xunit;

namespace InventoryReservationSystem.IntegrationTests;

public sealed class AdjustStockTests : IntegrationTestBase
{
    public AdjustStockTests(InventoryServiceFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task AdjustStock_DecreaseToNegative_ShouldFail()
    {
        var handler = Resolve<DecreaseStockCommandHandler>();

        var result = await handler.HandleAsync(
            new DecreaseStockCommand("SKU-001", "WH-1", 99999, "Test decrease to negative", Guid.NewGuid().ToString()),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("INSUFFICIENT_STOCK", result.ErrorCode);
    }

    [Fact]
    public async Task AdjustStock_ValidIncrease_ShouldCreateTransaction()
    {
        var handler = Resolve<IncreaseStockCommandHandler>();
        var sku = "SKU-003";
        var warehouseId = "WH-1";
        var correlationId = Guid.NewGuid().ToString();

        var transactionCountBefore = await GetTransactionCountAsync(sku, warehouseId);

        var result = await handler.HandleAsync(
            new IncreaseStockCommand(sku, warehouseId, 10, "Test stock increase", correlationId),
            CancellationToken.None);

        Assert.True(result.Success);

        var inventoryItem = await GetInventoryItemAsync(sku, warehouseId);
        Assert.NotNull(inventoryItem);

        var transactionCountAfter = await GetTransactionCountAsync(sku, warehouseId);
        Assert.True(transactionCountAfter > transactionCountBefore);
    }
}
