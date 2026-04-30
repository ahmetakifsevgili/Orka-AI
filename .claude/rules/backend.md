---
description: Orka API (.NET 8) katman kuralları. Dependency Injection, Controller, Middleware ve MediatR sınırları.
globs:
  - "Orka.API/**/*.cs"
  - "Orka.Core/**/*.cs"
alwaysApply: false
---

# Backend Kuralları — C# / .NET 8 / API Katmanı

## 1. Katmanlı Mimari Sınırları
- `Orka.Core` saf domain katmanıdır (Entity, DTO, Interface). ASLA dış kütüphanelere (EF Core dahil) bağımlı olmaz.
- `Orka.API`, Controller ve Middleware'leri barındırır. Mantıksal işlemler (Business Logic) burada yazılmaz, `Orka.Infrastructure` servislerine devredilir.
- Bağımlılık Yönü: **API -> Infrastructure -> Core**. (Core asla hiçbir şeye bakmaz).

## 2. API ve Controller Standartları
- Tüm Controller'lar varsayılan olarak `[Authorize]` kullanır.
- Streaming Endpoint'ler (SSE) `IActionResult` dönmez. `async Task` döner ve `Response.Body.FlushAsync()` kullanılarak yazılır.
- Kullanıcı ID'si daima Claim bazlı çekilir: `User.FindFirstValue(ClaimTypes.NameIdentifier)`.

## 3. Dependency Injection (DI) ve Kapsamlar
- Tüm DI kayıtları sadece ve sadece `Orka.API/Program.cs` dosyasında yapılır.
- Asenkron arkaplan işlerinde (`Task.Run` vb.) veya Singleton servislerde, Scoped servislere erişmek için mutlaka `using var scope = _scopeFactory.CreateScope();` ile yeni kapsam açılmalıdır.

## 4. Hata ve Log Yönetimi
- **Fire-and-forget YASAKTIR:** Her `Task.Run` zorunlu olarak bir `try/catch` ve `_logger.LogError` barındırmak zorundadır. Arkada sessizce patlayan task'lara tolerans yoktur.
- Global hatalar `ExceptionMiddleware` ile yakalanır, Controller içinde gereksiz try/catch kirliliği yapılmaz.

## 5. İsimlendirme Kuralları
- Arayüzler `I` ile başlar (`IWikiService`).
- İstek/Cevap modelleri `Request` / `Response` ile biter.
- Enum değerleri her zaman PascalCase olmalıdır.
