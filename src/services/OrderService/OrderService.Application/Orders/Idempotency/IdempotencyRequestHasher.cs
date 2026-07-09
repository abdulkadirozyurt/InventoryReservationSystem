using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OrderService.Application.Orders.Idempotency;

public static class IdempotencyRequestHasher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web)
    {
        WriteIndented = false,
    };

    public static string ComputeHash<T>(T request)
    {
        var json = JsonSerializer.Serialize(request, JsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }
}