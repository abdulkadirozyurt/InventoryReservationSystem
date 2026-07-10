using InventoryReservationSystem.IntegrationTests.Fixtures;
using InventoryService.Application.Reservations.Commands.ReleaseBatch;
using InventoryService.Application.Reservations.Commands.ReserveBatch;
using InventoryService.Domain.Inventory;
using Xunit;

namespace InventoryReservationSystem.IntegrationTests;

public sealed class ReleaseBatchTests : IntegrationTestBase
{
    public ReleaseBatchTests(InventoryServiceFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task ReleaseBatch_CancelReservation_ShouldReleaseStock()
    {
        var reserveHandler = Resolve<ReserveBatchCommandHandler>();
        var releaseHandler = Resolve<ReleaseBatchCommandHandler>();
        var orderId = Guid.NewGuid().ToString();

        var reserveCommand = new ReserveBatchCommand(
            orderId,
            new[] { new ReserveBatchItemCommand("SKU-001", "WH-1", 3) },
            Guid.NewGuid().ToString());

        var reserveResult = await reserveHandler.HandleAsync(reserveCommand, CancellationToken.None);
        Assert.True(reserveResult.Success);
        Assert.NotNull(reserveResult.ReservationId);

        var inventoryBefore = await GetInventoryItemAsync("SKU-001", "WH-1");
        Assert.NotNull(inventoryBefore);
        var reservedBefore = inventoryBefore.QuantityReserved;
        var availableBefore = inventoryBefore.QuantityAvailable;

        var releaseCommand = new ReleaseBatchCommand(
            reserveResult.ReservationId,
            new[] { new ReleaseBatchItemCommand("SKU-001", "WH-1", 3) },
            Guid.NewGuid().ToString());

        var releaseResult = await releaseHandler.HandleAsync(releaseCommand, CancellationToken.None);
        Assert.True(releaseResult.Success);

        var inventoryAfter = await GetInventoryItemAsync("SKU-001", "WH-1");
        Assert.NotNull(inventoryAfter);

        Assert.Equal(reservedBefore - 3, inventoryAfter.QuantityReserved);
        Assert.Equal(availableBefore + 3, inventoryAfter.QuantityAvailable);
    }
}
