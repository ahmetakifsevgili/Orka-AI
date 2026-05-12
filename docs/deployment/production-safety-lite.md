# Production Safety Lite

Bu not tam production hardening plani degildir. Frontend baseline ve kucuk/orta
ozelliklerden once protected environment icin minimum guvenlik ve ops kapisini
sabitler.

## Required protected environment config

Staging ve Production baslangicta fail-fast davranir. Secret degerleri loglanmaz;
yalnizca eksik config key adlari raporlanir.

- `JWT__Secret`: en az 32 byte.
- `JWT__RefreshTokenHashSecret`: en az 32 byte.
- `Auth__RefreshCookie__Name`: refresh cookie adi. Varsayilan `orka_refresh`.
- `Auth__RefreshCookie__Path`: refresh cookie path'i. Varsayilan `/api/auth`.
- `Auth__RefreshCookie__SameSite`: `Strict`, `Lax`, `None`, veya `Unspecified`.
- `Auth__RefreshCookie__Secure=true`: Staging/Production icin zorunlu.
- `ConnectionStrings__DefaultConnection`: gercek SQL Server connection string.
- `ConnectionStrings__Redis`: Redis connection string.
- `Cors__AllowedOrigins__0`: explicit frontend origin. Bos veya `*` kabul edilmez.
- `AllowedHosts`: explicit host listesi. Bos veya `*` kabul edilmez.
- `RateLimits__Auth__Backend=Redis`.
- `RateLimits__Auth__AllowInMemoryFallback=false`.
- `AI__Cost__Enabled=true`.
- Global daily AI budget: `AI__Cost__GlobalDailyUsdLimit` veya `AI__Cost__GlobalDailyTokenLimit`.
- User daily AI budget: `AI__Cost__UserDailyUsdLimit` veya `AI__Cost__UserDailyTokenLimit`.
- Routed AI provider credentials. Ornek: `AI__GitHubModels__Token`, `AI__Groq__ApiKey`, `AI__OpenRouter__ApiKey`.

Gecici degraded provider modu bilincli olarak gerekiyorsa
`AI__ProductionSafety__AllowMissingProviderCredentials=true` verilebilir. Bu
yalnizca provider credentials kontrolunu bypass eder; budget, CORS, DB, Redis ve
auth rate limit guardlari yine zorunludur.

## Public and private health

- Public: `/health/live` minimal liveness.
- Public: `/health/ready` protected env'de sanitized readiness.
- Public: `/health` protected env'de detailed check isimleri/aciklamalari sizdirmaz.
- Private/admin: `/api/dashboard/system-health`.
- Development: `/api/dev/diagnostics/config` local debug icin kalir, protected env'de `404` doner.

## Security headers

Protected env'de beklenen minimum headerlar:

- `Strict-Transport-Security`
- `X-Content-Type-Options: nosniff`
- `Referrer-Policy: strict-origin-when-cross-origin`
- `Permissions-Policy`
- `X-Frame-Options: DENY`
- enforced CSP

Development HSTS set etmez; HMR/CSP local akisi korunur.

## Validation gate

```powershell
cd D:/Orka
dotnet test Orka.API.Tests/Orka.API.Tests.csproj --filter ProductionSafetyLiteTests --no-restore --verbosity minimal
powershell -ExecutionPolicy Bypass -File scripts/quick-coordination.ps1
powershell -ExecutionPolicy Bypass -File scripts/quick-backend.ps1
git diff --check
git status --short
```

Stage/commit ayri onay olmadan yapilmaz.
