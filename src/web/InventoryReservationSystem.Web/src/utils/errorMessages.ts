export function errorCodeToUserMessage(
  code: string,
  status: number,
  fallbackMessage: string
): string {
  const knownCodes: Record<string, string> = {
    Timeout: 'İstek zaman aşımına uğradı. Lütfen tekrar deneyin.',
    NetworkError: 'Sunucuya ulaşılamıyor. Bağlantınızı kontrol edin.',
    Aborted: 'Sunucuya ulaşılamıyor. Bağlantınızı kontrol edin.',
    IdempotencyKeyRequired:
      'İşlem kimliği eksik. Lütfen sayfayı yenileyip tekrar deneyin.',
    InsufficientStock: 'Yeterli stok bulunmuyor.',
    InvalidResponse: 'Sunucudan beklenmeyen yanıt alındı.',
  };

  if (code in knownCodes) return knownCodes[code];

  if (code === 'HttpError') {
    if (status === 503)
      return 'Servis şu anda kullanılamıyor, kısa süre sonra tekrar deneyin.';
    if (status === 504)
      return 'Servis yanıt vermiyor. Kısa süre sonra tekrar deneyin.';
    if (status === 404) return 'Aradığınız kayıt bulunamadı.';
    if (status >= 500 && status <= 599)
      return 'Sunucu tarafında beklenmeyen bir hata oluştu.';
    if (status === 400) return fallbackMessage;
  }

  if (code === 'Refused') {
    return fallbackMessage
      ? `İşlem reddedildi. ${fallbackMessage}`
      : 'İşlem reddedildi.';
  }

  return fallbackMessage;
}
