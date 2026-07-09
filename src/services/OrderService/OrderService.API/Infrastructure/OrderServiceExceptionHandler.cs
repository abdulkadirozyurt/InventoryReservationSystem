using Grpc.Core;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace OrderService.API.Infrastructure;

/// <summary>
/// Maps dependency failures to consistent HTTP ProblemDetails responses.
/// </summary>
public sealed class OrderServiceExceptionHandler(
    ILogger<OrderServiceExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>
    /// Writes an HTTP response for exceptions handled by this service.
    /// </summary>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var correlationId = ResolveCorrelationId(httpContext);
        var mapping = MapException(exception);

        // Exception tek yerde maplenir. Böylece her endpoint aynı dependency hatasına farklı HTTP kodu dönmez.
        logger.LogError(
            exception,
            "Order request failed. CorrelationId: {CorrelationId}, StatusCode: {StatusCode}, ErrorCategory: {ErrorCategory}, ExceptionType: {ExceptionType}",
            correlationId,
            mapping.StatusCode,
            mapping.ErrorCategory,
            exception.GetType().Name);

        httpContext.Response.StatusCode = mapping.StatusCode;

        var problemDetails = new ProblemDetails
        {
            Status = mapping.StatusCode,
            Title = mapping.Title,
            Detail = mapping.Detail,
            Instance = httpContext.Request.Path
        };

        // Client bu değerle OrderService ve InventoryService loglarını aynı akış altında arayabilir.
        problemDetails.Extensions["correlationId"] = correlationId;
        problemDetails.Extensions["errorCategory"] = mapping.ErrorCategory;

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }

    private static string ResolveCorrelationId(HttpContext httpContext)
    {
        var correlationId = httpContext.Items["CorrelationId"]?.ToString();

        if (!string.IsNullOrWhiteSpace(correlationId))
            return correlationId;

        correlationId = httpContext.Request.Headers["x-correlation-id"].ToString();
        return string.IsNullOrWhiteSpace(correlationId)
            ? httpContext.TraceIdentifier
            : correlationId;
    }

    private static ExceptionMapping MapException(Exception exception)
    {
        return exception switch
        {
            // Circuit açıkken InventoryService'e yeni çağrı gönderilmez; client daha sonra tekrar denemelidir.
            BrokenCircuitException => new(
                StatusCodes.Status503ServiceUnavailable,
                "Inventory service unavailable",
                "Inventory service is temporarily unavailable. Please retry later.",
                "CircuitOpen"),

            // Polly timeout, OrderService'in yavaş dependency çağrısını kendi belirlediği sürede durdurduğunu gösterir.
            TimeoutRejectedException => new(
                StatusCodes.Status504GatewayTimeout,
                "Inventory service timeout",
                "Inventory service did not respond within the allowed time.",
                "DependencyTimeout"),

            RpcException rpcException => MapRpcException(rpcException),

            _ => new(
                StatusCodes.Status500InternalServerError,
                "Internal server error",
                "An unexpected error occurred.",
                "UnexpectedSystemError")
        };
    }

    private static ExceptionMapping MapRpcException(RpcException exception)
    {
        return exception.StatusCode switch
        {
            StatusCode.Unavailable => new(
                StatusCodes.Status503ServiceUnavailable,
                "Inventory service unavailable",
                "Inventory service is temporarily unavailable. Please retry later.",
                "DependencyUnavailable"),

            // Polly timeout gRPC call token'ını iptal ettiğinde gRPC bazen DeadlineExceeded yerine Cancelled döndürür.
            // Bu durumda client'ın gördüğü gerçek problem yavaş dependency olduğu için 504 dönüyoruz.
            StatusCode.Cancelled or StatusCode.DeadlineExceeded => new(
                StatusCodes.Status504GatewayTimeout,
                "Inventory service timeout",
                "Inventory service did not respond within the allowed time.",
                "DependencyTimeout"),

            StatusCode.InvalidArgument => new(
                StatusCodes.Status400BadRequest,
                "Invalid inventory request",
                "Inventory service rejected the request as invalid.",
                "DependencyValidation"),

            StatusCode.AlreadyExists or StatusCode.FailedPrecondition => new(
                StatusCodes.Status409Conflict,
                "Inventory operation conflict",
                "Inventory operation conflicts with existing state.",
                "DependencyConflict"),

            _ => new(
                StatusCodes.Status502BadGateway,
                "Inventory service error",
                "Inventory service returned an unexpected response.",
                "DependencyFailure")
        };
    }

    private sealed record ExceptionMapping(
        int StatusCode,
        string Title,
        string Detail,
        string ErrorCategory);
}
