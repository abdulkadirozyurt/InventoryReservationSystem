using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Inventory.Options;
using InventoryService.Application.Inventory.Services;
using InventoryService.Application.Observability.Abstractions;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Application.Reservations.Commands.ReserveBatch;
using InventoryService.Domain.Inventory;
using InventoryService.Domain.InventoryTransactions;
using InventoryService.Domain.Reservations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace InventoryService.UnitTests;

public sealed class ReserveBatchCommandHandlerTests
{
    private readonly IInventoryItemRepository _inventoryItemRepository = Substitute.For<IInventoryItemRepository>();
    private readonly IInventoryTransactionRepository _inventoryTransactionRepository = Substitute.For<IInventoryTransactionRepository>();
    private readonly IReservationRepository _reservationRepository = Substitute.For<IReservationRepository>();
    private readonly IInventoryUnitOfWork _unitOfWork = Substitute.For<IInventoryUnitOfWork>();
    private readonly IDistributedLockService _distributedLockService = Substitute.For<IDistributedLockService>();
    private readonly IInventoryServiceMetrics _metrics = Substitute.For<IInventoryServiceMetrics>();
    private readonly ILogger<ReserveBatchCommandHandler> _logger = Substitute.For<ILogger<ReserveBatchCommandHandler>>();
    private readonly ReserveBatchCommandHandler _handler;

    public ReserveBatchCommandHandlerTests()
    {
        _unitOfWork
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<Func<CancellationToken, Task>>(0)(call.ArgAt<CancellationToken>(1)));

