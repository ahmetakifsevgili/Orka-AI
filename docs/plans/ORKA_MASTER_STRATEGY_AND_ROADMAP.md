# 🔱 ORKA AI: ANA STRATEJİ VE YOL HARİTASI (MASTER)

Bu belge, Orka AI projesinin geçmişini, bugünkü vizyonunu ve gelecekteki gelişim planını içeren **tek ve kesin referans kaynağıdır**. Diğer tüm taslak dosyalar bu belgede birleştirilmiştir.

---

## 🔍 1. Mevcut Durum Analizi (Audit)

| Ajan | Durum | Görevi |
| :--- | :--- | :--- |
| **AnalyzerAgent** | ✅ Tamamlandı | IntentClassifier paylaşımlı LLM çağrısıyla completion tespiti. |
| **KorteksAgent** | ✅ Tamamlandı | Tavily + Wikipedia + GraderAgent guardrail → Wiki kayıt. |
| **QuizAgent** | ✅ Tamamlandı | Dinamik soru üretimi (Ders:3-5, Müfredat:15-20). |
| **SummarizerAgent** | ✅ Tamamlandı | Idempotent wiki üretimi + Grader peer review + fallback. |
| **EvaluatorAgent** | ⚠️ Kısmi | Puanlama + Redis kaydı var. Tüm ajanları kapsamıyor, gold example yok. |
| **GraderAgent** | ⚠️ Kısmi | Guardrail var. Consensus (çift model oylama) implemente edilmedi. |
| **DeepPlanAgent** | ✅ Tamamlandı | Supervisor (kategori) + Grader (context kalitesi) pipeline. |
| **TutorAgent** | ⚠️ Kısmi | Redis feedback okuma var. Wiki context bağlantısı ve few-shot eksik. |
| **IntentClassifier** | ✅ Tamamlandı | Tek LLM çağrısı → Analyzer + Supervisor ortak kullanımı. |
| **SupervisorAgent** | ✅ Tamamlandı | Intent bazlı TUTOR/QUIZ/RESEARCH routing. |
| **WikiAgent** | ✅ Tamamlandı | Wiki içeriği üzerinde SSE soru-cevap. |
| **RedisMemoryService** | ⚠️ Kısmi | Feedback log + rate limit var. IRedisMemoryService yanlış katmanda. |
| **PistonService** | ✅ Yeni | Kod çalıştırma sandbox. TutorAgent bağlantısı eksik. |

### Tespit Edilen Kritik Mimari Sorunlar

| Sorun | Açıklama |
| :--- | :--- |
| **Clean Architecture ihlali** | `IRedisMemoryService`, `IEvaluatorAgent` arayüzleri Infrastructure'da — Core'a taşınmalı. |
| **AgentRole.Evaluator yok** | EvaluatorAgent, Grader modelini paylaşıyor. Ayrı rol ve model gerekli. |
| **TutorAgent → Wiki kopuk** | Tutor öğretirken konu wikisini okumıyor. KorteksAgent araştırması boşa gidiyor. |
| **Gold Example yok** | 9-10 puan → ham audit log yazılıyor. Curated few-shot örnek hiç implemente edilmedi. |
| **Completion → İlerleme yok** | AnalyzerAgent konu tamamlandı diyor, ama session bir sonraki alt konuya geçmiyor. |
| **Eval scope dar** | Sadece TutorAgent değerlendiriliyor. Korteks, Summarizer, DeepPlan çıktıları kör nokta. |
| **Consensus yok** | GraderAgent tek model. Stratejide yazılı ama hiçbir fazda planlanmamıştı. |
| **Piston → Tutor kopuk** | IDE çalışıyor, kod çıktısı TutorAgent'a gitmiyor. |
| **Skill mastery yok** | Öğrencinin ne öğrendiği takip edilmiyor, sadece başarısızlıklar kaydediliyor. |

---

