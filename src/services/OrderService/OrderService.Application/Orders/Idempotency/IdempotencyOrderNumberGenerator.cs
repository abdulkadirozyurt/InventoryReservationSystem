using System.Security.Cryptography;
using System.Text;

namespace OrderService.Application.Orders.Idempotency;

public static class IdempotencyOrderNumberGenerator
{
    public static string Generate(string idempotencyKey, string requestHash)
    {
        // Order number sadece Idempotency-Key'den üretilirse aynı key farklı body ile tekrar kullanıldığında
        // eski order'a yanlışlıkla bağlanabilir. Bu yüzden key ve request hash birlikte kullanılır.
        // Aynı key + aynı body her retry'da aynı order number üretir; farklı body ise farklı order number üretir.
        var source = $"{idempotencyKey.Trim()}:{requestHash}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