        _distributedLockService
            .AcquireAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLockHandle>(new TestDistributedLockHandle([])));

        _handler = new ReserveBatchCommandHandler(
            _inventoryItemRepository,
            _inventoryTransactionRepository,
            _reservationRepository,
            _unitOfWork,
            _distributedLockService,
            _metrics,
            CreateLowStockAlertService(),
            _logger);
    }

    [Fact]
    public async Task HandleAsync_WhenFallbackDisabledAndPrimaryInsufficient_ShouldFailWithoutAlternateWarehouse()
    {
        // Arrange
        var primary = new InventoryItem("SKU-1", "WH-1", 1);
        var alternate = new InventoryItem("SKU-1", "WH-2", 10);
        var command = new ReserveBatchCommand(
            "ORDER-1",
            [new ReserveBatchItemCommand("SKU-1", "WH-1", 2)],
            "CORR-1");

        _inventoryItemRepository
            .GetBySkuAndWarehouseAsync("SKU-1", "WH-1", Arg.Any<CancellationToken>())
            .Returns(primary);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Collection(result.Failures, failure =>
        {
            Assert.Equal("SKU-1", failure.Sku);
            Assert.Equal("WH-1", failure.WarehouseId);
            Assert.Equal("INSUFFICIENT_STOCK", failure.ErrorCode);
        });
        Assert.Equal(1, primary.QuantityAvailable);
        Assert.Equal(10, alternate.QuantityAvailable);
        await _inventoryItemRepository.DidNotReceive().GetBySkuAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _inventoryItemRepository.DidNotReceive().UpdateAsync(Arg.Any<InventoryItem>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenFallbackEnabledAndAlternateHasStock_ShouldReserveAcrossWarehousesWithDeterministicLocks()
    {
        // Arrange
        IReadOnlyCollection<string>? capturedLockKeys = null;
        var primary = new InventoryItem("SKU-1", "WH-1", 2);
        var alternate = new InventoryItem("SKU-1", "WH-2", 3);
        var command = new ReserveBatchCommand(
            "ORDER-2",
            [new ReserveBatchItemCommand("SKU-1", "WH-1", 5)],
            "CORR-2",
            EnableFallback: true);

        _inventoryItemRepository
            .GetBySkuAsync("SKU-1", Arg.Any<CancellationToken>())
            .Returns([primary, alternate]);
        _inventoryItemRepository
            .GetBySkuAndWarehouseAsync("SKU-1", "WH-1", Arg.Any<CancellationToken>())
            .Returns(primary);
        _inventoryItemRepository
            .GetBySkuAndWarehouseAsync("SKU-1", "WH-2", Arg.Any<CancellationToken>())
            .Returns(alternate);
        _distributedLockService
            .AcquireAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturedLockKeys = call.ArgAt<IReadOnlyCollection<string>>(0);
                return Task.FromResult<IDistributedLockHandle>(new TestDistributedLockHandle(capturedLockKeys));
            });

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, primary.QuantityAvailable);
        Assert.Equal(2, primary.QuantityReserved);
        Assert.Equal(0, alternate.QuantityAvailable);
        Assert.Equal(3, alternate.QuantityReserved);
        Assert.Equal(["inventory:SKU-1:WH-1", "inventory:SKU-1:WH-2", "reservation-order:ORDER-2"], capturedLockKeys);
        await _inventoryItemRepository.Received(1).UpdateAsync(primary, Arg.Any<CancellationToken>());
        await _inventoryItemRepository.Received(1).UpdateAsync(alternate, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenFallbackEnabledAndTotalStockInsufficient_ShouldFailWithoutMutation()
    {
        // Arrange
        var primary = new InventoryItem("SKU-1", "WH-1", 1);
        var alternate = new InventoryItem("SKU-1", "WH-2", 1);
        var command = new ReserveBatchCommand(
            "ORDER-3",
            [new ReserveBatchItemCommand("SKU-1", "WH-1", 3)],
            "CORR-3",
            EnableFallback: true);

        _inventoryItemRepository
            .GetBySkuAsync("SKU-1", Arg.Any<CancellationToken>())
            .Returns([primary, alternate]);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Collection(result.Failures, failure => Assert.Equal("INSUFFICIENT_STOCK", failure.ErrorCode));
        Assert.Equal(1, primary.QuantityAvailable);
        Assert.Equal(0, primary.QuantityReserved);
        Assert.Equal(1, alternate.QuantityAvailable);
        Assert.Equal(0, alternate.QuantityReserved);
        await _distributedLockService.DidNotReceive().AcquireAsync(
            Arg.Any<IReadOnlyCollection<string>>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
        await _inventoryItemRepository.DidNotReceive().UpdateAsync(Arg.Any<InventoryItem>(), Arg.Any<CancellationToken>());
        await _reservationRepository.DidNotReceive().AddAsync(Arg.Any<Reservation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenDuplicateLinesUseFallback_ShouldAggregateAndPersistStableAllocation()
    {
        // Arrange
        Reservation? persistedReservation = null;
        var primary = new InventoryItem("SKU-1", "WH-1", 2);
        var alternate = new InventoryItem("SKU-1", "WH-2", 1);
        var command = new ReserveBatchCommand(
            "ORDER-4",
            [
                new ReserveBatchItemCommand("SKU-1", "WH-1", 1),
                new ReserveBatchItemCommand("SKU-1", "WH-1", 2)
            ],
            "CORR-4",
            EnableFallback: true);

        _inventoryItemRepository
            .GetBySkuAsync("SKU-1", Arg.Any<CancellationToken>())
            .Returns([primary, alternate]);
        _inventoryItemRepository
            .GetBySkuAndWarehouseAsync("SKU-1", "WH-1", Arg.Any<CancellationToken>())
            .Returns(primary);
        _inventoryItemRepository
            .GetBySkuAndWarehouseAsync("SKU-1", "WH-2", Arg.Any<CancellationToken>())
            .Returns(alternate);
        _reservationRepository
            .AddAsync(Arg.Do<Reservation>(reservation => persistedReservation = reservation), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(persistedReservation);
        Assert.Collection(
            persistedReservation.Items.OrderBy(item => item.WarehouseId, StringComparer.Ordinal),
            item =>
            {
                Assert.Equal("WH-1", item.WarehouseId);
                Assert.Equal(2, item.Quantity);
            },
            item =>
            {
                Assert.Equal("WH-2", item.WarehouseId);
                Assert.Equal(1, item.Quantity);
            });
    }

    private static LowStockAlertService CreateLowStockAlertService()
    {
        return new LowStockAlertService(
            Options.Create(new LowStockThresholdOptions { Threshold = 10 }),
            Substitute.For<ILogger<LowStockAlertService>>());
    }

    private sealed class TestDistributedLockHandle(IReadOnlyCollection<string> lockKeys) : IDistributedLockHandle
    {
        public IReadOnlyCollection<string> LockKeys { get; } = lockKeys;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