## 🎯 2. Stratejik Vizyon: Eğitilebilir Ajan Organizasyonu

Sistemin zekasını "Fine-Tuning" yapmadan, akıllı mimariyle geliştirme stratejimiz:

### I. Dynamic Few-Shot (Dinamik Ömnek Kütüphanesi)
- **Mantık**: 9-10 puan alan başarılı diyaloglar "Altın Örnek" olarak saklanır.
- **Uygulama**: Ajan çalışmadan önce bu başarılı örnekler prompt'una enjekte edilir (Kendi başarısını taklit eder).

### II. Recursive Self-Correction (Öz-Düzeltme Döngüsü)
- **Mantık**: TutorAgent'ın stream (akış) modundaki denetimsizliğini çözmek için.
- **Uygulama**: Cevap kullanıcıya gitmeden önce "Hızlı Bir Denetçi" (Small Model) tarafından mikro-hızda süzülür.

### III. Score-Based Instruction Tuning (Puan Odaklı Ayar)
- **Mantık**: Evaluator puanlarının (Uzunluk, Samimiyet, Doğruluk) doğrudan ajan talimatlarını etkilemesi.
- **Örnek**: Puanlar düşükse Tutor otomatik olarak "daha sade anlatım" moduna geçer.

### IV. Consensus (Çoğunluk Kararı - Voting)
- **Mantık**: Kritik "True/False" kararlarının tek bir modele bırakılmaması.
- **Uygulama**: İki model (Llama-8B & Gemini) oylama yapar, çelişkide Llama-405B hakemlik eder.

---

## 🏗️ 3. Orka Sandbox ve OALL (Çekişmeli Öğrenme)

Sizin manuel Swagger testlerinizi sıfıra indiren otomatik eğitim fabrikası:

1.  **Sentetik Öğrenci Fabrikası**: Gemini Flash kullanarak hayali öğrenciler yaratılır:
    - **Öğrenci A (Hassas):** 7. sınıf öğrencisi, çabuk pes eder.
    - **Öğrenci B (Agresif):** Hata mesajlarına sinirlenen yazılımcı.
    - **Öğrenci C (Pasif):** Anlamış gibi yapan ama anlamayan.
2.  **SSE (Swagger Simulation Engine)**: Sistemin API uç noktalarından durmaksızın mesajlaşıp veri toplaması.
3.  **Adversarial Evolution**: Tutor başarılı oldukça, Llama-405B öğrencinin daha kafa karıştırıcı (`A+`) versiyonunu üretir.
4.  **Passive Training Dashboard**: Sizin simülasyonları sadece "Onayla/Reddet" diyerek sistemi eğittiğiniz panel.

---

## 🚀 4. GÜNCEL YOL HARİTASI (Faz 9 - 17)

> **Okuma Kılavuzu:** Her fazın yanındaki etiket önceliği gösterir.
> `[TEMEL]` = Bir sonraki faz bu olmadan çalışmaz.
> `[KRİTİK]` = Hedefin doğrudan gerektirdiği özellik.
> `[GELİŞİM]` = Sistemi daha güçlü yapar, zorunlu değil.

---

### [Faz 9] — Redis: Projenin "Muhabbiri" ✅ TAMAMLANDI
- [x] Redis (StackExchange.Redis) entegrasyonu.
- [x] `RecordEvaluationAsync` + `GetRecentFeedbackAsync` — feedback log (max 20, TTL 7 gün).
- [x] `CheckRateLimitAsync` — rate limit kalkanı.
- [x] `TutorAgent.FetchPerformanceProfileAsync` — Redis notlarını prompt'a enjekte eder.
- [x] `EvaluatorAgent` → puan + feedback → Redis + SQL `AgentEvaluations`.

---

### [Faz 10] — Mimari Temizlik ve Arayüz Katmanı `[TEMEL]`
> **Neden önce bu?** Sonraki fazlardaki her yeni servis doğru katmana injection yapabilmek için bu temel şart.

