using System;
using System.Threading;
using System.Threading.Tasks;
using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Inventory.Commands.RebalanceWarehouse;
using InventoryService.Application.Inventory.Options;
using InventoryService.Application.Inventory.Services;
using InventoryService.Application.Observability.Abstractions;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Domain.Inventory;
using InventoryService.Domain.InventoryTransactions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace InventoryService.UnitTests;

public class RebalanceWarehouseCommandHandlerTests
{
    private readonly IInventoryItemRepository _inventoryItemRepository;
    private readonly IInventoryTransactionRepository _inventoryTransactionRepository;
    private readonly IInventoryUnitOfWork _inventoryUnitOfWork;
    private readonly IDistributedLockService _distributedLockService;
    private readonly IInventoryServiceMetrics _metrics;
    private readonly ILogger<RebalanceWarehouseCommandHandler> _logger;
    private readonly RebalanceWarehouseCommandHandler _handler;

    public RebalanceWarehouseCommandHandlerTests()
    {
        _inventoryItemRepository = Substitute.For<IInventoryItemRepository>();
        _inventoryTransactionRepository = Substitute.For<IInventoryTransactionRepository>();
        _inventoryUnitOfWork = Substitute.For<IInventoryUnitOfWork>();
        _distributedLockService = Substitute.For<IDistributedLockService>();
        _metrics = Substitute.For<IInventoryServiceMetrics>();
        _logger = Substitute.For<ILogger<RebalanceWarehouseCommandHandler>>();

        // Setup double transaction or single lambda execution
        _inventoryUnitOfWork
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(async x =>
            {
                var action = x.Arg<Func<CancellationToken, Task>>();
                var token = x.Arg<CancellationToken>();
                await action(token);
            });

        _handler = new RebalanceWarehouseCommandHandler(
            _inventoryItemRepository,
            _inventoryTransactionRepository,
            _inventoryUnitOfWork,
            _distributedLockService,
            _metrics,
            CreateLowStockAlertService(),
            _logger);
    }

    private static LowStockAlertService CreateLowStockAlertService()
    {
        return new LowStockAlertService(
            Options.Create(new LowStockThresholdOptions { Threshold = 10 }),
            Substitute.For<ILogger<LowStockAlertService>>());
    }

