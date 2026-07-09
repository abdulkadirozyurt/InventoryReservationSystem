namespace OrderService.Application.Orders.Abstractions;

public interface IIdempotencyStore
{
    /// <summary>
    /// Attempts to claim an idempotency key for a request. 
    /// If the key is already claimed, it will return the status of the claim (e.g., Replay, Conflict, Processing, StoreUnavailable). 
    /// If the claim is successful, it will return Claimed status. If the request has already been completed, it will return the completed result.
    /// </summary>
    /// <param name="idempotencyKey"></param>
    /// <param name="requestHash"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>
    /// <see cref="IdempotencyClaimResult"/> indicating the result of the claim attempt. 
    /// </returns>
    Task<IdempotencyClaimResult> TryClaimAsync(string idempotencyKey, string requestHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to retrieve the completed result of a previously completed idempotent request using the idempotency key.
    /// </summary>
    /// <param name="idempotencyKey"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains the completed result if the request has been completed, or null if it has not.
    /// </returns>
    Task<IdempotencyCompletedResult?> TryGetCompleteAsync(string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an idempotent request as completed, storing the HTTP status code, response body, and content type associated with the request.
    /// </summary>
    /// <param name="idempotencyKey"></param>
    /// <param name="requestHash"></param>
    /// <param name="statusCode"></param>
    /// <param name="responseBody"></param>
    /// <param name="contentType"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>
    /// A task that represents the asynchronous operation.
    /// </returns>
    Task CompleteAsync(string idempotencyKey, string requestHash, int statusCode, string responseBody, string contentType, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the result of an attempt to claim an idempotency key for a request.
/// </summary>
/// <param name="Status"></param>
/// <param name="CompletedResult"></param>
public sealed record IdempotencyClaimResult(
    IdempotencyClaimStatus Status,
    IdempotencyCompletedResult? CompletedResult = null);

/// <summary>
/// Represents the status of an idempotency claim attempt.
/// </summary>
public enum IdempotencyClaimStatus
{
    Claimed,
    Replay,
    Conflict,
    Processing,
    StoreUnavailable
}


/// <summary>
/// Represents the result of a completed idempotent request, including the HTTP status code, response body, and content type.
/// </summary>
/// <param name="StatusCode"></param>
/// <param name="ResponseBody"></param>
/// <param name="ContentType"></param>
public sealed record IdempotencyCompletedResult(
    int StatusCode,
    string ResponseBody,
    string ContentType);