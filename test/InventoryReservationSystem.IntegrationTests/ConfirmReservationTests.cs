using InventoryReservationSystem.IntegrationTests.Fixtures;
using InventoryService.Application.Reservations.Commands.ConfirmReservation;
using InventoryService.Application.Reservations.Commands.ReserveBatch;
using InventoryService.Domain.Inventory;
using Xunit;

namespace InventoryReservationSystem.IntegrationTests;

public sealed class ConfirmReservationTests : IntegrationTestBase
{
    public ConfirmReservationTests(InventoryServiceFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task ConfirmReservation_ValidReservation_ShouldDecreaseReservedQuantity()
    {
        var reserveHandler = Resolve<ReserveBatchCommandHandler>();
        var confirmHandler = Resolve<ConfirmReservationCommandHandler>();
        var orderId = Guid.NewGuid().ToString();

        var reserveCommand = new ReserveBatchCommand(
            orderId,
            new[] { new ReserveBatchItemCommand("SKU-002", "WH-1", 5) },
            Guid.NewGuid().ToString());

        var reserveResult = await reserveHandler.HandleAsync(reserveCommand, CancellationToken.None);
        Assert.True(reserveResult.Success);

        var inventoryBefore = await GetInventoryItemAsync("SKU-002", "WH-1");
        Assert.NotNull(inventoryBefore);
        var reservedBefore = inventoryBefore.QuantityReserved;
        var availableBefore = inventoryBefore.QuantityAvailable;

        var confirmCommand = new ConfirmReservationCommand(
            reserveResult.ReservationId!,
            Guid.NewGuid().ToString());

        var confirmResult = await confirmHandler.HandleAsync(confirmCommand, CancellationToken.None);
        Assert.True(confirmResult.Success);

        var inventoryAfter = await GetInventoryItemAsync("SKU-002", "WH-1");
        Assert.NotNull(inventoryAfter);

        Assert.Equal(reservedBefore - 5, inventoryAfter.QuantityReserved);
        Assert.Equal(availableBefore, inventoryAfter.QuantityAvailable);
    }
}
