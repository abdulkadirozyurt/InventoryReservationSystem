using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Inventory.Commands.CreateSnapshot;
using InventoryService.Application.Inventory.Commands.RestoreSnapshot;
using InventoryService.Application.Inventory.Exceptions;
using InventoryService.Application.Observability.Abstractions;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Domain.Inventory;
using InventoryService.Domain.InventoryTransactions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace InventoryService.UnitTests;

public class InventorySnapshotCommandHandlerTests
{
    private readonly IInventoryItemRepository _inventoryItemRepository;
    private readonly IInventorySnapshotRepository _inventorySnapshotRepository;
    private readonly IInventoryTransactionRepository _inventoryTransactionRepository;
    private readonly IInventoryUnitOfWork _inventoryUnitOfWork;
    private readonly IDistributedLockService _distributedLockService;
    private readonly IInventoryServiceMetrics _metrics;

    private readonly CreateInventorySnapshotCommandHandler _createHandler;
    private readonly RestoreInventorySnapshotCommandHandler _restoreHandler;

    public InventorySnapshotCommandHandlerTests()
    {
        _inventoryItemRepository = Substitute.For<IInventoryItemRepository>();
        _inventorySnapshotRepository = Substitute.For<IInventorySnapshotRepository>();
        _inventoryTransactionRepository = Substitute.For<IInventoryTransactionRepository>();
        _inventoryUnitOfWork = Substitute.For<IInventoryUnitOfWork>();
        _distributedLockService = Substitute.For<IDistributedLockService>();
        _metrics = Substitute.For<IInventoryServiceMetrics>();

        var createLogger = Substitute.For<ILogger<CreateInventorySnapshotCommandHandler>>();
        var restoreLogger = Substitute.For<ILogger<RestoreInventorySnapshotCommandHandler>>();

        // Setup IInventoryUnitOfWork to immediately execute the passed transaction action
        _inventoryUnitOfWork
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(async x =>
            {
                var action = x.Arg<Func<CancellationToken, Task>>();
                var token = x.Arg<CancellationToken>();
                await action(token);
            });

        _createHandler = new CreateInventorySnapshotCommandHandler(
            _inventoryItemRepository,
            _inventorySnapshotRepository,
            _metrics,
            createLogger);

        _restoreHandler = new RestoreInventorySnapshotCommandHandler(
            _inventoryItemRepository,
            _inventorySnapshotRepository,
            _inventoryTransactionRepository,
            _distributedLockService,
            _inventoryUnitOfWork,
            _metrics,
            restoreLogger);
    }

    [Fact]
    public async Task CreateSnapshot_ShouldPersistCurrentRows()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        var requestedBy = "admin";
        var command = new CreateInventorySnapshotCommand(requestedBy, correlationId);

        var existingItem1 = new InventoryItem("SKU-A", "WH-01", 100);
        var existingItem2 = new InventoryItem("SKU-B", "WH-02", 50);
        existingItem2.Reserve(10); // Reserves 10, sets available to 40

