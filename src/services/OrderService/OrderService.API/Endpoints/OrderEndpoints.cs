using System.Text.Json;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;
using OrderService.Application.Orders.Abstractions;
using OrderService.Application.Orders.Commands.BulkCancelOrders;
using OrderService.Application.Orders.Commands.CancelOrder;
using OrderService.Application.Orders.Commands.ConfirmOrder;
using OrderService.Application.Orders.Commands.CreateOrder;
using OrderService.Application.Orders.Idempotency;
using OrderService.Application.Orders.Queries.GetOrder;
using OrderService.Application.Orders.Queries.ListOrders;
using OrderService.Application.Orders.Results;
using OrderService.Domain.Orders;
using OrderService.Infrastructure.Idempotency;

namespace OrderService.API.Endpoints;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/orders").WithTags("Orders");

        group.MapPost("/", CreateOrderAsync)
            .WithName("CreateOrder");

        group.MapGet("/{orderNumber}", GetOrderAsync)
            .WithName("GetOrder");

        group.MapPost("/{orderNumber}/cancel", CancelOrderAsync)
            .WithName("CancelOrder");

        group.MapPost("/bulk-cancel", BulkCancelOrdersAsync)
            .WithName("BulkCancelOrders");

        group.MapGet("/", ListOrdersAsync)
            .WithName("ListOrders");

        group.MapPost("/{orderNumber}/confirm", ConfirmOrderAsync)
            .WithName("ConfirmOrder");

        return app;
    }

    private static async Task<IResult> CreateOrderAsync(
        CreateOrderRequest request,
        CreateOrderCommandHandler handler,
        IIdempotencyStore idempotencyStore,
        IdempotencyOptions idempotencyOptions,
        ILoggerFactory loggerFactory,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("OrderService.API.Endpoints.CreateOrder");
        var correlationId = GetCorrelationId(context);
        var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Results.BadRequest(new
            {
                ErrorCode = "IdempotencyKeyRequired",
                Message = "Idempotency-Key header is required for create order requests."
            });


        // Aynı Idempotency-Key tekrar geldiğinde aynı istek mi, farklı istek mi anlayabilmek için
        // request body'nin kısa bir parmak izini alıyoruz.
        var requestHash = IdempotencyRequestHasher.ComputeHash(request);

        var claim = await idempotencyStore.TryClaimAsync(
            idempotencyKey,
            requestHash,
            cancellationToken);

        #region IdempotencyKeyClaimCheck

        if (claim.Status == IdempotencyClaimStatus.Replay &&
            claim.CompletedResult is not null)
        {
            // Aynı istek daha önce tamamlanmış. Handler'a, MongoDB'ye veya gRPC'ye gitmeden
            // Redis'te tuttuğumuz eski HTTP cevabını geri dönüyoruz.
            logger.LogInformation(
                "Idempotency replay for key {IdempotencyKey} and correlation id {CorrelationId}",
                idempotencyKey,
                correlationId);

            return Results.Text(
                claim.CompletedResult.ResponseBody,
                claim.CompletedResult.ContentType,
                statusCode: claim.CompletedResult.StatusCode);
        }

        if (claim.Status == IdempotencyClaimStatus.Conflict)
        {
            // Aynı key daha önce kullanılmış ama request body farklı.
            // Bu durumda eski cevabı dönmek yanlış olacağı için isteği reddediyoruz.
            logger.LogWarning(
                "Idempotency key conflict for key {IdempotencyKey} and correlation id {CorrelationId}",
                idempotencyKey,
                correlationId);

            return Results.Conflict(new
            {
                ErrorCode = "IdempotencyKeyConflict",
                Message = "This Idempotency-Key was already used with a different request body."
            });
        }

        if (claim.Status == IdempotencyClaimStatus.Processing)
        {
            // Aynı key ile ilk istek hâlâ çalışıyor olabilir.
            // Hemen hata dönmek yerine kısa süre bekleyip Redis'teki sonucu kontrol ediyoruz.
            var waitStartedAt = DateTimeOffset.UtcNow;

            while (DateTimeOffset.UtcNow - waitStartedAt < idempotencyOptions.ReplayWaitTimeout)
            {
                await Task.Delay(
                    idempotencyOptions.ReplayPollInterval,
                    cancellationToken);

                var completedResult = await idempotencyStore.TryGetCompleteAsync(
                    idempotencyKey,
                    cancellationToken);

                if (completedResult is null)
                    continue;

                logger.LogInformation(
                    "Idempotency replay completed after waiting for key {IdempotencyKey} and correlation id {CorrelationId}",
                    idempotencyKey,
                    correlationId);

                return Results.Text(
                    completedResult.ResponseBody,
                    completedResult.ContentType,
                    statusCode: completedResult.StatusCode);
            }

            // Bekleme süresinde ilk istek tamamlanmadı.
            // İkinci order oluşturmuyoruz; client aynı key ile tekrar deneyebilir.
            logger.LogInformation(
                "Idempotency request is still processing after wait timeout for key {IdempotencyKey} and correlation id {CorrelationId}",
                idempotencyKey,
                correlationId);

            return Results.Conflict(new
            {
                ErrorCode = "IdempotencyKeyProcessing",
                Message = "A request with this Idempotency-Key is still processing."
            });
        }

        if (claim.Status == IdempotencyClaimStatus.StoreUnavailable)
        {
            // Redis çalışmıyorsa idempotency garantisi veremeyiz.
            // Duplicate order riskine girmemek için create-order işlemini başlatmıyoruz.
            logger.LogError(
                "Idempotency store unavailable for key {IdempotencyKey} and correlation id {CorrelationId}",
                idempotencyKey,
                correlationId);

            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Idempotency store is unavailable.");
        }

        // Buraya yalnızca Claimed durumunda geliriz.
        // Yani bu key ilk kez alındı ve create-order işlemini çalıştırabiliriz.
        logger.LogInformation(
            "Idempotency key claimed for key {IdempotencyKey} and correlation id {CorrelationId}",
            idempotencyKey,
            correlationId);

        #endregion

        // Order number aynı Idempotency-Key ve aynı request body hash'inden deterministik üretilir.
        // Böylece aynı retry aynı order'a gider; aynı key farklı body ise farklı order number üretir ve conflict kuralını bozmaz.
        var orderNumber = IdempotencyOrderNumberGenerator.Generate(idempotencyKey, requestHash);

        var command = new CreateOrderCommand(
            orderNumber,
            request.Items.Select(item => new CreateOrderItemCommand(item.Sku, item.WarehouseId, item.Quantity)).ToArray(),
            GetCorrelationId(context));

        CreateOrderResult result;

        try
        {
            result = await handler.HandleAsync(command, cancellationToken);
        }
        catch (Exception exception) when (ShouldReleaseIdempotencyClaim(exception))
        {
            // Bu hata InventoryService'e güvenli şekilde ulaşılamadığını gösterir.
            // Claim'i kör silmiyoruz; Redis yalnız aynı request hash hâlâ Processing durumundaysa siliyor.
            await idempotencyStore.ReleaseClaimAsync(
                idempotencyKey,
                requestHash,
                correlationId,
                cancellationToken);

            throw;
        }

        var responseBody = new CreateOrderResponse(
            result.Success,
            result.OrderNumber,
            result.ReservationId ?? string.Empty,
            result.Failures.Select(failure => new CreateOrderFailureResponse(
                failure.Sku,
                failure.WarehouseId,
                failure.ErrorCode,
                failure.Reason)).ToArray());

        var statusCode = result.Success
            ? StatusCodes.Status201Created
            : StatusCodes.Status409Conflict;

        // İşlem bitti. Aynı key tekrar gelirse MongoDB ve gRPC tekrar çalışmasın diye
        // verdiğimiz HTTP cevabını Redis'e kaydediyoruz.
        var serializedResponse = JsonSerializer.Serialize(
            responseBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await idempotencyStore.CompleteAsync(
            idempotencyKey,
            requestHash,
            statusCode,
            serializedResponse,
            "application/json",
            cancellationToken);

        logger.LogInformation(
            "Idempotency response cached for key {IdempotencyKey}, order {OrderNumber}, correlation id {CorrelationId} and status {StatusCode}",
            idempotencyKey,
            result.OrderNumber,
            correlationId,
            statusCode);

        return Results.Json(responseBody, statusCode: statusCode);
    }

    private static async Task<IResult> GetOrderAsync(string orderNumber, GetOrderQueryHandler handler, CancellationToken cancellationToken)
    {
        var order = await handler.HandleAsync(new GetOrderQuery(orderNumber), cancellationToken);
        return order is null ? Results.NotFound() : Results.Ok(MapOrder(order));
    }

    private static async Task<IResult> CancelOrderAsync(
        string orderNumber,
        CancelOrderRequest? request,
        CancelOrderCommandHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new CancelOrderCommand(orderNumber, GetCorrelationId(context), request?.Reason),
            cancellationToken);

        return MapCancelOperationResult(result);
    }

    private static async Task<IResult> BulkCancelOrdersAsync(
        BulkCancelOrdersRequest request,
        BulkCancelOrdersCommandHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var results = await handler.HandleAsync(
            new BulkCancelOrdersCommand(request.OrderNumbers, GetCorrelationId(context), request.Reason),
            cancellationToken);

        return Results.Ok(new BulkCancelOrdersResponse(results.Select(MapCancelResponse).ToArray()));
    }

    private static async Task<IResult> ListOrdersAsync(
        OrderStatus? status,
        DateTime? from,
        DateTime? to,
        ListOrdersQueryHandler handler,
        CancellationToken cancellationToken)
    {
        var orders = await handler.HandleAsync(new ListOrdersQuery(status, from, to), cancellationToken);
        return Results.Ok(new ListOrdersResponse(orders.Select(MapOrder).ToArray()));
    }

    private static async Task<IResult> ConfirmOrderAsync(
        string orderNumber,
        ConfirmOrderCommandHandler handler,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new ConfirmOrderCommand(orderNumber, GetCorrelationId(context)),
            cancellationToken);

        return MapOperationResult(result);
    }

    private static IResult MapOperationResult(OrderOperationResult result)
    {
        if (result.Success)
            return Results.Ok(new ConfirmOrderResponse(result.OrderNumber, true));

        var response = new ConfirmOrderResponse(result.OrderNumber, false, result.ErrorCode, result.ErrorMessage);

        return result.ErrorCode == "OrderNotFound"
            ? Results.NotFound(response)
            : Results.BadRequest(response);
    }

    private static IResult MapCancelOperationResult(OrderOperationResult result)
    {
        if (result.Success)
            return Results.Ok(MapCancelResponse(result));

        var response = MapCancelResponse(result);

        return result.ErrorCode == "OrderNotFound"
            ? Results.NotFound(response)
            : Results.BadRequest(response);
    }

    private static GetOrderResponse MapOrder(OrderResult order)
    {
        return new GetOrderResponse(
            order.OrderNumber,
            order.Status,
            order.ReservationId,
            order.Items.Select(item => new GetOrderItemResponse(
                item.Sku,
                item.WarehouseId,
                item.RequestedQuantity,
                item.ReservedQuantity)).ToArray(),
            order.CreatedAt,
            order.UpdatedAt);
    }

    private static CancelOrderResponse MapCancelResponse(OrderOperationResult result)
    {
        return new CancelOrderResponse(result.OrderNumber, result.Success, result.ErrorCode, result.ErrorMessage);
    }

    private static bool ShouldReleaseIdempotencyClaim(Exception exception)
    {
        // Circuit açıksa çağrı InventoryService'e gönderilmeden reddedilir; bu yüzden claim'i bırakmak güvenlidir.
        if (exception is BrokenCircuitException)
            return true;

        // Polly timeout sonrası aynı Idempotency-Key aynı OrderNumber ürettiği için retry güvenlidir.
        // Claim bırakılırsa client aynı key ile yeniden deneyebilir, InventoryService aynı OrderId üzerinden replay yapar.
        if (exception is TimeoutRejectedException)
            return true;

        if (exception is RpcException rpcException)
        {
            // Bağlantı kurulamadığında gRPC katmanı bazen Unavailable yerine Cancelled döndürebilir.
            // Üç durum da retry edilebilir dependency failure olduğu için claim güvenli biçimde bırakılır.
            return rpcException.StatusCode is StatusCode.Unavailable
                or StatusCode.Cancelled
                or StatusCode.DeadlineExceeded;
        }

        return false;
    }

    private static string GetCorrelationId(HttpContext context)
    {
        return context.Items[Extensions.CorrelationIdItemName]?.ToString()
            ?? Guid.CreateVersion7().ToString("N");
    }
}