- [ ] `IRedisMemoryService` → `Orka.Core/Interfaces/` katmanına taşı (şu an Infrastructure'da — Clean Architecture ihlali).
- [ ] `IEvaluatorAgent` → `Orka.Core/Interfaces/IAgents.cs` içine ekle.
- [ ] `AgentRole.Evaluator` enum değeri ekle — EvaluatorAgent kendi modelini kullansın, Grader'ı paylaşmasın.
- [ ] `appsettings.json`'a `AI:GitHubModels:Agents:Evaluator:Model` ekle (önerilen: `gpt-4o-mini`).

---

### [Faz 11] — Ajan Sinir Sistemi: TutorAgent Bağlantı Katmanı `[KRİTİK]`
> **Neden?** Tutor öğretirken konu wikisini görmüyor, KorteksAgent araştırması boşa gidiyor. Bu faz ajanları gerçekten "konuşturan" katmandır.

- [ ] `TutorAgent.FetchWikiContextAsync` ekle: aktif konu wikisini `WikiService.GetTopicPages` ile çek, system prompt'a enjekte et (max 2000 token özet, `[KONU WİKİSİ]` bloku).
- [ ] KorteksAgent araştırma tamamlandığında `orka:wiki-ready:{topicId}` key'ini Redis'e yaz → TutorAgent bir sonraki mesajda güncel araştırmayı bilsin.
- [ ] Piston çıktısı → TutorAgent: `CodeController.RunCode` sonucunu `orka:piston:{sessionId}:last` key'ine yaz → Tutor bir sonraki mesajda "Az önce çalıştırdığın kodu gördüm..." diyebilsin.

---

### [Faz 12] — Dynamic Few-Shot: Altın Örnek Kütüphanesi `[KRİTİK]`
> **Neden?** Strateji bölümünde "I. Dynamic Few-Shot" olarak tanımlandı ama hiçbir fazda yer almamıştı. Sistemin kendi başarısını taklit etmesi için şart.

- [ ] `EvaluatorAgent`: puan >= 9 ise `orka:gold:{topicId}` Redis listesine `(userMessage, agentResponse)` çiftini yaz (max 10 kayıt, TTL 30 gün).
- [ ] `RedisMemoryService.GetGoldExamplesAsync(topicId, count)` metodu ekle.
- [ ] `TutorAgent.FetchGoldExamplesAsync(topicId)`: ilgili konudan max 2 gold örnek çek, system prompt'a few-shot olarak enjekte et (`[ALTIN ÖRNEK]` bloku).
- [ ] `EvaluatorAgent` scope genişletme: `TriggerBackgroundTasks`'ta `agentRole` parametresi dinamik hale getirilsin — Summarizer, KorteksAgent, DeepPlan çıktıları da değerlendirilsin.

---

### [Faz 13] — Skill Mastery ve Kurs İlerleme Motoru `[KRİTİK]`
> **Neden?** "Öğrenciye skill kazandır" hedefinin altyapısı. Completion tespiti var ama bir sonraki konuya geçiş yok, kazanılan beceri kaydı yok.

- [ ] `SkillMastery` entity ekle: `(UserId, TopicId, SubTopicTitle, MasteredAt, QuizScore)`.
- [ ] EF Core migration: `SkillMastery` tablosu.
- [ ] `AnalyzerAgent` `IsComplete=true` döndüğünde → `session.CompletedSections` index'ini ilerlet → bir sonraki alt konunun ilk dersini otomatik başlat.
- [ ] Quiz başarıyla tamamlanınca `SkillMastery` kaydı oluştur.
- [ ] `TutorAgent.FetchUserMemoryProfileAsync` güncelle: başarısızlıkların yanına "kazanılan beceriler" listesini de ekle (`[ÖĞRENCİ ZATEN BİLİYOR: ...]` bloku).

---

### [Faz 14] — GraderAgent Consensus `[GELİŞİM]`
> **Neden?** Strateji bölümünde "IV. Consensus" olarak tanımlandı ama hiçbir fazda yer almamıştı. Tek modele bağımlılığı kırar.

- [ ] `GraderAgent.IsContextRelevantAsync` içinde çift model oylama: `AgentRole.Grader` (Llama-8B) + `AgentRole.Analyzer` (Gemini) paralel sorgulanır.
- [ ] İkisi aynı sonuçta hemfikirsek → direkt karar.
- [ ] Çelişkide: `AgentRole.DeepPlan` (Llama-405B) hakemlik yapar.

---

### [Faz 15] — Orka Sandbox: Simülasyon Merkezi `[GELİŞİM]`
> Faz 11-13 tamamlanmadan bu faz anlamlı sonuç vermez — ajanlar birbirine bağlı değilse simülasyon gerçekçi geri bildirim üretemez.

- [ ] `SandboxController` + `SandboxEngine` servisi.
- [ ] Sentetik Persona tanımları (Gemini Flash ile üretim):
  - **Persona A (Hassas):** 7. sınıf öğrencisi, çabuk pes eder.
  - **Persona B (Agresif):** Hata mesajlarına sinirlenen yazılımcı.
  - **Persona C (Pasif):** Anlamış gibi yapan ama anlamayan.
- [ ] `SandboxSession`: gerçek `/api/chat/stream` endpoint'ine HTTP istekleri atan otonom ajan.
- [ ] Simülasyon çıktıları `SandboxRun` tablosuna kaydedilir (PersonaType, TopicId, TotalScore, Duration).

---

### [Faz 16] — Adversarial Loop `[GELİŞİM]`
- [ ] `TutorAgent` yüksek skor alırsa (ort. >= 8.5, min 10 tur), Sandbox Engine Llama-405B ile `Persona A+` üretir.
- [ ] `A+` profili: daha karmaşık sorular, kasıtlı yanlış yönlendirmeler, konu sapmaları.
- [ ] `RedisMemoryService.GetAverageScoreAsync(topicId)` → ortalama skor hesaplama metodu.

---

### [Faz 17] — Wiki & IDE Tam Entegrasyon + Geliştirici Paneli `[GELİŞİM]`
- [ ] Piston entegrasyonu: `InteractiveIDE` kod çalıştırma ✅ (tamamlandı).
- [ ] Wiki sayfa kalite denetimi: SummarizerAgent çıktısı EvaluatorAgent tarafından puanlanır.
- [ ] `WikiAgent` → KorteksAgent araştırma sonuçlarını wiki copilot yanıtlarına dahil eder.
- [ ] Geliştirici Review Panel: simülasyon raporları listesi + tek tıkla "Altın Veri Seti" export.

---

## 🛠️ 5. Model Envanteri ve Sağlayıcılar

- **Akıl Katmanı**: GPT-4o, Llama-3.1-405B (GitHub Models).
- **Hız Katmanı**: Llama-3.3-70B (Groq/SambaNova/OpenRouter).
- **Simülasyon Katmanı**: Gemini-2.5-Flash, Llama-3.1-8B (Cerebras).
- **Semantik Katman**: Cohere Embed-Multilingual-V3 (Anlamsal Puanlama).
- **Analiz Katmanı**: Mistral-Small (Denetçi/Auditor).

---

> [!IMPORTANT]
> **Faz Bağımlılık Zinciri:**
> `Faz 10 (Mimari Temizlik)` → `Faz 11 (TutorAgent Bağlantısı)` → `Faz 12 (Gold Examples)` → `Faz 13 (Skill Mastery)` → `Faz 14-17 (Gelişim)`
>
> Faz 15 (Sandbox) Faz 13 tamamlanmadan başlatılmamalıdır. Ajanlar birbirine konuşmuyorken simülasyon boş döner.
