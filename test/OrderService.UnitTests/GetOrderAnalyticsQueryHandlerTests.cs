using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using OrderService.Application.Orders.Abstractions;
using OrderService.Application.Orders.Queries.GetOrderAnalytics;
using OrderService.Domain.Orders;
using Xunit;

namespace OrderService.UnitTests;

public class GetOrderAnalyticsQueryHandlerTests
{
    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly GetOrderAnalyticsQueryHandler _handler;

    public GetOrderAnalyticsQueryHandlerTests()
    {
        _handler = new GetOrderAnalyticsQueryHandler(_orderRepository);
    }

    [Fact]
    public async Task HandleAsync_WhenNoOrdersFound_ReturnsZeroMetrics()
    {
        // Arrange
        var from = DateTime.UtcNow.AddDays(-5);
        var to = DateTime.UtcNow;
        _orderRepository.ListDetailedAsync(from, to, null, null, null, Arg.Any<CancellationToken>())
            .Returns(new List<Order>());

        var query = new GetOrderAnalyticsQuery(from, to, null, null, null);

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(0, result.TotalOrdersFound);
        Assert.Equal(0.0, result.ReservationDensity);
        Assert.Equal(0.0, result.SuccessRatio);
        Assert.Equal(0.0, result.FailureRatio);
        Assert.Equal(0.0, result.AverageFulfillmentDurationSeconds);
    }

    [Fact]
    public async Task HandleAsync_CalculatesRatiosAndDensity_Correctly()
    {
        // Arrange
        var from = DateTime.UtcNow.AddDays(-5);
        var to = DateTime.UtcNow;

        // Order 1: Confirmed. Duration = 10s. Item: quantity 10/10.
        var baseTime = DateTime.UtcNow;
        var item1 = new OrderLineItem("SKU-1", "WH-1", 10);
        item1.SetReservedQuantity(10);
        var order1 = new Order("ORD-1", [item1]);
        SetCreatedAt(order1, baseTime.AddSeconds(-20));
        SetUpdatedAt(order1, baseTime.AddSeconds(-10));
        order1.Confirm();
        // Since Confirm resets UpdatedAt to UtcNow, we override it after Confirming
        SetCreatedAt(order1, baseTime.AddSeconds(-20));
        SetUpdatedAt(order1, baseTime.AddSeconds(-10));

        // Order 2: Cancelled. Item: quantity 5, reserved 0.
        var item2 = new OrderLineItem("SKU-1", "WH-1", 5);
        var order2 = new Order("ORD-2", [item2]);
        order2.Cancel();

        // Order 3: Expired. Item: quantity 5, reserved 1.
        var item3 = new OrderLineItem("SKU-2", "WH-1", 5);
        item3.SetReservedQuantity(1);
        var order3 = new Order("ORD-3", [item3]);
        order3.Expire();

        // Order 4: Pending. Item: quantity 5, reserved 0.
        var item4 = new OrderLineItem("SKU-1", "WH-2", 5);
        var order4 = new Order("ORD-4", [item4]);

        var orders = new List<Order> { order1, order2, order3, order4 };
        _orderRepository.ListDetailedAsync(from, to, null, null, null, Arg.Any<CancellationToken>())
            .Returns(orders);

        var query = new GetOrderAnalyticsQuery(from, to, null, null, null);

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(4, result.TotalOrdersFound);
        // Confirmed = 1 -> 1/4 = 0.25
        Assert.Equal(0.25, result.SuccessRatio);
        // Cancelled + Expired = 2 -> 2/4 = 0.50
        Assert.Equal(0.50, result.FailureRatio);
        // Duration: order1 only confirmed, duration = 10s
        Assert.Equal(10.0, result.AverageFulfillmentDurationSeconds);
        // Density with no filters: sum(Reserved) / sum(Requested)
        // item1: 10/10, item2: 0/5, item3: 1/5, item4: 0/5
        // total reserved: 10 + 0 + 1 + 0 = 11
        // total requested: 10 + 5 + 5 + 5 = 25
        // 11/25 = 0.44
        Assert.Equal(0.44, result.ReservationDensity);
    }

