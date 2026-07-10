using InventoryService.Application.Inventory.Options;
using InventoryService.Application.Inventory.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace InventoryService.UnitTests;

public sealed class LowStockAlertServiceTests
{
    private readonly ILogger<LowStockAlertService> _logger = Substitute.For<ILogger<LowStockAlertService>>();
    private readonly LowStockAlertService _service;

    public LowStockAlertServiceTests()
    {
        _service = new LowStockAlertService(
            Options.Create(new LowStockThresholdOptions { Threshold = 10 }),
            _logger);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(0)]
    public void Check_WhenQuantityIsAtOrBelowThreshold_ShouldWriteWarning(int quantityAvailable)
    {
        _service.Check("reserve_batch", "corr-1", "SKU-1", "WH-1", quantityAvailable);

        _logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(state => state.ToString()!.Contains("Low stock alert triggered")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public void Check_WhenQuantityIsAboveThreshold_ShouldNotWriteWarning()
    {
        _service.Check("reserve_batch", "corr-1", "SKU-1", "WH-1", 11);

        _logger.DidNotReceiveWithAnyArgs().Log(
            default,
            default,
            default!,
            default,
            default!);
    }
}
