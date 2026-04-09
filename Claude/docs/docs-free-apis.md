# docs/free-apis.md — Orka AI Kaynak Envanteri ve Sorumluluklar

Orka, maliyeti minimize edip zekayı maksimize etmek için hibrit bir API stratejisi kullanır. Her model, en başarılı olduğu "niyet" için uzmanlaştırılmıştır.

---

## 🏗️ Görev Dağılım Matrisi

| Model | Servis | Görev | Neden? |
|-------|--------|-------|--------|
| **Gemini 2.0 Flash** | Google AI | Ana Diyalog Yöneticisi | Hızlı, büyük context ve çok zeki. |
| **Gemini 2.0 Flash-Lite** | Google AI | Hızlı Niyet Analizi (Nadir) | Çok düşük latency. |
| **Llama 3.3 70B** | OpenRouter (Free) | Seviye Tespiti (Assessment) | Mantık yürütme ve test sorularında çok katı/başarılı. |
| **Mistral Small** | Mistral AI | Özetleme & Wiki Curator | Ücretsiz token kotası yüksek, arka plan işleri için ideal. |
| **DeepSeek Chat** | OpenRouter (Free) | Fallback (Yedek) | Gemini veya Llama hata verirse devreye girer. |

---

## 🔑 API Yönetim Yasaları

1.  **Güvenlik:** Hiçbir API Key asla client-side (JS) tarafına geçemez. Tüm çağrılar `Orka.Infrastructure` üzerinden proxylenir.
2.  **Maliyet Bilinci:** Sistem, her mesajda kullanılan token'ı ve tahmini maliyeti (ücretsiz olsa bile) DB'ye kaydetmek zorundadır.
3.  **Fallback Protokolü:** Eğer bir model rate-limit'e girerse, `RouterService` saniyeler içinde kullanıcıya hissettirmeden alternatif modele (OpenRouter) geçiş yapmalıdır.

---

## 🛠️ Bağlantı Bilgileri

- **Google AI:** `https://generativelanguage.googleapis.com`
- **OpenRouter:** `https://openrouter.ai/api/v1`
- **Mistral AI:** `https://api.mistral.ai/v1`

---
> Orka'nın gücü, bu modellerin bir orkestra şefi gibi yönetilmesinde saklıdır. Hiçbir model tek başına Orka değildir.
