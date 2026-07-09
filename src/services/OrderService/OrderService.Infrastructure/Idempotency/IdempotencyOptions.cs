namespace OrderService.Infrastructure.Idempotency;

public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    // Tamamlanmış bir create-order isteğinin sonucunu Redis'te ne kadar saklayacağımızı belirler.
    // Bu süre dolmadan aynı Idempotency-Key ve aynı istek tekrar gelirse yeni order oluşturulmaz;
    // Redis'te saklanan önceki HTTP cevabı doğrudan geri döndürülür.
    public TimeSpan CompletedTtl { get; init; } = TimeSpan.FromHours(24);

    // Create-order isteği çalışmaya başladığında Redis'e geçici bir "Processing" kaydı yazılır.
    // Bu süre, uygulama işlem ortasında çökerse geçici kaydın sonsuza kadar kalmasını engeller.
    // Order'ın domain durumunu değil, idempotency tarafındaki geçici işlem kilidini ifade eder.
    public TimeSpan ProcessingTtl { get; init; } = TimeSpan.FromMinutes(2);

    // Aynı Idempotency-Key ile ikinci istek geldiğinde ilk istek hâlâ çalışıyorsa,
    // ikinci isteğin tamamlanmış sonucu en fazla ne kadar bekleyeceğini belirler.
    // İlk istek bu sürede tamamlanırsa saklanan önceki HTTP cevabı geri döndürülür.
    public TimeSpan ReplayWaitTimeout { get; init; } = TimeSpan.FromSeconds(3);

    // İkinci istek beklerken Redis'i ne sıklıkla kontrol edeceğini belirler.
    // Örneğin 100 ms ise ReplayWaitTimeout dolana kadar yaklaşık her 100 ms'de bir
    // ilk isteğin "Completed" durumuna geçip geçmediği kontrol edilir.
    public TimeSpan ReplayPollInterval { get; init; } = TimeSpan.FromMilliseconds(100);

}