    [Fact]
    public async Task HandleAsync_WhenValidRebalanceWithExistingTarget_ShouldMoveStockAndSaveAudits()
    {
        // Arrange
        var command = new RebalanceWarehouseCommand(
            Sku: "SKU-123",
            SourceWarehouseId: "WH-A",
            TargetWarehouseId: "WH-B",
            Quantity: 5,
            Reason: "Stock adjustment rebalancing",
            CorrelationId: "correlation-id-123");

        var sourceItem = new InventoryItem("SKU-123", "WH-A", 10);
        var targetItem = new InventoryItem("SKU-123", "WH-B", 5);

        _inventoryItemRepository
            .GetBySkuAndWarehouseAsync("SKU-123", "WH-A", Arg.Any<CancellationToken>())
            .Returns(sourceItem);

        _inventoryItemRepository
            .GetBySkuAndWarehouseAsync("SKU-123", "WH-B", Arg.Any<CancellationToken>())
            .Returns(targetItem);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(5, result.SourceAvailableStock); // 10 - 5
        Assert.Equal(10, result.TargetAvailableStock); // 5 + 5

        await _inventoryItemRepository.Received(2).UpdateAsync(Arg.Any<InventoryItem>(), Arg.Any<CancellationToken>());
        await _inventoryTransactionRepository.Received(2).AddAsync(Arg.Any<InventoryTransaction>(), Arg.Any<CancellationToken>());

        // Verify audit transactions have correct type and delta
        await _inventoryTransactionRepository.Received(1).AddAsync(
            Arg.Is<InventoryTransaction>(t => t.Sku == "SKU-123" && t.WarehouseId == "WH-A" && t.QuantityAvailableDelta == -5 && t.Type == InventoryTransactionType.Rebalance && t.Reason == command.Reason),
            Arg.Any<CancellationToken>());

        await _inventoryTransactionRepository.Received(1).AddAsync(
            Arg.Is<InventoryTransaction>(t => t.Sku == "SKU-123" && t.WarehouseId == "WH-B" && t.QuantityAvailableDelta == 5 && t.Type == InventoryTransactionType.Rebalance && t.Reason == command.Reason),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenValidRebalanceWithNewTarget_ShouldCreateTargetItemAndMoveStock()
    {
        // Arrange
        var command = new RebalanceWarehouseCommand(
            Sku: "SKU-123",
            SourceWarehouseId: "WH-A",
            TargetWarehouseId: "WH-C",
            Quantity: 5,
            Reason: "Stock rebalancing to new location",
            CorrelationId: "correlation-id-123");

        var sourceItem = new InventoryItem("SKU-123", "WH-A", 10);
        InventoryItem? targetItem = null;

        _inventoryItemRepository
            .GetBySkuAndWarehouseAsync("SKU-123", "WH-A", Arg.Any<CancellationToken>())
            .Returns(sourceItem);

        _inventoryItemRepository
            .GetBySkuAndWarehouseAsync("SKU-123", "WH-C", Arg.Any<CancellationToken>())
            .Returns(targetItem);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(5, result.SourceAvailableStock);
        Assert.Equal(5, result.TargetAvailableStock);

        await _inventoryItemRepository.Received(1).UpdateAsync(sourceItem, Arg.Any<CancellationToken>());
        await _inventoryItemRepository.Received(1).AddAsync(Arg.Is<InventoryItem>(i => i.Sku == "SKU-123" && i.WarehouseId == "WH-C" && i.QuantityAvailable == 5), Arg.Any<CancellationToken>());
        await _inventoryTransactionRepository.Received(2).AddAsync(Arg.Any<InventoryTransaction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenSourceStockNotFound_ShouldReturnStockNotFoundAndNotMutate()
    {
        // Arrange
        var command = new RebalanceWarehouseCommand(
            Sku: "SKU-123",
            SourceWarehouseId: "WH-A",
            TargetWarehouseId: "WH-B",
            Quantity: 5,
            Reason: "Rebalance test",
            CorrelationId: "correlation-id-123");

        _inventoryItemRepository
            .GetBySkuAndWarehouseAsync("SKU-123", "WH-A", Arg.Any<CancellationToken>())
            .Returns((InventoryItem?)null);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("STOCK_NOT_FOUND", result.ErrorCode);
        Assert.Equal("Source stock not found.", result.ErrorMessage);

        await _inventoryItemRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
        await _inventoryItemRepository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _inventoryTransactionRepository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task HandleAsync_WhenInsufficientStockAtSource_ShouldReturnInsufficientStockAndNotMutate()
    {
        // Arrange
        var command = new RebalanceWarehouseCommand(
            Sku: "SKU-123",
            SourceWarehouseId: "WH-A",
            TargetWarehouseId: "WH-B",
            Quantity: 15,
            Reason: "Rebalance test",
            CorrelationId: "correlation-id-123");

        var sourceItem = new InventoryItem("SKU-123", "WH-A", 10);

        _inventoryItemRepository
            .GetBySkuAndWarehouseAsync("SKU-123", "WH-A", Arg.Any<CancellationToken>())
            .Returns(sourceItem);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("INSUFFICIENT_STOCK", result.ErrorCode);

        await _inventoryItemRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
        await _inventoryItemRepository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _inventoryTransactionRepository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task HandleAsync_WhenSourceAndTargetWarehousesAreEqual_ShouldReturnValidationError()
    {
        // Arrange
        var command = new RebalanceWarehouseCommand(
            Sku: "SKU-123",
            SourceWarehouseId: "WH-A",
            TargetWarehouseId: "WH-A",
            Quantity: 5,
            Reason: "Rebalance test",
            CorrelationId: "correlation-id-123");

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
        Assert.Contains("different", result.ErrorMessage);

        await _inventoryItemRepository.DidNotReceiveWithAnyArgs().GetBySkuAndWarehouseAsync(default!, default!, default);
    }

    [Fact]
    public async Task HandleAsync_WhenReasonIsMissing_ShouldReturnValidationError()
    {
        // Arrange
        var command = new RebalanceWarehouseCommand(
            Sku: "SKU-123",
            SourceWarehouseId: "WH-A",
            TargetWarehouseId: "WH-B",
            Quantity: 5,
            Reason: "",
            CorrelationId: "correlation-id-123");

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
        Assert.Contains("Reason is required", result.ErrorMessage);

        await _inventoryItemRepository.DidNotReceiveWithAnyArgs().GetBySkuAndWarehouseAsync(default!, default!, default);
    }

    [Fact]
    public async Task HandleAsync_WhenLockAcquisitionTimesOut_ShouldReturnLockTimeout()
    {
        // Arrange
        var command = new RebalanceWarehouseCommand(
            Sku: "SKU-123",
            SourceWarehouseId: "WH-A",
            TargetWarehouseId: "WH-B",
            Quantity: 5,
            Reason: "Rebalance test",
            CorrelationId: "correlation-id-123");

        _distributedLockService
            .AcquireAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IDistributedLockHandle>(new TimeoutException("Lock timed out")));

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("LOCK_TIMEOUT", result.ErrorCode);

        await _inventoryItemRepository.DidNotReceiveWithAnyArgs().GetBySkuAndWarehouseAsync(default!, default!, default);
    }
}