    [Fact]
    public async Task HandleAsync_FiltersDensityBySkuAndWarehouse()
    {
        // Arrange
        var from = DateTime.UtcNow.AddDays(-5);
        var to = DateTime.UtcNow;

        // Order 1:
        // item1: SKU-1, WH-1, requested 10, reserved 8
        // item2: SKU-2, WH-1, requested 10, reserved 2
        var item1 = new OrderLineItem("SKU-1", "WH-1", 10);
        item1.SetReservedQuantity(8);
        var item2 = new OrderLineItem("SKU-2", "WH-1", 10);
        item2.SetReservedQuantity(2);
        var order1 = new Order("ORD-1", [item1, item2]);

        // Order 2:
        // item3: SKU-1, WH-2, requested 10, reserved 5
        var item3 = new OrderLineItem("SKU-1", "WH-2", 10);
        item3.SetReservedQuantity(5);
        var order2 = new Order("ORD-2", [item3]);

        var orders = new List<Order> { order1, order2 };

        // Test filter by SKU-1
        _orderRepository.ListDetailedAsync(from, to, "SKU-1", null, null, Arg.Any<CancellationToken>())
            .Returns(orders);

        var query = new GetOrderAnalyticsQuery(from, to, "SKU-1", null, null);

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        // For SKU-1: item1 (8/10) and item3 (5/10)
        // total reserved = 13, total requested = 20 -> 13/20 = 0.65
        Assert.Equal(0.65, result.ReservationDensity);

        // Test filter by WH-1
        _orderRepository.ListDetailedAsync(from, to, null, "WH-1", null, Arg.Any<CancellationToken>())
            .Returns(orders);

        var queryWh = new GetOrderAnalyticsQuery(from, to, null, "WH-1", null);

        // Act
        var resultWh = await _handler.HandleAsync(queryWh, CancellationToken.None);

        // Assert
        // For WH-1: item1 (8/10) and item2 (2/10)
        // total reserved = 10, total requested = 20 -> 10/20 = 0.50
        Assert.Equal(0.50, resultWh.ReservationDensity);

        // Test filter by SKU-1 and WH-1
        _orderRepository.ListDetailedAsync(from, to, "SKU-1", "WH-1", null, Arg.Any<CancellationToken>())
            .Returns(orders);

        var queryBoth = new GetOrderAnalyticsQuery(from, to, "SKU-1", "WH-1", null);

        // Act
        var resultBoth = await _handler.HandleAsync(queryBoth, CancellationToken.None);

        // Assert
        // For SKU-1 and WH-1: item1 (8/10) only
        // total reserved = 8, total requested = 10 -> 8/10 = 0.80
        Assert.Equal(0.80, resultBoth.ReservationDensity);
    }

    [Fact]
    public async Task HandleAsync_WhenNoRequestedQuantityMatchesFilter_ReturnsZeroDensity()
    {
        // Arrange
        var from = DateTime.UtcNow.AddDays(-5);
        var to = DateTime.UtcNow;

        var item = new OrderLineItem("SKU-2", "WH-2", 10);
        var order = new Order("ORD-1", [item]);
        var orders = new List<Order> { order };

        _orderRepository.ListDetailedAsync(from, to, "SKU-1", null, null, Arg.Any<CancellationToken>())
            .Returns(orders);

        var query = new GetOrderAnalyticsQuery(from, to, "SKU-1", null, null);

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        Assert.Equal(0.0, result.ReservationDensity);
    }

    private static void SetCreatedAt(Order order, DateTime value)
    {
        var property = typeof(Order).GetProperty(nameof(Order.CreatedAt));
        property?.SetValue(order, value);
    }

    private static void SetUpdatedAt(Order order, DateTime value)
    {
        var property = typeof(Order).GetProperty(nameof(Order.UpdatedAt));
        property?.SetValue(order, value);
    }
}
