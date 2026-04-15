# Orka AI — Claude Code Kılavuzu

## Proje Özeti

Orka AI, çok ajanlı bir AI mimarisi kullanan kişiselleştirilmiş öğrenme platformudur.

- **Backend:** ASP.NET Core 8 · EF Core + SQL Server LocalDB · JWT Auth · MediatR · Semantic Kernel
- **Frontend:** React 19 · Vite 6 · Tailwind CSS v4 · Wouter (router) · Framer Motion
- **AI Katmanı:** Groq (Primary) · Mistral · OpenRouter · SambaNova · Cerebras (Fallback chain)
- **Çalışma dizinleri:** `Orka.API/`, `Orka.Core/`, `Orka.Infrastructure/`, `Orka-Front/src/`
- **API base:** `http://localhost:5065/api` — Frontend proxy: `/api → localhost:5065`

## Token Verimliliği — Temel Kural

**Her dosyayı okuma. Yalnızca aktif görevle ilgili dosyalara bak.**

Bir değişiklik yapmadan önce:
1. Hangi katman etkileniyor? (API · Core · Infrastructure · Frontend)
2. O katmanla ilgili kurallar dosyasını oku (aşağıya bak).
3. Yalnızca değiştireceğin dosyayı oku — tüm klasörü tarama.

## Kural Dosyaları — Ne Zaman Okunur

| Görev | Oku |
|---|---|
| C# controller, servis veya entity değişikliği | `.claude/rules/backend.md` |
| Agent, Orchestrator veya Wiki akışı | `.claude/rules/backend.md` |
| React component, sayfa veya hook | `.claude/rules/frontend.md` |
| API servisi (`api.ts`) veya tip değişikliği | `.claude/rules/frontend.md` |
| Her iki tarafı etkileyen özellik | Her iki dosyayı da oku |

## Doğrulama Kuralları

- **Build:** `dotnet build Orka.Infrastructure/Orka.Infrastructure.csproj` — 0 hata olmalı.
- **TS check:** `cd Orka-Front && npx tsc --noEmit` — 0 hata olmalı.
- **Çalıştırma:** Backend `dotnet run --project Orka.API`, Frontend `cd Orka-Front && npm run dev`.
- Test endpoint'lerini `http://localhost:5065/api/chat/test-ai` ile hızlı doğrula.
- Uyarılar (warning) zaten mevcutsa ve sayıları artmıyorsa kabul edilebilir.

## Hızlı Referans

```
Orka.Core/          → Entity'ler, Interface'ler, Enum'lar, DTO'lar (bağımlılık YOK)
Orka.Infrastructure/ → Servis implementasyonları, EF DbContext, SK Plugins
Orka.API/           → Controller'lar, Middleware, Program.cs (DI kayıtları)
Orka-Front/src/
  components/       → Yeniden kullanılabilir UI bileşenleri
  pages/            → Route seviyesi sayfalar
  services/api.ts   → Tek Axios instance + tüm API namespace'leri
  lib/types.ts      → Tüm shared TS tipleri buradadır
  contexts/         → React Context provider'ları
```
