using Grpc.Core;
using Grpc.Core.Interceptors;
using InventoryService.Application.Inventory.Exceptions;

namespace InventoryService.API.Interceptors;

public sealed class GrpcExceptionInterceptor(ILogger<GrpcExceptionInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        // Service bilinçli bir gRPC status ürettiyse aynı status'u koru.
        catch (RpcException)
        {
            throw;
        }
        catch (InventoryStoreUnavailableException exception)
        {
            LogException(exception, context, StatusCode.Unavailable);

            throw CreateRpcException(
                StatusCode.Unavailable,
                "Inventory storage is unavailable.");
        }
        catch (DuplicateReservationException exception)
        {
            // Aynı order için ikinci reservation yazımı Mongo unique index tarafından reddedildi.
            // Bunu internal server error yerine istemcinin anlayabileceği AlreadyExists status'u olarak döndürüyoruz.
            LogException(exception, context, StatusCode.AlreadyExists);

            throw CreateRpcException(
                StatusCode.AlreadyExists,
                "Reservation already exists for this order.");
        }
        catch (TimeoutException exception)
        {
            LogException(exception, context, StatusCode.DeadlineExceeded);

            throw CreateRpcException(
                StatusCode.DeadlineExceeded,
                "Inventory operation timed out.");
        }
        catch (ArgumentException exception)
        {
            LogException(exception, context, StatusCode.InvalidArgument);

            throw CreateRpcException(
                StatusCode.InvalidArgument,
                "Inventory request is invalid.");
        }
        catch (Exception exception)
        {
            LogException(exception, context, StatusCode.Internal);

            throw CreateRpcException(
                StatusCode.Internal,
                "An internal inventory service error occurred.");
        }
    }


    private void LogException(Exception exception, ServerCallContext context, StatusCode statusCode)
    {
        // Correlation ID gRPC metadata üzerinden gelir; servisler arası aynı isteği izlemeyi sağlar.
        var correlationId = context.RequestHeaders.GetValue("x-correlation-id")
            ?? context.RequestHeaders.GetValue("correlation-id")
            ?? string.Empty;

        logger.LogError(
            exception,
            "Unhandled gRPC exception. Method: {Method}, StatusCode: {StatusCode}, CorrelationId: {CorrelationId}, ErrorClass: {ErrorClass}",
            context.Method,
            statusCode,
            correlationId,
            exception.GetType().Name);
    }

    private static RpcException CreateRpcException(StatusCode statusCode, string safeMessage)
    {
        // Client'a stack trace veya internal exception mesajı sızdırma; log zaten server tarafında var.
        return new RpcException(new Status(statusCode, safeMessage));
    }
}