namespace OrderService.Application.Orders.Results;

public sealed record OrderOperationResult(
    string OrderNumber,
    bool Success,
    string? ErrorCode = null,
    string? ErrorMessage = null);
