namespace OrderService.Application.Orders.Queries.GetOrderAnalytics;

public sealed record GetOrderAnalyticsQueryResult(
    double ReservationDensity,
    double SuccessRatio,
    double FailureRatio,
    double AverageFulfillmentDurationSeconds,
    int TotalOrdersFound);
