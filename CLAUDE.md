# Orka AI — Claude Code Kılavuzu

Kişiselleştirilmiş çok-ajanlı AI öğrenme platformu.  Tek kişilik mühendis (Ahmet) tarafından geliştirilir — zamanı azdır, iterasyon ucuz olmalıdır.

## Stack (tek satır)

**Backend** .NET 8 · EF Core + SQL LocalDB · JWT · MediatR · Semantic Kernel ·
**Frontend** React 19 · Vite 6 · Tailwind v4 · Wouter ·
**AI** GitHub Models (Primary) → Groq → Gemini (fallback) ·
**Cache/Telemetri** Redis · **API** `http://localhost:5065/api`

## Altın Kural — Uçtan Uca Takip & Token Ekonomisi

**1. YAMA YAPMA, KÖK NEDENİ ÇÖZ:** İzole yamalar eklemek yerine zincirleme etki analizi (Call-Graph Tracking) yap. Hatanın kök nedenini bul ve mimari bütünlüğü koru.
**2. Her dosyayı açma. Her klasörü listeleme.**
Bir işe başlamadan önce:
1. Hangi katman etkileniyor? → İlgili rule dosyası otomatik yüklenir (path-scoped).
2. Sadece düzeltilecek dosyayı oku.  Çevresindeki klasörü tarama.
3. Değişken ismini biliyorsan → `Grep`. Dosya yolunu biliyorsan → `Read`.

## Kural Yükleme Haritası

Rule dosyaları `.claude/rules/` altındadır, YAML frontmatter ile **path-scoped** yüklenirler — doğru rule, doğru zamanda gelir.

| Dosya | Otomatik yüklendiği yollar |
|---|---|
| `backend.md` | `Orka.API/**`, `Orka.Core/Interfaces/**` |
| `database.md`| `Orka.Infrastructure/Data/**`, `Orka.Core/Entities/**`, `Migrations/**` |
| `frontend.md` | `Orka-Front/src/**` |
| `agents.md` | `Orka.Infrastructure/Services/*Agent*.cs`, `SemanticKernel/**`, `scripts/llm-eval/**` |
| `security.md` | `Orka.API/Controllers/AuthController.cs`, JWT/Auth dosyaları |
| `testing.md` | `scripts/**`, `*.ps1`, `tests/**` |

Her iki tarafı birden etkileyen özellikler için her iki ilgili rule'u oku.

## Doğrulama Disiplini (sırayla)

```bash
# 1) Backend
dotnet build Orka.API/Orka.API.csproj      # 0 hata şart

# 2) Frontend
cd Orka-Front && npx tsc --noEmit          # 0 hata şart

# 3) Tam sistem sağlık testi (backend ayakta olmalı)
node scripts/healthcheck.mjs                # PASS/FAIL/BONUS raporu
```

**Commit atmadan önce** 1+2 zorunlu, 3 önerilir.  Uyarılar mevcuttu ve sayıları artmıyorsa kabul edilebilir.

## Çalıştırma

```bash
# Backend (ayrı terminalde)
dotnet run --project Orka.API

# Frontend (ayrı terminalde)
cd Orka-Front && npm run dev
```

## Mimari Özet

```
Orka.Core/          Entity · Interface · Enum · DTO · Event  (bağımlılık YOK)
Orka.Infrastructure Servis impl · DbContext · SK Plugin · Agent
Orka.API            Controller · Middleware · Program.cs (tüm DI kayıtları)
Orka-Front/src/
  components/       UI bileşenleri
  pages/            Route-level sayfalar
  services/api.ts   Tek Axios instance + tüm API namespace'leri
  lib/types.ts      Tüm shared TS tipleri
  contexts/         React Context provider'ları
```

**Bağımlılık yönü:** API → Infrastructure → Core.  Core asla Infrastructure'a bakmaz.

## Kritik Operasyonel Notlar

- **Admin erişimi:** LLMOps HUD yalnızca `User.IsAdmin = true` hesaplara açıktır.  Geliştirici hesabını admin yapmak için: `sqlcmd -S "(localdb)\mssqllocaldb" -d OrkaDb -i scripts/promote_admin.sql`.
- **API anahtarları:** `appsettings.json`'da DEĞİLDİR — `dotnet user-secrets` içinde saklanır.  Sekreti görmek: `cd Orka.API && dotnet user-secrets list`.
- **Migration eklerken:** `cd Orka.Infrastructure && dotnet ef migrations add <İsim> --startup-project ../Orka.API`.
- **Task.Run fire-and-forget yok:** Tüm arkaplan işleri `try/catch + ILogger` wrapper'ında.
- **Gradient/neon/red-blue-purple yasak** — palet: zinc + emerald (başarı) + amber (uyarı).

## CLAUDE.local.md

Kişisel not/tercihlerin varsa `CLAUDE.local.md` dosyasına yaz — otomatik okunur ve gitignore'dur, commit'lenmez.

## Kişisel Referanslar

- **Docs:** `docs/architecture/` (Mimari harita ve Master Prompt)
- **Admin promote:** `scripts/promote_admin.sql`
- **Sağlık denetimi:** `node scripts/healthcheck.mjs` (aşağıda detay)
- **LLMOps eval:** `scripts/llm-eval/` (promptfoo config)
