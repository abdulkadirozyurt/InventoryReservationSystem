using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Application.Reservations.Results.Reconciliation;
using InventoryService.Domain.DeadLetterQueue;
using InventoryService.Infrastructure.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace InventoryService.UnitTests;

public sealed class InventoryReconciliationBackgroundServiceTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceScope _scope;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReservationRepository _reservationRepository;
    private readonly IInventoryItemRepository _inventoryItemRepository;
    private readonly IDeadLetterQueueRepository _dlqRepository;
    private readonly ILogger<InventoryReconciliationBackgroundService> _logger;
    private readonly IOptions<ReconciliationWorkerOptions> _options;
    private readonly InventoryReconciliationBackgroundService _worker;

    public InventoryReconciliationBackgroundServiceTests()
    {
        _serviceProvider = Substitute.For<IServiceProvider>();
        _scope = Substitute.For<IServiceScope>();
        _scopeFactory = Substitute.For<IServiceScopeFactory>();
        _reservationRepository = Substitute.For<IReservationRepository>();
        _inventoryItemRepository = Substitute.For<IInventoryItemRepository>();
        _dlqRepository = Substitute.For<IDeadLetterQueueRepository>();
        _logger = Substitute.For<ILogger<InventoryReconciliationBackgroundService>>();

        var config = new ReconciliationWorkerOptions { IntervalSeconds = 1 };
        _options = Options.Create(config);

        _serviceProvider.GetService(typeof(IServiceScopeFactory)).Returns(_scopeFactory);
        _scopeFactory.CreateScope().Returns(_scope);
        _scope.ServiceProvider.Returns(_serviceProvider);

        _serviceProvider.GetService(typeof(IReservationRepository)).Returns(_reservationRepository);
        _serviceProvider.GetService(typeof(IInventoryItemRepository)).Returns(_inventoryItemRepository);
        _serviceProvider.GetService(typeof(IDeadLetterQueueRepository)).Returns(_dlqRepository);

        _worker = new InventoryReconciliationBackgroundService(_serviceProvider, _options, _logger);
    }

    [Fact]
    public async Task ProcessReconciliationAsync_WhenMatchingQuantities_CompletesSuccessfullyWithoutDLQOrWarnings()
    {
        // Arrange
        var expected = new List<ExpectedReservedQuantitySnapshot>
        {
            new("SKU-1", "WH-1", 10, new[] { "res-1" }, new[] { "ord-1" }),
            new("SKU-2", "WH-1", 5, new[] { "res-2" }, new[] { "ord-2" })
        };

        var actual = new Dictionary<(string Sku, string WarehouseId), int>
        {
            { ("SKU-1", "WH-1"), 10 },
            { ("SKU-2", "WH-1"), 5 }
        };

        _reservationRepository.GetExpectedReservedQuantityBySkuWarehouseAsync(Arg.Any<CancellationToken>())
            .Returns(expected);
        _inventoryItemRepository.GetReservedQuantitySnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(actual);

        // Act
        await _worker.ProcessReconciliationAsync(CancellationToken.None);

        // Assert
        await _dlqRepository.DidNotReceiveWithAnyArgs().UpsertFailureAsync(default!, default);

        _logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Inventory reconciliation completed successfully")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task ProcessReconciliationAsync_WhenMismatchedQuantities_LogsWarningWithCorrectMismatchTypesAndIds()
    {
        // Arrange
        var expected = new List<ExpectedReservedQuantitySnapshot>
        {
            // SKU-1: perfect match
            new("SKU-1", "WH-1", 10, new[] { "res-1" }, new[] { "ord-1" }),
            // SKU-2: value mismatch -> ReservedQuantityMismatch
            new("SKU-2", "WH-1", 5, new[] { "res-2" }, new[] { "ord-2" }),
            // SKU-4: only expected -> ExpectedReservedMissingInInventory
            new("SKU-4", "WH-1", 4, new[] { "res-4" }, new[] { "ord-4" })
        };

        var actual = new Dictionary<(string Sku, string WarehouseId), int>
        {
            { ("SKU-1", "WH-1"), 10 },
            { ("SKU-2", "WH-1"), 6 },
            { ("SKU-3", "WH-2"), 2 } // SKU-3: only actual -> InventoryReservedWithoutPendingReservations
        };

        _reservationRepository.GetExpectedReservedQuantityBySkuWarehouseAsync(Arg.Any<CancellationToken>())
            .Returns(expected);
        _inventoryItemRepository.GetReservedQuantitySnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(actual);

        // Act
        await _worker.ProcessReconciliationAsync(CancellationToken.None);

        // Assert
        await _dlqRepository.DidNotReceiveWithAnyArgs().UpsertFailureAsync(default!, default);

        // Log warnings verification - check that all three mismatch types are logged
        _logger.Received(3).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Inventory reconciliation mismatch found")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());

        // Check specifically for mismatch types/reservation IDs in logs
        _logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("ReservedQuantityMismatch") && o.ToString()!.Contains("res-2") && o.ToString()!.Contains("ord-2")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());

        _logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("ExpectedReservedMissingInInventory") && o.ToString()!.Contains("res-4") && o.ToString()!.Contains("ord-4")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());

        _logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("InventoryReservedWithoutPendingReservations")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());

        _logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("3 mismatches detected")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task ProcessReconciliationAsync_WhenRepositoryThrows_UpsertsToDLQAndLogsError()
    {
        // Arrange
        var exception = new Exception("Database failure");
        _reservationRepository.GetExpectedReservedQuantityBySkuWarehouseAsync(Arg.Any<CancellationToken>())
            .Throws(exception);

        // Act
        await _worker.ProcessReconciliationAsync(CancellationToken.None);

        // Assert
        await _dlqRepository.Received(1).UpsertFailureAsync(
            Arg.Is<DeadLetterRecord>(r =>
                r.OperationType == "Reconciliation" &&
                r.ErrorCategory == "Exception" &&
                r.Reason.Contains("Database failure")),
            Arg.Any<CancellationToken>());

        _logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Exception during InventoryReconciliation execution")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task ProcessReconciliationAsync_WhenDLQUpsertThrows_LogsBothErrorsWithoutCrashing()
    {
        // Arrange
        var repoException = new Exception("Database failure");
        var dlqException = new Exception("DLQ failure");

        _reservationRepository.GetExpectedReservedQuantityBySkuWarehouseAsync(Arg.Any<CancellationToken>())
            .Throws(repoException);

        _dlqRepository.UpsertFailureAsync(Arg.Any<DeadLetterRecord>(), Arg.Any<CancellationToken>())
            .Throws(dlqException);

        // Act
        await _worker.ProcessReconciliationAsync(CancellationToken.None);

        // Assert
        _logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Exception during InventoryReconciliation execution")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());

        _logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Failed to save reconciliation failure to DLQ")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
