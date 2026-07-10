using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Inventory.Options;
using InventoryService.Application.Inventory.Services;
using InventoryService.Application.Observability.Abstractions;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Application.Reservations.Commands.ReleaseBatch;
using InventoryService.Domain.Checkpoints;
using InventoryService.Domain.DeadLetterQueue;
using InventoryService.Domain.Inventory;
using InventoryService.Domain.Reservations;
using InventoryService.Infrastructure.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace InventoryService.UnitTests;

public sealed class ReservationExpiryBackgroundServiceTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceScope _scope;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReservationRepository _reservationRepository;
    private readonly ICheckpointRepository _checkpointRepository;
    private readonly IInventoryItemRepository _inventoryItemRepository;
    private readonly IInventoryTransactionRepository _inventoryTransactionRepository;
    private readonly IInventoryUnitOfWork _inventoryUnitOfWork;
    private readonly IDistributedLockService _distributedLockService;
    private readonly IInventoryServiceMetrics _metrics;
    private readonly ReleaseBatchCommandHandler _releaseBatchCommandHandler;
    private readonly IDeadLetterQueueRepository _dlqRepository;
    private readonly ILogger<ReservationExpiryBackgroundService> _logger;
    private readonly IOptions<ExpiryWorkerOptions> _options;
    private readonly ReservationExpiryBackgroundService _worker;

    public ReservationExpiryBackgroundServiceTests()
    {
        _serviceProvider = Substitute.For<IServiceProvider>();
        _scope = Substitute.For<IServiceScope>();
        _scopeFactory = Substitute.For<IServiceScopeFactory>();

        _reservationRepository = Substitute.For<IReservationRepository>();
        _checkpointRepository = Substitute.For<ICheckpointRepository>();
        _inventoryItemRepository = Substitute.For<IInventoryItemRepository>();
        _inventoryTransactionRepository = Substitute.For<IInventoryTransactionRepository>();
        _inventoryUnitOfWork = Substitute.For<IInventoryUnitOfWork>();
        _distributedLockService = Substitute.For<IDistributedLockService>();
        _metrics = Substitute.For<IInventoryServiceMetrics>();
        _dlqRepository = Substitute.For<IDeadLetterQueueRepository>();
        _logger = Substitute.For<ILogger<ReservationExpiryBackgroundService>>();

        _releaseBatchCommandHandler = new ReleaseBatchCommandHandler(
            _inventoryItemRepository,
            _inventoryTransactionRepository,
            _reservationRepository,
            _inventoryUnitOfWork,
            _distributedLockService,
            _metrics,
            CreateLowStockAlertService(),
            Substitute.For<ILogger<ReleaseBatchCommandHandler>>(),
            _dlqRepository
        );

        _options = Options.Create(new ExpiryWorkerOptions
        {
            BatchSize = 10,
            IntervalSeconds = 5,
            MaxRetryCount = 3
        });

        _serviceProvider.GetService(typeof(IServiceScopeFactory)).Returns(_scopeFactory);
        _scopeFactory.CreateScope().Returns(_scope);
        _scope.ServiceProvider.Returns(_serviceProvider);

        _serviceProvider.GetService(typeof(IReservationRepository)).Returns(_reservationRepository);
        _serviceProvider.GetService(typeof(ICheckpointRepository)).Returns(_checkpointRepository);
        _serviceProvider.GetService(typeof(ReleaseBatchCommandHandler)).Returns(_releaseBatchCommandHandler);
        _serviceProvider.GetService(typeof(IDeadLetterQueueRepository)).Returns(_dlqRepository);

        _worker = new ReservationExpiryBackgroundService(_serviceProvider, _options, _logger);

        _inventoryUnitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(async x =>
            {
                var action = x.Arg<Func<CancellationToken, Task>>();
                await action(x.Arg<CancellationToken>());
            });

        var lockHandle = Substitute.For<IDistributedLockHandle>();
        _distributedLockService.AcquireAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(lockHandle);
    }

    private static LowStockAlertService CreateLowStockAlertService()
    {
        return new LowStockAlertService(
            Options.Create(new LowStockThresholdOptions { Threshold = 10 }),
            Substitute.For<ILogger<LowStockAlertService>>());
    }

    [Fact]
    public async Task ProcessExpiredReservationsAsync_WhenNoReservations_DoesNotProcessOrRegisterCheckpoint()
    {
        _checkpointRepository.GetByNameAsync("ReservationExpiry", Arg.Any<CancellationToken>())
            .Returns((Checkpoint?)null);

        _reservationRepository.GetExpiredPendingReservationsAsync(
            Arg.Any<DateTime>(),
            Arg.Any<DateTime?>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>())
            .Returns(new List<Reservation>());

        await _worker.ProcessExpiredReservationsAsync(CancellationToken.None);

        await _inventoryItemRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
        await _checkpointRepository.DidNotReceiveWithAnyArgs().SaveAsync(default!, default);
    }

    [Fact]
    public async Task ProcessExpiredReservationsAsync_WhenSuccessfulRelease_AdvancesCheckpoint()
    {
        var reservation = CreateReservation();

        SetupExpiredReservation(reservation);
        _reservationRepository.GetByReservationIdAsync("res-1", Arg.Any<CancellationToken>())
            .Returns(reservation);

        var inventoryItem = new InventoryItem("SKU-1", "WH-1", 100);
        inventoryItem.Reserve(5);
        _inventoryItemRepository.GetBySkuAndWarehouseAsync("SKU-1", "WH-1", Arg.Any<CancellationToken>())
            .Returns(inventoryItem);

        await _worker.ProcessExpiredReservationsAsync(CancellationToken.None);

        await _inventoryItemRepository.Received(1).UpdateAsync(
            Arg.Is<InventoryItem>(i => i.Sku == "SKU-1" && i.QuantityAvailable == 100 && i.QuantityReserved == 0),
            Arg.Any<CancellationToken>());

        await _reservationRepository.Received(1).UpdateAsync(
            Arg.Is<Reservation>(r => r.ReservationId == "res-1" && r.Status == ReservationStatus.Expired),
            Arg.Any<CancellationToken>());

        await _checkpointRepository.Received(1).SaveAsync(
            Arg.Is<Checkpoint>(c => c.JobName == "ReservationExpiry" && c.LastReservationId == "res-1"),
            Arg.Any<CancellationToken>());

        await _dlqRepository.DidNotReceiveWithAnyArgs().UpsertFailureAsync(default!, default);
    }

    [Fact]
    public async Task ProcessExpiredReservationsAsync_WhenReleaseFailsBeforeMaxRetry_UpsertsDLQAndDoesNotAdvanceCheckpoint()
    {
        var reservation = CreateReservation();

        SetupExpiredReservation(reservation);
        SetupLockTimeout(reservation);

        _dlqRepository.UpsertFailureAsync(Arg.Any<DeadLetterRecord>(), Arg.Any<CancellationToken>())
            .Returns(1);

        await _worker.ProcessExpiredReservationsAsync(CancellationToken.None);

        await _dlqRepository.Received(1).UpsertFailureAsync(
            Arg.Is<DeadLetterRecord>(r => r.ReservationId == "res-1" && r.OperationType == "ExpiryRelease" && r.ErrorCategory == "LOCK_TIMEOUT"),
            Arg.Any<CancellationToken>());

        await _checkpointRepository.DidNotReceiveWithAnyArgs().SaveAsync(default!, default);
    }

    [Fact]
    public async Task ProcessExpiredReservationsAsync_WhenRepeatedReleaseFailuresReachMaxRetry_AdvancesCheckpointOnceQuarantined()
    {
        var reservation = CreateReservation();

        SetupExpiredReservation(reservation);
        SetupLockTimeout(reservation);

        _dlqRepository.UpsertFailureAsync(Arg.Any<DeadLetterRecord>(), Arg.Any<CancellationToken>())
            .Returns(1, 2, 3);

        await _worker.ProcessExpiredReservationsAsync(CancellationToken.None);
        await _worker.ProcessExpiredReservationsAsync(CancellationToken.None);
        await _worker.ProcessExpiredReservationsAsync(CancellationToken.None);

        await _dlqRepository.Received(3).UpsertFailureAsync(
            Arg.Is<DeadLetterRecord>(r => r.ReservationId == "res-1" && r.OrderId == "order-1" && r.OperationType == "ExpiryRelease"),
            Arg.Any<CancellationToken>());

        await _checkpointRepository.Received(1).SaveAsync(
            Arg.Is<Checkpoint>(c => c.JobName == "ReservationExpiry" && c.LastReservationId == "res-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessExpiredReservationsAsync_WhenDLQThrows_DoesNotAdvanceCheckpoint()
    {
        var reservation = CreateReservation();

        SetupExpiredReservation(reservation);
        SetupLockTimeout(reservation);

        _dlqRepository.UpsertFailureAsync(Arg.Any<DeadLetterRecord>(), Arg.Any<CancellationToken>())
            .Throws(new Exception("Database write failure"));

        await Assert.ThrowsAsync<Exception>(() =>
            _worker.ProcessExpiredReservationsAsync(CancellationToken.None));

        await _dlqRepository.Received(1).UpsertFailureAsync(Arg.Any<DeadLetterRecord>(), Arg.Any<CancellationToken>());
        await _checkpointRepository.DidNotReceiveWithAnyArgs().SaveAsync(default!, default);
    }

    private static Reservation CreateReservation()
    {
        var items = new List<ReservationItem> { new ReservationItem("SKU-1", "WH-1", 5) };
        return new Reservation("res-1", "order-1", items, DateTime.UtcNow.AddMinutes(5));
    }

    private void SetupExpiredReservation(Reservation reservation)
    {
        _checkpointRepository.GetByNameAsync("ReservationExpiry", Arg.Any<CancellationToken>())
            .Returns((Checkpoint?)null);

        _reservationRepository.GetExpiredPendingReservationsAsync(
            Arg.Any<DateTime>(),
            Arg.Any<DateTime?>(),
            Arg.Any<string?>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>())
            .Returns(new List<Reservation> { reservation });
    }

    private void SetupLockTimeout(Reservation reservation)
    {
        _reservationRepository.GetByReservationIdAsync(reservation.ReservationId, Arg.Any<CancellationToken>())
            .Returns(reservation);

        _distributedLockService.AcquireAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Throws(new TimeoutException("Lock wait timeout"));
    }
}
