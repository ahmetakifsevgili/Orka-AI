---
description: Auth, JWT claim, admin gating, user secrets, rate limit kuralları
globs:
  - "Orka.API/Controllers/AuthController.cs"
  - "Orka.API/Controllers/DashboardController.cs"
  - "Orka.Infrastructure/Services/AuthService.cs"
  - "Orka.Core/Entities/User.cs"
  - "Orka.Core/DTOs/Auth/**"
  - "Orka.API/Program.cs"
alwaysApply: false
---

# Güvenlik & Kimlik Doğrulama Kuralları

## JWT Claim Yapısı

Her access token şu claim'leri taşır:

| Claim | Kaynak | Kullanım |
|---|---|---|
| `sub` | `user.Id` (Guid) | Tüm controller'larda `User.FindFirstValue(ClaimTypes.NameIdentifier)` |
| `email` | `user.Email` | Display / logging |
| `plan` | `user.Plan` (Free/Pro) | Rate limit + feature flag |
| `role` (ClaimTypes.Role) | `user.IsAdmin ? "Admin" : "User"` | `[Authorize(Roles="Admin")]` policy için |
| `isAdmin` | Lower-case string | Frontend kolay parse için |

## Admin Gating

- **Backend:** `[Authorize(Roles = "Admin")]` attribute'u hassas endpoint'lerin üstüne koyulur.
- **Frontend:** `storage.getUser()?.isAdmin === true` kontrolü ile admin-only UI gösterilir.
- **Mevcut admin endpoint'leri:** `/api/dashboard/system-health` (LLMOps HUD).
- **Mevcut admin UI'ları:** DashboardPanel'deki "Sistem Analitiği" sekmesi.
- Yeni bir admin-only özellik eklendiğinde: hem backend `[Authorize(Roles="Admin")]` hem frontend `isAdmin` kontrolü zorunludur — tek taraflı kontrol güvenlik açığıdır.

Kullanıcıyı admin yapmak için:
```bash
sqlcmd -S "(localdb)\mssqllocaldb" -d OrkaDb -i promote_admin.sql
```

## API Anahtarları

- `appsettings.json`'a API anahtarı **yazılmaz**.
- `dotnet user-secrets` kullanılır.  Görmek: `cd Orka.API && dotnet user-secrets list`.
- Yeni bir sağlayıcı eklerken: `dotnet user-secrets set "AI:<Provider>:ApiKey" "sk-..."`.
- `appsettings.*.json` (environment-specific) `.gitignore`'dadır.

## Rate Limiting (Redis Token Bucket)

- `IRedisMemoryService.CheckRateLimitAsync(clientIp, maxRequests, window)` ile IP-bazlı.
- Plan bazlı günlük limit `User.DailyMessageCount` + `DailyMessageResetAt` ile takip edilir (Free: 50, Pro: 500).
- Reset günlük `DailyMessageResetAt.Date < DateTime.UtcNow.Date` kontrolü ile.

## Token Refresh Flow

- `accessToken` TTL: 60 dk (config: `JWT:AccessTokenExpiryMinutes`).
- `refreshToken` TTL: 30 gün (config: `JWT:RefreshTokenExpiryDays`).
- Refresh çağrısında eski token `IsRevoked = true` yapılır, yeni çift üretilir.
- Frontend interceptor 401'de tek seferlik refresh + pending queue pattern uygular.

## Secret Sızıntı Önleme

- Log'a asla `user.PasswordHash`, `refreshToken`, `JWT:Secret` yazma.
- Exception mesajlarında bu alanlar görünürse `ExceptionMiddleware` maskeleme yapar.
- Stack trace'te secret varsa — acilen fix.

## ExceptionMiddleware

- `NotFoundException` → 404
- `UnauthorizedException` → 401
- `BadRequestException` → 400
- Diğer `Exception` → 500 + generic mesaj (iç detay log'lanır, response'a yazılmaz).
