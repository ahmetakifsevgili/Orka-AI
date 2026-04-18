---
description: Sistem sağlığı, LLM kalite eval, smoke test, promptfoo, Node sağlık script kuralları
globs:
  - "scripts/**"
  - "scripts/llm-eval/**"
  - "*.ps1"
alwaysApply: false
---

# Test & Sağlık Denetim Kuralları

## Sağlık Denetim Scripti — `scripts/healthcheck.mjs`

Tek komutla tüm sistemi test eder:

```bash
node scripts/healthcheck.mjs                     # backend localhost:5065 olmalı
node scripts/healthcheck.mjs --base-url=http://stage:5065   # farklı backend
node scripts/healthcheck.mjs --quick             # sadece smoke testler (LLM çağrısı yok)
```

Test kategorileri (zorunlu sıra):
1. **Infra** — Backend health endpoint, DB migration durumu, Redis ping
2. **Auth** — Register → Login → Refresh → Revoke akışı
3. **Core CRUD** — Topic create/list, Session create, Message CRUD
4. **AI akışları** — DeepPlan üretimi, TutorAgent stream, quiz üretimi
5. **LLMOps** — EvaluatorAgent skoru var mı, Redis metric push oluyor mu, provider mix sağlıklı mı
6. **Admin endpoint'leri** — Admin JWT ile `/api/dashboard/system-health` erişimi

Çıktı:
- Konsolda PASS/FAIL/BONUS ile renkli rapor.
- `scripts/reports/healthcheck-YYYYMMDD-HHMM.json` — detaylı JSON.
- `scripts/reports/healthcheck-YYYYMMDD-HHMM.md` — insan okunur markdown.

## LLM Kalite Değerlendirmesi — `scripts/llm-eval/`

**Araç:** [promptfoo](https://www.promptfoo.dev) — HTTP provider ile Orka endpoint'lerine bağlanır.

```bash
cd scripts/llm-eval
npx promptfoo eval              # tüm senaryoları çalıştır
npx promptfoo eval -t deneme    # yalnızca "deneme" tag'li senaryolar
npx promptfoo view              # son sonucu browser UI'da görüntüle
```

Config dosyası `promptfooconfig.yaml` içerir:
- Orka `/api/chat/stream` HTTP provider tanımı
- 20+ scenario: farklı konu/faz/zorluk kombinasyonları
- LLM-as-judge assertion'ları (factual, pedagogy, context boyutları)

## Kurallar

- **Production DB'ye test scripti asla çalışmaz** — yalnızca `OrkaDb` (local) veya `OrkaDb_Test`.
- Yeni senaryo eklerken minimum `factual` ve `context` assert ekle.
- Test hesapları: `orka_test_runner@orka.ai` (script otomatik oluşturur/temizler).
- PowerShell scriptleri kullanılmaz — **Node.js tercih edilir** (cross-platform, bash'ten çalışır).

## Başarı Kriterleri (kapı eşikleri)

| Kategori | Hedef | Kritik seviye |
|---|---|---|
| Infra uptime | 100% | FAIL → gate |
| Auth flow | 100% | FAIL → gate |
| Core CRUD | 100% | FAIL → gate |
| AI akışları | ≥ 90% | < 70% → gate |
| LLMOps avg score | ≥ 7.0/10 | < 5.0 → gate |
| Primary provider ratio | ≥ 85% | < 60% → gate |

Gate seviyesi altındaki bir PR commit'lenmez.