public sealed record CreateOrderRequest(IReadOnlyList<CreateOrderItemRequest> Items);

public sealed record CreateOrderItemRequest(string Sku, string WarehouseId, int Quantity);

public sealed record CreateOrderResponse(
    bool Success,
    string OrderNumber,
    string ReservationId,
    IReadOnlyCollection<CreateOrderFailureResponse> Failures);

public sealed record CreateOrderFailureResponse(string Sku, string WarehouseId, string ErrorCode, string Reason);

public sealed record GetOrderResponse(
    string OrderNumber,
    string Status,
    string? ReservationId,
    IReadOnlyCollection<GetOrderItemResponse> Items,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record GetOrderItemResponse(
    string Sku,
    string WarehouseId,
    int RequestedQuantity,
    int ReservedQuantity);

public sealed record CancelOrderRequest(string? Reason);

public sealed record CancelOrderResponse(
    string OrderNumber,
    bool Success,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record BulkCancelOrdersRequest(IReadOnlyList<string> OrderNumbers, string? Reason);

public sealed record BulkCancelOrdersResponse(IReadOnlyCollection<CancelOrderResponse> Results);

public sealed record ListOrdersResponse(IReadOnlyCollection<GetOrderResponse> Orders);

public sealed record ConfirmOrderResponse(
    string OrderNumber,
    bool Success,
    string? ErrorCode = null,
    string? ErrorMessage = null);