        _inventoryItemRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<InventoryItem> { existingItem1, existingItem2 });

        // Act
        var result = await _createHandler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.SnapshotId);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);

        await _inventorySnapshotRepository.Received(1).AddAsync(
            Arg.Is<InventorySnapshot>(s =>
                s.SnapshotId == result.SnapshotId &&
                s.RequestedBy == requestedBy &&
                s.CorrelationId == correlationId &&
                s.Items.Count == 2 &&
                s.Items[0].Sku == "SKU-A" &&
                s.Items[0].QuantityAvailable == 100 &&
                s.Items[0].QuantityReserved == 0 &&
                s.Items[1].Sku == "SKU-B" &&
                s.Items[1].QuantityAvailable == 40 &&
                s.Items[1].QuantityReserved == 10
            ),
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task RestoreSnapshot_WhenSnapshotDoesNotExist_ShouldFail()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        var requestedBy = "admin";
        var snapshotId = "invalid-snapshot-id";
        var command = new RestoreInventorySnapshotCommand(snapshotId, requestedBy, correlationId);

        _inventorySnapshotRepository.GetByIdAsync(snapshotId, Arg.Any<CancellationToken>())
            .Returns((InventorySnapshot?)null);

        // Act
        var result = await _restoreHandler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("SNAPSHOT_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task RestoreSnapshot_WhenSnapshotExists_ShouldRestoreQuantitiesAndWriteAuditDeltasAndAcquireDeterministicLocks()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        var requestedBy = "admin";
        var snapshotId = Guid.NewGuid().ToString("N");
        var command = new RestoreInventorySnapshotCommand(snapshotId, requestedBy, correlationId);

        // Snapshot contains SKU-A (Av: 80, Res: 10) and SKU-B (Av: 40, Res: 5)
        var snapshotItems = new List<SnapshotItem>
        {
            new("SKU-A", "WH-01", 80, 10),
            new("SKU-B", "WH-02", 40, 5)
        };
        var snapshot = new InventorySnapshot(snapshotId, DateTime.UtcNow, requestedBy, correlationId, snapshotItems);

        _inventorySnapshotRepository.GetByIdAsync(snapshotId, Arg.Any<CancellationToken>())
            .Returns(snapshot);

        // DB currently contains SKU-A (Av: 75, Res: 8) [delta: +5, +2] and SKU-B (Av: 40, Res: 5) [no change]
        var dbItemA = new InventoryItem("SKU-A", "WH-01", 75);
        dbItemA.RestoreQuantities(75, 8); // Setup initial reserved quantity
        var dbItemB = new InventoryItem("SKU-B", "WH-02", 40);
        dbItemB.RestoreQuantities(40, 5);

        _inventoryItemRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<InventoryItem> { dbItemA, dbItemB });

        // Act
        var result = await _restoreHandler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);

        // Verify deterministic sorting of lock keys: A, B. Order matters.
        await _distributedLockService.Received(1).AcquireAsync(
            Arg.Is<IReadOnlyCollection<string>>(keys =>
                keys.SequenceEqual(new[] { "inventory:SKU-A:WH-01", "inventory:SKU-B:WH-02" })),
            Arg.Any<TimeSpan>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>()
        );

        // Item A should be updated
        await _inventoryItemRepository.Received(1).UpdateAsync(
            Arg.Is<InventoryItem>(x => x.Sku == "SKU-A" && x.QuantityAvailable == 80 && x.QuantityReserved == 10),
            Arg.Any<CancellationToken>()
        );

        // Item B has no changes, update should NOT be called
        await _inventoryItemRepository.DidNotReceive().UpdateAsync(
            Arg.Is<InventoryItem>(x => x.Sku == "SKU-B"),
            Arg.Any<CancellationToken>()
        );

        // Transaction log must have delta for Item A only
        await _inventoryTransactionRepository.Received(1).AddAsync(
            Arg.Is<InventoryTransaction>(tx =>
                tx.Sku == "SKU-A" &&
                tx.WarehouseId == "WH-01" &&
                tx.Type == InventoryTransactionType.SnapshotRestore &&
                tx.QuantityAvailableDelta == 5 &&
                tx.QuantityReservedDelta == 2 &&
                tx.CorrelationId == correlationId &&
                tx.Reason == $"Snapshot restore: {snapshotId} by {requestedBy}"
            ),
            Arg.Any<CancellationToken>()
        );

        // Transaction log must NOT be written for Item B
        await _inventoryTransactionRepository.DidNotReceive().AddAsync(
            Arg.Is<InventoryTransaction>(tx => tx.Sku == "SKU-B"),
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task RestoreSnapshot_WhenSnapshotItemDoesNotExist_ShouldCreateNewAndWriteAuditDeltas()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        var requestedBy = "admin";
        var snapshotId = Guid.NewGuid().ToString("N");
        var command = new RestoreInventorySnapshotCommand(snapshotId, requestedBy, correlationId);

        var snapshotItems = new List<SnapshotItem>
        {
            new("SKU-C", "WH-03", 25, 2)
        };
        var snapshot = new InventorySnapshot(snapshotId, DateTime.UtcNow, requestedBy, correlationId, snapshotItems);

        _inventorySnapshotRepository.GetByIdAsync(snapshotId, Arg.Any<CancellationToken>())
            .Returns(snapshot);

        // Empty DB
        _inventoryItemRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<InventoryItem>());

        // Act
        var result = await _restoreHandler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);

        // Item C should be added as new
        await _inventoryItemRepository.Received(1).AddAsync(
            Arg.Is<InventoryItem>(x => x.Sku == "SKU-C" && x.WarehouseId == "WH-03" && x.QuantityAvailable == 25 && x.QuantityReserved == 2),
            Arg.Any<CancellationToken>()
        );

        // Transaction log must contain initial counts
        await _inventoryTransactionRepository.Received(1).AddAsync(
            Arg.Is<InventoryTransaction>(tx =>
                tx.Sku == "SKU-C" &&
                tx.WarehouseId == "WH-03" &&
                tx.Type == InventoryTransactionType.SnapshotRestore &&
                tx.QuantityAvailableDelta == 25 &&
                tx.QuantityReservedDelta == 2 &&
                tx.CorrelationId == correlationId
            ),
            Arg.Any<CancellationToken>()
        );
    }
}
