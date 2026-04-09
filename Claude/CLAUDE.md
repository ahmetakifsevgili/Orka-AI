# CLAUDE.md — Orka AI Anayasası ve Protokolleri

Bu dosya Orka projesinin en üst düzey karar mercidir. Her oturumda ve her işlemde ilk olarak BURASI okunacaktır. Buradaki yasalar çiğnenemez, esnetilemez.

---

## 🏛️ TEMEL FELSEFE
Orka, pasif bir chatbot değil; kullanıcının öğrenme sürecini yöneten, seviyesini ölçen ve dinamik olarak yol haritası çizen **"Proaktif bir AI Mentordur."** Sistem, filtrelerle değil; canlı bir diyalog ve bağlam (context) yönetimiyle çalışır.

---

## 🤖 4 AJAN MİMARİSİ (KESİN TANIM)

> [!IMPORTANT]
> Sistem tam olarak 4 ajandan oluşur. Her ajanın kendine özgü API key'i ve modeli vardır.

| Ajan | Rol | Model | API Key | Servis |
|------|-----|-------|---------|--------|
| 🔵 **Router Agent** | Kullanıcı niyetini okur, trafik yönetir | Gemini 2.0 Flash | `RouterApiKey` | `RouterService` → `GeminiService.SemanticRouteAsync` |
| 🟢 **Tutor Agent** | Ders anlatır, plan yapar, ana sohbet yönetir | Gemini 2.0 Flash | `WikiApiKey` | `ChatService` → `GeminiService.TutorGetResponseAsync` |
| 🟠 **Assessor Agent** | Seviye ölçer, mülakat simulasyonu yapar | Groq — Llama 3.3 70B | `Groq:ApiKey` | `ChatService` → `GroqService.GetResponseAsync` |
| 🟣 **Curator Agent** | Arka planda Wiki besler, QuizBlock üretir | Mistral Small | `Mistral:ApiKey` | `WikiService` → `MistralService.GenerateReinforcementQuestionsAsync` |

**Önemli:** OpenRouter artık kulllanılmıyor. Assessor için doğrudan Groq kullanılır.

---

## 🚫 KESİN YASAKLAR (PROHIBITIONS)
- **Filtre ve Manuel Seçim YASAKTIR:** Kullanıcıya asla model, mod veya filtre seçtirilmez.
- **Statik Kelime Eşleşmesi YASAKTIR:** "sa", "as" gibi kelimelere bakarak niyet okunmaz. Niyet okuma sadece LLM tabanlı bağlam analizi ile yapılır. *(İstisna: Network sorunlarında fallback amaçlı 10 karakter altı sezgisel 'heuristic' mantığı veya / ile başlayan slash komutları (0ms kuralı) kalkanı aşmak için yasal istisnadır.)*
- **Pasif Bekleyiş YASAKTIR:** AI asla sadece bir cevabı verip susmaz. Her zaman diyalog akışını (Assessment → Plan → Study) bir sonraki adıma taşımalıdır.
- **Yanlış Ajan Kullanımı YASAKTIR:** Her görev için doğru ajan çağrılmalıdır. Routing için Tutor, Ders anlatımı için Router kullanılamaz.
- **Dışlanmış Modeller YASAKTIR:** OpenRouter artık kullanılmıyor. 4 ajan dışında model kullanılamaz.
- **Kaydırsız Bilgi YASAKTIR:** Öğrenilen her kritik bilgi, saniyeler içinde Wiki'ye işlenmek zorundadır.
- **String Contains ile Bayrak Okuma YASAKTIR:** `PhaseMetadata` bayrağı kontrolü her zaman JSON deserialize ile yapılır.

---

## ✅ SİSTEMİK ZORUNLULUKLAR (MANDATES)
1. **Dinamik Diyalog:** Her mesajda `Topic.Metadata` (Seviye, İlerleme, Tercihler) okunmalı ve cevaba dahil edilmelidir.
2. **Önce Onay:** Yeni bir plan veya seviye testi yapılmadan önce kullanıcıdan mutlaka onay alınmalıdır ("Hazır mısın?", "Plan yapalım mı?").
3. **Wiki Canlılığı:** Her ders anlatımı sonrası Wiki bloğunun altına otomatik olarak **"Pekiştirme Soruları"** eklenmelidir.
4. **Semantik Budama (Semantic Truncation):** 50 mesajı geçen diyaloglarda, bağlamı korumak ve token maliyetini yönetmek için eski mesajlar "Semantik Özet" haline getirilerek budanmalıdır.
5. **Hata Yönetimi (Graceful Fallback):** Ana model (Gemini) hata verirse, sistem kullanıcıya hissettirmeden yedek modele (OpenRouter Llama 3.3) geçmelidir.
6. **Temiz Mimari:** Tüm AI mantığı `Orka.Infrastructure` içinde kalmalı, Controller'lar sadece diyalog yöneticisini (Manager) çağırmalıdır.
7. **Geri Dönüş (Resumption):** 24 saat sonraki ilk mesajda AI kullanıcıya son bırakılan konuyu hatırlatmalıdır.
8. **Thread-Safe Arka Plan:** Wiki Curator ve diğer Fire & Forget işlemler `IServiceScopeFactory` ile kendi DbContext scope'larını açmalıdır.

---

## 📂 DOKÜMANTASYON HARİTASI
| Dosya | İçerik |
|-------|--------|
| `docs/docs-architecture.md` | "Diyalog Yöneticisi" bazlı yeni nesil mimari şeması. |
| `docs/docs-ai-router.md` | **"Beyin Spesifikasyonu"**. Diyalog fazları ve analiz kuralları. |
| `master-scenario-v2.md` | Başarı kriteri E2E test senaryosu. |
| `orka-orchestra-fix.md` | Orkestrasyonun güncel teknik uygulama rehberi. |

---

## 🔄 ÇALIŞMA PROTOKOLÜ
1. **Analiz:** Önce `CLAUDE.md` oku.
2. **Hafıza:** DB'deki mevcut durumu ve `Metadata`yı kontrol et.
3. **İmplementasyon:** Dokümandaki **kod standartlarına** birebir uy (Kopyala-Yapıştır değil, mantığı koru).
4. **Doğrulama:** `dotnet build` → 0 hata.
5. **Rapor:** Yapılan değişikliği "Yasalara Uyumluluk" çerçevesinde raporla.

---
> Orka bir program değildir, yaşayan bir otomasyondur.
