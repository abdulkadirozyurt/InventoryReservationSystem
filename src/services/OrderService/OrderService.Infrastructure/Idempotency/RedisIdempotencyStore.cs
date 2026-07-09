using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OrderService.Application.Orders.Abstractions;
using StackExchange.Redis;

namespace OrderService.Infrastructure.Idempotency;

public sealed class RedisIdempotencyStore(IDatabase redisDatabase, IdempotencyOptions options, ILogger<RedisIdempotencyStore> logger) : IIdempotencyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web);


    public async Task<IdempotencyClaimResult> TryClaimAsync(string idempotencyKey, string requestHash, CancellationToken cancellationToken = default)
    {
        var redisKey = BuildKey(idempotencyKey);

        var processingEntry = new IdempotencyEntry
        {
            State = IdempotencyEntryStates.Processing,
            RequestHash = requestHash,
            CreatedAtUTC = DateTimeOffset.UtcNow
        };

        try
        {
            var created = await redisDatabase.StringSetAsync(
                redisKey,
                JsonSerializer.Serialize(processingEntry, JsonOptions),
                options.ProcessingTtl,
                When.NotExists
            );

            if (created)
            {
                return new IdempotencyClaimResult(IdempotencyClaimStatus.Claimed);
            }

            var existingValue = await redisDatabase.StringGetAsync(redisKey);
            if (!existingValue.HasValue)
            {
                return new IdempotencyClaimResult(IdempotencyClaimStatus.StoreUnavailable);
            }

            var existing = JsonSerializer.Deserialize<IdempotencyEntry>((string)existingValue!, JsonOptions);
            if (existing is null)
            {
                return new IdempotencyClaimResult(IdempotencyClaimStatus.StoreUnavailable);
            }

            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return new IdempotencyClaimResult(IdempotencyClaimStatus.Conflict);
            }

            if (string.Equals(existing.State, IdempotencyEntryStates.Completed, StringComparison.Ordinal))
            {
                return new IdempotencyClaimResult(
                    IdempotencyClaimStatus.Replay,
                    new IdempotencyCompletedResult(
                        existing.StatusCode ?? StatusCodes.Status200OK,
                        existing.ResponseBody ?? string.Empty,
                        existing.ContentType ?? "application/json"));
            }

            return new IdempotencyClaimResult(IdempotencyClaimStatus.Processing);

        }
        catch (RedisTimeoutException ex)
        {
            logger.LogError(
                ex,
                "Idempotency Redis timeout for key {IdempotencyKey}",
                redisKey);

            return new IdempotencyClaimResult(IdempotencyClaimStatus.StoreUnavailable);
        }
        catch (RedisException ex)
        {
            logger.LogError(
                ex,
                "Idempotency Redis operation failed for idempotencyKey {IdempotencyKey}",
                redisKey);

            return new IdempotencyClaimResult(IdempotencyClaimStatus.StoreUnavailable);
        }
    }

    public async Task<IdempotencyCompletedResult?> TryGetCompleteAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var existingValue = await redisDatabase.StringGetAsync(BuildKey(idempotencyKey));
            if (!existingValue.HasValue)
            {
                return null;
            }

            var existing = JsonSerializer.Deserialize<IdempotencyEntry>((string)existingValue!, JsonOptions);
            if (existing is null ||
                !string.Equals(existing.State, IdempotencyEntryStates.Completed, StringComparison.Ordinal))
            {
                return null;
            }

            return new IdempotencyCompletedResult(
                existing.StatusCode ?? StatusCodes.Status200OK,
                existing.ResponseBody ?? string.Empty,
                existing.ContentType ?? "application/json");
        }
        catch (RedisTimeoutException ex)
        {
            logger.LogError(
                ex,
                "Idempotency Redis read timeout for key {IdempotencyKey}",
                idempotencyKey);

            return null;
        }
        catch (RedisException ex)
        {
            logger.LogError(
                ex,
                "Idempotency Redis read failed for key {IdempotencyKey}",
                idempotencyKey);

            return null;
        }
    }

    public async Task CompleteAsync(
        string idempotencyKey,
        string requestHash,
        int statusCode,
        string responseBody,
        string contentType,
        CancellationToken cancellationToken)
    {
        var completedEntry = new IdempotencyEntry
        {
            State = IdempotencyEntryStates.Completed,
            RequestHash = requestHash,
            StatusCode = statusCode,
            ResponseBody = responseBody,
            ContentType = contentType,
            CreatedAtUTC = DateTimeOffset.UtcNow
        };



        try
        {
            await redisDatabase.StringSetAsync(
                BuildKey(idempotencyKey),
                JsonSerializer.Serialize(completedEntry, JsonOptions),
                options.CompletedTtl,
                When.Always);
        }
        catch (RedisTimeoutException ex)
        {
            logger.LogError(
                ex,
                "Idempotency Redis write timeout for key {IdempotencyKey}",
                idempotencyKey);
        }
        catch (RedisException ex)
        {
            logger.LogError(
                ex,
                "Idempotency Redis write failed for key {IdempotencyKey}",
                idempotencyKey);
        }
    }




    private static RedisKey BuildKey(string idempotencyKey) => $"order-service:idempotency:{idempotencyKey}";
}