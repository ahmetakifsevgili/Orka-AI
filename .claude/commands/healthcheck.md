---
description: Tüm Orka sistemini tek komutla dene ve rapor oluştur
---

Backend'in ayakta olduğundan emin ol (`dotnet run --project Orka.API` ayrı terminalde).

Sonra şu komutu çalıştır:

```bash
node scripts/healthcheck.mjs
```

Çıkan rapora bak (`scripts/reports/` altında JSON + MD).  Fail olan testleri listele, her biri için:
1. Hangi katmanda bozuk (Infra / Auth / CRUD / AI / LLMOps / Admin)?
2. Root cause olabilecek en muhtemel dosya ne?
3. Hızlı fix önerisi.

Kullanıcıya 10 satırı geçmeyen özet sun.  Detay isterse JSON raporunun tam yolunu ver.
