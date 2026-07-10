using InventoryService.Application.Inventory.Abstractions;
using InventoryService.Application.Inventory.Options;
using InventoryService.Application.Inventory.Services;
using InventoryService.Application.Observability.Abstractions;
using InventoryService.Application.Reservations.Abstractions;
using InventoryService.Application.Reservations.Commands.AdminReleaseReservation;
using InventoryService.Application.Reservations.Commands.ReleaseBatch;
using InventoryService.Domain.DeadLetterQueue;
using InventoryService.Domain.Inventory;
using InventoryService.Domain.InventoryTransactions;
using InventoryService.Domain.Reservations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace InventoryService.UnitTests;

public sealed class AdminReleaseReservationCommandHandlerTests
{
    private readonly IReservationRepository _reservationRepository = Substitute.For<IReservationRepository>();
    private readonly IInventoryItemRepository _inventoryItemRepository = Substitute.For<IInventoryItemRepository>();
    private readonly IInventoryTransactionRepository _inventoryTransactionRepository = Substitute.For<IInventoryTransactionRepository>();
    private readonly IInventoryUnitOfWork _unitOfWork = Substitute.For<IInventoryUnitOfWork>();
    private readonly IDistributedLockService _distributedLockService = Substitute.For<IDistributedLockService>();
    private readonly IInventoryServiceMetrics _metrics = Substitute.For<IInventoryServiceMetrics>();
    private readonly IDeadLetterQueueRepository _deadLetterQueueRepository = Substitute.For<IDeadLetterQueueRepository>();
    private readonly AdminReleaseReservationCommandHandler _handler;

    public AdminReleaseReservationCommandHandlerTests()
    {
        _unitOfWork
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<Func<CancellationToken, Task>>(0)(call.ArgAt<CancellationToken>(1)));

        _distributedLockService
            .AcquireAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLockHandle>(new TestDistributedLockHandle()));

        var releaseBatchCommandHandler = new ReleaseBatchCommandHandler(
            _inventoryItemRepository,
            _inventoryTransactionRepository,
            _reservationRepository,
            _unitOfWork,
            _distributedLockService,
            _metrics,
            CreateLowStockAlertService(),
            Substitute.For<ILogger<ReleaseBatchCommandHandler>>(),
            _deadLetterQueueRepository);

        _handler = new AdminReleaseReservationCommandHandler(
            _reservationRepository,
            releaseBatchCommandHandler,
            Substitute.For<ILogger<AdminReleaseReservationCommandHandler>>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_WhenReasonIsInvalid_ShouldReturnValidationError(string? invalidReason)
    {
        var command = new AdminReleaseReservationCommand("res-123", invalidReason!, "AdminUser", "corr-id");

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_WhenRequestedByIsInvalid_ShouldReturnValidationError(string? invalidRequestedBy)
    {
        var command = new AdminReleaseReservationCommand("res-123", "Reason for release", invalidRequestedBy!, "corr-id");

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_WhenReservationIdIsInvalid_ShouldReturnValidationError(string? invalidReservationId)
    {
        var command = new AdminReleaseReservationCommand(invalidReservationId!, "Reason for release", "AdminUser", "corr-id");

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_WhenReservationDoesNotExist_ShouldReturnReservationNotFound()
    {
        var command = new AdminReleaseReservationCommand("res-123", "Reason for release", "AdminUser", "corr-id");
        _reservationRepository.GetByReservationIdAsync(command.ReservationId, Arg.Any<CancellationToken>())
            .Returns((Reservation?)null);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("RESERVATION_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_WhenReservationIsAlreadyReleased_ShouldReturnSuccessImmediately()
    {
        var command = new AdminReleaseReservationCommand("res-123", "Reason for release", "AdminUser", "corr-id");
        var reservation = CreateReservation(command.ReservationId);
        reservation.Release();

        _reservationRepository.GetByReservationIdAsync(command.ReservationId, Arg.Any<CancellationToken>())
            .Returns(reservation);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
        await _inventoryItemRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task HandleAsync_WhenReservationIsAlreadyExpired_ShouldReturnSuccessImmediately()
    {
        var command = new AdminReleaseReservationCommand("res-123", "Reason for release", "AdminUser", "corr-id");
        var reservation = CreateReservation(command.ReservationId);
        reservation.Expire();

        _reservationRepository.GetByReservationIdAsync(command.ReservationId, Arg.Any<CancellationToken>())
            .Returns(reservation);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
        await _inventoryItemRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task HandleAsync_WhenReservationIsConfirmed_ShouldReturnInvalidReservationState()
    {
        var command = new AdminReleaseReservationCommand("res-123", "Reason for release", "AdminUser", "corr-id");
        var reservation = CreateReservation(command.ReservationId);
        reservation.Confirm();

        _reservationRepository.GetByReservationIdAsync(command.ReservationId, Arg.Any<CancellationToken>())
            .Returns(reservation);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("INVALID_RESERVATION_STATE", result.ErrorCode);
        await _inventoryItemRepository.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task HandleAsync_WhenReservationIsPending_ShouldReleaseWithAdminAuditReason()
    {
        var command = new AdminReleaseReservationCommand("res-123", "Force release by admin", "AdminUser", "corr-id");
        var reservation = CreateReservation(command.ReservationId);
        var inventoryItem = new InventoryItem("SKU-1", "WH-1", 5);
        inventoryItem.Reserve(5);

        _reservationRepository.GetByReservationIdAsync(command.ReservationId, Arg.Any<CancellationToken>())
            .Returns(reservation);
        _inventoryItemRepository.GetBySkuAndWarehouseAsync("SKU-1", "WH-1", Arg.Any<CancellationToken>())
            .Returns(inventoryItem);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
        Assert.Equal(ReservationStatus.Released, reservation.Status);
        Assert.Equal(5, inventoryItem.QuantityAvailable);
        Assert.Equal(0, inventoryItem.QuantityReserved);

        await _inventoryTransactionRepository.Received(1).AddAsync(
            Arg.Is<InventoryTransaction>(transaction =>
                transaction.Type == InventoryTransactionType.Release &&
                transaction.Reason == "AdminRelease: Force release by admin; RequestedBy: AdminUser" &&
                transaction.CorrelationId == command.CorrelationId),
            Arg.Any<CancellationToken>());
    }

    private static Reservation CreateReservation(string reservationId)
    {
        return new Reservation(
            reservationId,
            "order-1",
            [new ReservationItem("SKU-1", "WH-1", 5)],
            DateTime.UtcNow.AddMinutes(10));
    }

    private static LowStockAlertService CreateLowStockAlertService()
    {
        return new LowStockAlertService(
            Options.Create(new LowStockThresholdOptions { Threshold = 10 }),
            Substitute.For<ILogger<LowStockAlertService>>());
    }

    private sealed class TestDistributedLockHandle : IDistributedLockHandle
    {
        public IReadOnlyCollection<string> LockKeys { get; } = [];

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
