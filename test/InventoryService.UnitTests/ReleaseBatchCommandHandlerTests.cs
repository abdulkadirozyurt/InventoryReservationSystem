using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Inventory.Exceptions;
using InventoryService.Application.Observability.Abstractions;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Application.Reservations.Commands.ReleaseBatch;
using InventoryService.Application.Reservations.Results.Release;
using InventoryService.Domain.DeadLetterQueue;
using InventoryService.Domain.Reservations;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace InventoryService.UnitTests;

public class ReleaseBatchCommandHandlerTests
{
    private readonly IInventoryItemRepository _inventoryItemRepository;
    private readonly IInventoryTransactionRepository _inventoryTransactionRepository;
    private readonly IReservationRepository _reservationRepository;
    private readonly IInventoryUnitOfWork _inventoryUnitOfWork;
    private readonly IDistributedLockService _distributedLockService;
    private readonly IInventoryServiceMetrics _metrics;
    private readonly ILogger<ReleaseBatchCommandHandler> _logger;
    private readonly IDeadLetterQueueRepository _deadLetterQueueRepository;
    private readonly ReleaseBatchCommandHandler _handler;

    public ReleaseBatchCommandHandlerTests()
    {
        _inventoryItemRepository = Substitute.For<IInventoryItemRepository>();
        _inventoryTransactionRepository = Substitute.For<IInventoryTransactionRepository>();
        _reservationRepository = Substitute.For<IReservationRepository>();
        _inventoryUnitOfWork = Substitute.For<IInventoryUnitOfWork>();
        _distributedLockService = Substitute.For<IDistributedLockService>();
        _metrics = Substitute.For<IInventoryServiceMetrics>();
        _logger = Substitute.For<ILogger<ReleaseBatchCommandHandler>>();
        _deadLetterQueueRepository = Substitute.For<IDeadLetterQueueRepository>();

        // Setup IInventoryUnitOfWork callback to immediately execute the passed transaction body
        _inventoryUnitOfWork
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(async x =>
            {
                var action = x.Arg<Func<CancellationToken, Task>>();
                var token = x.Arg<CancellationToken>();
                await action(token);
            });

        _handler = new ReleaseBatchCommandHandler(
            _inventoryItemRepository,
            _inventoryTransactionRepository,
            _reservationRepository,
            _inventoryUnitOfWork,
            _distributedLockService,
            _metrics,
            _logger,
            _deadLetterQueueRepository);
    }

