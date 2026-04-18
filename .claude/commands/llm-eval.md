---
description: LLM kalite değerlendirmesini (promptfoo) çalıştır ve özet sun
---

Backend ayakta olmalı.  Admin hesabı gerekli (SystemHealth endpoint'i için — admin JWT gerekiyor mu? kontrol et).

```bash
cd scripts/llm-eval
npx promptfoo eval
```

Çalışma bittiğinde:
1. Geçen/kalan scenario sayısı (konsol çıktısı).
2. Ortalama faithfulness / answer-relevance skoru.
3. Halüsinasyon riski oluşan scenario varsa listele.
4. `npx promptfoo view` ile detay raporu tarayıcıda açılabileceğini hatırlat.

Kullanıcıya 8 satırı geçmeyen özet sun.
