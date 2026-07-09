using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OrderService.Application.Orders.Abstractions;
using StackExchange.Redis;

namespace OrderService.Infrastructure.Idempotency;

// Redis üzerinde Idempotency-Key yaşam döngüsünü yöneten gerçek store'dur.
// İlk gelen create-order isteği key'i "Processing" olarak claim eder.
// İşlem başarıyla bitince aynı key "Completed" durumuna geçirilir ve HTTP response saklanır.
// Aynı key tekrar gelirse yeni order oluşturmak yerine Redis'teki eski response replay edilir.
// Aynı key farklı request body ile kullanılırsa conflict kabul edilir.
public sealed class RedisIdempotencyStore(IDatabase redisDatabase, IdempotencyOptions options, ILogger<RedisIdempotencyStore> logger) : IIdempotencyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web);


    public async Task<IdempotencyClaimResult> TryClaimAsync(string idempotencyKey, string requestHash, CancellationToken cancellationToken = default)
    {
        var redisKey = BuildKey(idempotencyKey);

        // işlenen request için bir  entry oluşturur.
        var processingEntry = new IdempotencyEntry
        {
            State = IdempotencyEntryStates.Processing,
            RequestHash = requestHash,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        try
        {
            // Redis'te key yoksa yeni entry oluşturur ve claim eder.
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

            // Redis'te key varsa mevcut entry'yi alır ve duruma göre claim sonucunu döner.
            var existingValue = await redisDatabase.StringGetAsync(redisKey);
            if (!existingValue.HasValue)
            {
                return new IdempotencyClaimResult(IdempotencyClaimStatus.StoreUnavailable);
            }

            // Redis'teki entry'yi deserialize eder ve duruma göre claim sonucunu döner.
            var existingEntry = JsonSerializer.Deserialize<IdempotencyEntry>((string)existingValue!, JsonOptions);
            if (existingEntry is null)
            {
                return new IdempotencyClaimResult(IdempotencyClaimStatus.StoreUnavailable);
            }

            // Aynı key ile farklı request body gelirse conflict kabul edilir.
            if (!string.Equals(existingEntry.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return new IdempotencyClaimResult(IdempotencyClaimStatus.Conflict);
            }

            
            if (string.Equals(existingEntry.State, IdempotencyEntryStates.Completed, StringComparison.Ordinal))
            {
                return new IdempotencyClaimResult(
                    IdempotencyClaimStatus.Replay,
                    new IdempotencyCompletedResult(
                        existingEntry.StatusCode ?? StatusCodes.Status200OK,
                        existingEntry.ResponseBody ?? string.Empty,
                        existingEntry.ContentType ?? "application/json"));
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

            var existingEntry = JsonSerializer.Deserialize<IdempotencyEntry>((string)existingValue!, JsonOptions);
            if (existingEntry is null ||
                !string.Equals(existingEntry.State, IdempotencyEntryStates.Completed, StringComparison.Ordinal))
            {
                return null;
            }

            return new IdempotencyCompletedResult(
                existingEntry.StatusCode ?? StatusCodes.Status200OK,
                existingEntry.ResponseBody ?? string.Empty,
                existingEntry.ContentType ?? "application/json");
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
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        // Processing -> Completed geçişi atomik olmak zorunda.
        // Script Redis'teki mevcut kaydı okur, kaydın hâlâ Processing durumda olduğunu
        // ve aynı requestHash'e ait olduğunu doğrular. Şartlar sağlanırsa response'u
        // Completed TTL ile yazar; aksi halde 0 döner ve eski worker yeni sahibi ezemez.
        const string completeScript = """
            local value = redis.call('GET', KEYS[1])
            if not value then
                return 0
            end

            local entry = cjson.decode(value)
            if entry.state ~= 'Processing' then
                return 0
            end

            if entry.requestHash ~= ARGV[1] then
                return 0
            end

            redis.call('PSETEX', KEYS[1], ARGV[2], ARGV[3])
            return 1
            """;

        try
        {
            // Complete işlemi de claim release gibi sahiplik kontrolü yapar.
            // Eğer Processing TTL dolup aynı key başka request tarafından sahiplenildiyse eski worker yeni state'i ezemez.
            var result = await redisDatabase.ScriptEvaluateAsync(
                completeScript,
                [BuildKey(idempotencyKey)],
                [requestHash, (long)options.CompletedTtl.TotalMilliseconds, JsonSerializer.Serialize(completedEntry, JsonOptions)]);

            if ((long)result == 0)
            {
                logger.LogWarning(
                    "Idempotency completion skipped because the processing claim no longer belongs to this request. IdempotencyKey: {IdempotencyKey}",
                    idempotencyKey);
            }
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




    public async Task<bool> ReleaseClaimAsync(
        string idempotencyKey,
        string requestHash,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        // Başarısız/iptal edilen işlemde Processing claim'i sadece aynı request'e aitse bırakılır.
        // Script mevcut kaydı okur, state ve requestHash kontrolünü yapar, sonra aynı atomik adımda siler.
        // Böylece TTL dolduktan sonra aynı key'i sahiplenen başka bir request yanlışlıkla silinmez.
        const string releaseScript = """
            local value = redis.call('GET', KEYS[1])
            if not value then
                return 0
            end

            local entry = cjson.decode(value)
            if entry.state ~= 'Processing' then
                return 0
            end

            if entry.requestHash ~= ARGV[1] then
                return 0
            end

            return redis.call('DEL', KEYS[1])
            """;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // GET, state/hash kontrolü ve DEL tek Lua script içinde atomik çalışır.
            // Ayrı ayrı komut kullansaydık kontrol ile silme arasında başka request aynı key'i sahiplenebilirdi.
            var result = await redisDatabase.ScriptEvaluateAsync(
                releaseScript,
                [BuildKey(idempotencyKey)],
                [requestHash]);

            var released = (long)result == 1;

            logger.LogInformation(
                "Idempotency processing claim release finished. IdempotencyKey: {IdempotencyKey}, Released: {Released}, CorrelationId: {CorrelationId}",
                idempotencyKey,
                released,
                correlationId);

            return released;
        }
        catch (RedisTimeoutException exception)
        {
            logger.LogError(
                exception,
                "Idempotency Redis claim release timed out. IdempotencyKey: {IdempotencyKey}, CorrelationId: {CorrelationId}",
                idempotencyKey,
                correlationId);

            return false;
        }
        catch (RedisException exception)
        {
            logger.LogError(
                exception,
                "Idempotency Redis claim release failed. IdempotencyKey: {IdempotencyKey}, CorrelationId: {CorrelationId}",
                idempotencyKey,
                correlationId);

            return false;
        }
    }

    private static RedisKey BuildKey(string idempotencyKey) => $"order-service:idempotency:{idempotencyKey}";
}