    [Fact]
    public async Task HandleAsync_WhenReservationDoesNotExist_ShouldReturnFailureWithReservationNotFound()
    {
        // Arrange
        var command = new ReleaseBatchCommand(
            ReservationId: "unknown-res",
            Items: [new ReleaseBatchItemCommand("SKU-1", "WH-1", 5)],
            CorrelationId: "corr-1"
        );

        _reservationRepository
            .GetByReservationIdAsync(command.ReservationId, Arg.Any<CancellationToken>())
            .Returns((Reservation?)null);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("RESERVATION_NOT_FOUND", result.ErrorCode);
        Assert.Equal("Reservation not found.", result.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_WhenReservationAlreadyReleased_ShouldSkipProcessingAndReturnSuccess()
    {
        // Arrange
        var items = new List<ReservationItem> { new ReservationItem("SKU-1", "WH-1", 5) };
        var reservation = new Reservation("res-1", "order-1", items, DateTime.UtcNow.AddMinutes(10));
        reservation.Release(); // Transition to Released

        var command = new ReleaseBatchCommand(
            ReservationId: "res-1",
            Items: [new ReleaseBatchItemCommand("SKU-1", "WH-1", 5)],
            CorrelationId: "corr-1"
        );

        _reservationRepository
            .GetByReservationIdAsync(command.ReservationId, Arg.Any<CancellationToken>())
            .Returns(reservation);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);

        // Verify that no lock is acquired and no database update is done
        await _distributedLockService.DidNotReceiveWithAnyArgs().AcquireAsync(default!, default, default);
        await _inventoryItemRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
        await _reservationRepository.DidNotReceive().UpdateAsync(Arg.Any<Reservation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenReservationAlreadyExpired_ShouldSkipProcessingAndReturnSuccess()
    {
        // Arrange
        var items = new List<ReservationItem> { new ReservationItem("SKU-1", "WH-1", 5) };
        var reservation = new Reservation("res-1", "order-1", items, DateTime.UtcNow.AddMinutes(10));
        reservation.Expire(); // Transition to Expired

        var command = new ReleaseBatchCommand(
            ReservationId: "res-1",
            Items: [new ReleaseBatchItemCommand("SKU-1", "WH-1", 5)],
            CorrelationId: "corr-1"
        );

        _reservationRepository
            .GetByReservationIdAsync(command.ReservationId, Arg.Any<CancellationToken>())
            .Returns(reservation);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);

        // Verify that no lock is acquired and no database update is done
        await _distributedLockService.DidNotReceiveWithAnyArgs().AcquireAsync(default!, default, default);
        await _inventoryItemRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
        await _reservationRepository.DidNotReceive().UpdateAsync(Arg.Any<Reservation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenReservationStatusIsInvalid_ShouldReturnInvalidReservationState()
    {
        // Arrange
        var items = new List<ReservationItem> { new ReservationItem("SKU-1", "WH-1", 5) };
        var reservation = new Reservation("res-1", "order-1", items, DateTime.UtcNow.AddMinutes(10));
        reservation.Confirm(); // Transition to Confirmed (which is invalid state for release execution)

        var command = new ReleaseBatchCommand(
            ReservationId: "res-1",
            Items: [new ReleaseBatchItemCommand("SKU-1", "WH-1", 5)],
            CorrelationId: "corr-1"
        );

        _reservationRepository
            .GetByReservationIdAsync(command.ReservationId, Arg.Any<CancellationToken>())
            .Returns(reservation);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("INVALID_RESERVATION_STATE", result.ErrorCode);
        Assert.Equal("Reservation must be pending to release.", result.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_WhenCancelReleaseFails_ShouldWriteCancelReleaseToDlqExactlyOnce()
    {
        // Arrange
        var command = new ReleaseBatchCommand(
            ReservationId: "unknown-res",
            Items: [new ReleaseBatchItemCommand("SKU-1", "WH-1", 5)],
            CorrelationId: "corr-1",
            IsExpiry: false
        );

        _reservationRepository
            .GetByReservationIdAsync(command.ReservationId, Arg.Any<CancellationToken>())
            .Returns((Reservation?)null);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        await _deadLetterQueueRepository
            .Received(1)
            .UpsertFailureAsync(
                Arg.Is<DeadLetterRecord>(r => r.OperationType == "CancelRelease" && r.ReservationId == "unknown-res" && r.CorrelationId == "corr-1"),
                Arg.Is<CancellationToken>(ct => !ct.CanBeCanceled)
            );
    }

    [Fact]
    public async Task HandleAsync_WhenExpiryReleaseFails_ShouldNotWriteToDlqFromHandler()
    {
        // Arrange
        var command = new ReleaseBatchCommand(
            ReservationId: "unknown-res",
            Items: [new ReleaseBatchItemCommand("SKU-1", "WH-1", 5)],
            CorrelationId: "corr-1",
            IsExpiry: true
        );

        _reservationRepository
            .GetByReservationIdAsync(command.ReservationId, Arg.Any<CancellationToken>())
            .Returns((Reservation?)null);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        await _deadLetterQueueRepository
            .DidNotReceiveWithAnyArgs()
            .UpsertFailureAsync(default!, default);
    }

    [Fact]
    public async Task HandleAsync_WhenReservationAlreadyReleased_ShouldNotWriteToDlq()
    {
        // Arrange
        var items = new List<ReservationItem> { new ReservationItem("SKU-1", "WH-1", 5) };
        var reservation = new Reservation("res-1", "order-1", items, DateTime.UtcNow.AddMinutes(10));
        reservation.Release(); // already released

        var command = new ReleaseBatchCommand(
            ReservationId: "res-1",
            Items: [new ReleaseBatchItemCommand("SKU-1", "WH-1", 5)],
            CorrelationId: "corr-1",
            IsExpiry: false
        );

        _reservationRepository
            .GetByReservationIdAsync(command.ReservationId, Arg.Any<CancellationToken>())
            .Returns(reservation);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        await _deadLetterQueueRepository
            .DidNotReceiveWithAnyArgs()
            .UpsertFailureAsync(default!, default);
    }
}
