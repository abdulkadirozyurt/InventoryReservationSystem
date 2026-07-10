using InventoryReservationSystem.IntegrationTests.Fixtures;
using InventoryService.Application.Reservations.Commands.ReserveBatch;
using InventoryService.Domain.Inventory;
using InventoryService.Domain.Reservations;
using MongoDB.Driver;
using Xunit;

namespace InventoryReservationSystem.IntegrationTests;

public sealed class ReserveBatchTests : IntegrationTestBase
{
    public ReserveBatchTests(InventoryServiceFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task ReserveBatch_AllSufficientStock_ShouldSucceed()
    {
        var handler = Resolve<ReserveBatchCommandHandler>();
        var orderId = Guid.NewGuid().ToString();

        var command = new ReserveBatchCommand(
            orderId,
            new[] { new ReserveBatchItemCommand("SKU-001", "WH-1", 5) },
            Guid.NewGuid().ToString());

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.ReservationId);

        var reservation = await GetReservationAsync(orderId);
        Assert.NotNull(reservation);
        Assert.Equal(ReservationStatus.Pending, reservation.Status);

        var inventoryItem = await GetInventoryItemAsync("SKU-001", "WH-1");
        Assert.NotNull(inventoryItem);
        // Seed: QuantityReserved=5, after reserving 5, expected 10
        Assert.Equal(10, inventoryItem.QuantityReserved);
        Assert.Equal(95, inventoryItem.QuantityAvailable);
    }

    [Fact]
    public async Task ReserveBatch_OneSkuInsufficient_ShouldRollbackAll()
    {
        var handler = Resolve<ReserveBatchCommandHandler>();
        var orderId = Guid.NewGuid().ToString();

        var inventoryBefore = await GetInventoryItemAsync("SKU-001", "WH-1");
        Assert.NotNull(inventoryBefore);
        var availableBefore = inventoryBefore.QuantityAvailable;
        var reservedBefore = inventoryBefore.QuantityReserved;

        var command = new ReserveBatchCommand(
            orderId,
            new[] { new ReserveBatchItemCommand("SKU-001", "WH-1", 999) },
            Guid.NewGuid().ToString());

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Failures);
        Assert.Contains(result.Failures, f => f.ErrorCode == "INSUFFICIENT_STOCK");

        var reservation = await GetReservationAsync(orderId);
        Assert.Null(reservation);

        var inventoryAfter = await GetInventoryItemAsync("SKU-001", "WH-1");
        Assert.NotNull(inventoryAfter);
        Assert.Equal(availableBefore, inventoryAfter.QuantityAvailable);
        Assert.Equal(reservedBefore, inventoryAfter.QuantityReserved);
    }

    [Fact]
    public async Task ReserveBatch_DuplicateIdempotencyKey_ShouldReturnSameReservation()
    {
        var handler = Resolve<ReserveBatchCommandHandler>();
        var orderId = Guid.NewGuid().ToString();
        var command = new ReserveBatchCommand(
            orderId,
            new[] { new ReserveBatchItemCommand("SKU-001", "WH-1", 1) },
            Guid.NewGuid().ToString());

        string? firstReservationId = null;
        for (int i = 0; i < 5; i++)
        {
            var result = await handler.HandleAsync(command, CancellationToken.None);
            Assert.True(result.Success);
            Assert.NotNull(result.ReservationId);
            if (i == 0)
            {
                firstReservationId = result.ReservationId;
            }
            else
            {
                Assert.Equal(firstReservationId, result.ReservationId);
            }
        }

        var reservationsCollection = Fixture.Database.GetCollection<Reservation>("reservations");
        var filter = Builders<Reservation>.Filter.Eq(r => r.OrderId, orderId);
        var count = await reservationsCollection.CountDocumentsAsync(filter);
        Assert.Equal(1, count);
    }
}
