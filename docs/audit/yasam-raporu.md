# Orka V2.9 Yaşam Raporu

Tarih: 2026-05-07

## V2.10 Addendum - Heavy Eval, Browser QA, Quiz Product-Label Fix

**Durum: PASS_WITH_NOTES**

V2.10 fazinda `contract_tests/heavy/` altinda gated 40+ senaryolu kalite olcum katmani eklendi. Bu katman intent -> onay -> Korteks -> sentez -> quiz -> plan -> Tutor akisini sadece "test pass" diye degil, alan dogrulugu, onay kapisi, quiz kalitesi, Tutor davranisi ve UX sizintilari acisindan skorlar.

En onemli karar: tablo mantigi milyonlarca konuyu elle sentezleyen statik bir konu ansiklopedisi olmayacak. Tablolar Orka icin kavram dugumleri, onkosul baglari, kaynak baglari, quiz/attempt olaylari, mistake sinyalleri ve kullanici ogrenme durumunu tutan dinamik bir omurga olmali. Konu icerigi Korteks + kaynaklar + sentez katmanindan dinamik cikar; kalici olan kullanicinin ogrenme izi ve kavram grafigidir.

Kritik duzeltme: Quiz katmani artik Orka IDE/sandbox urun etiketlerini cevap seceneklerine veya aciklamalara sokmuyor. Bu anlatim Tutor/pratik akisi icin dogru yerde kalir; quiz sadece kavrami olcer.

Kanit dosyalari:
- `contract_tests/heavy/scenarios.json`
- `contract_tests/heavy/scoring.py`
- `docs/audit/orka-v2.10-heavy-learning-flow-eval.md`
- `docs/audit/tutor-response-scoring-report.md`
- `docs/audit/frontend-browser-qa-report.md`

Son dogrulama:
- `python -m pytest contract_tests/ -q` -> 37 passed, 42 skipped.
- `ORKA_RUN_HEAVY_EVAL=1 ORKA_HEAVY_FULL_FLOW_LIMIT=1 python -m pytest contract_tests/heavy -q` -> 41 passed.
- `npm run build` -> PASS.
- `npm run smoke:ui` -> PASS.
- `npm run smoke:contracts` -> PASS.
- `npm run typecheck` -> PASS.

V2.10 karar guncellemesi:
- Quiz icinde Orka IDE/sandbox urun etiketi artik kritik kalite hatasi sayilir.
- Orka IDE/sandbox anlatimi Tutor ve pratik akisi icin dogru yerde kalir.
- Tablo stratejisi milyonlarca konuyu elle yazmak degildir; dinamik kavram grafigi, kullanici ogrenme izi, kaynak baglari ve sinav/attempt olaylarini tutan veri omurgasidir.
- Sonraki buyuk arastirma/roadmap isinde `orka-derin-arastirma-raporu-2026-05-07.md` ve verilen sektor linki ana referans olarak karsilastirilacaktir.
Faz: V2.9 Quality Reality Gate
Amaç: Orka'nın öğrenme yaşam döngüsünü yeşil test üretmek için değil, gerçek kaliteyi ölçmek için değerlendirmek.

## Genel Karar

**Durum: PASS_WITH_NOTES**

Bu fazda Orka için 56 senaryoluk kalite gerçekliği kataloğu ve 78 test case üreten backend ölçüm kapısı eklendi. Ölçüm katmanı artık yalnızca "dosyada şu string var mı" seviyesinde kalmıyor; niyet analizi, araştırma sentezi, quiz kalite kapısı, plan kalite kontratı ve Tutor davranış kontratını ayrı ayrı ölçüyor.

Önemli ayrım: Bu rapor üretim/staging live provider garantisi değildir. Live Korteks/provider akışları ayrı runtime lifetest ile kanıtlanmalıdır. Bu fazın ana kanıtı deterministic fixture + statik kontrat + frontend smoke guard katmanıdır.

## 1. Niyet Analizi Gerçekten İyi mi?

**Skor: 82 / 100 - PASS_WITH_NOTE**

Ölçülen senaryolar:
- Java algoritmalar
- Java veri yapıları + algoritmalar
- SQL index + sorgu optimizasyonu
- KPSS paragraf hızlanma
- KPSS problem çözme
- C# async/await hata
- Python pandas veri analizi
- Matematik olasılık + kombinasyon
- IELTS speaking
- Yazım hatalı `jva algortima calismak istiyom`

Kanıt:
- `Orka.API.Tests/OrkaV29QualityRealityGateTests.cs`
- `StudyIntentAnalyzer_ProducesApprovedResearchIntentQualitySignals`
- `StudyIntentAnalyzer_CorrectionRegeneratesIntentBeforeKorteks`

Gerçek düzeltme:
- `StudyIntentAnalyzer` yazım hatalı `jva`, `algortima`, `istiyom`, `indx` gibi girdileri daha iyi normalize edecek şekilde güçlendirildi.
- Matematik, olasılık, kombinasyon, İngilizce speaking, KPSS ve asenkron gibi ifadeler araştırma niyetinde daha doğru İngilizce karşılığa çevriliyor.

Kalan not:
- LLM tabanlı intent analizi bazen daha iyi sonuç verebilir; deterministic fallback artık daha iyi ama canlı model çıktısı ayrıca eval edilmelidir.

## 2. Onay UX'i Anlaşılır mı?

**Skor: 78 / 100 - PASS_WITH_NOTE**

Beklenen:
- Kullanıcı isteği önce niyet kartına düşer.
- Korteks'e gitmeden önce kullanıcı onay verir.
- Kart ana konu, odak, amaç ve araştırma niyetini gösterir.

Kanıt:
- Frontend smoke guard: `Plan mode requires intent confirmation before Korteks`
- Frontend smoke guard: `Plan mode exposes meaningful staged UX`

Kalan not:
- Bu fazda browser tabanlı tam görsel lifetest çalıştırılmadı. Bir sonraki ürün QA turunda Playwright/in-app browser ile intent card UX'i piksel ve akış olarak izlenmeli.

## 3. Düzeltme Yapılınca Analiz Yeniden mi Oluşuyor?

**Skor: 85 / 100 - PASS**

Kanıt:
- Correction alanı ile `java calismak istiyorum` girdisi `java veri yapilari ve algoritmalar calismak istiyorum` olarak yeniden analiz edildi.
- Test sonucu araştırma niyetinin `data structures` ve `algorithms` içerdiğini doğruluyor.

Risk:
- Frontend tarafında düzeltme formunun kullanıcının gözünde ne kadar anlaşılır olduğu ayrıca runtime UX testi ister.

## 4. Onay Gelmeden Korteks Çalışıyor mu?

**Skor: 88 / 100 - PASS**

Kanıt:
- Backend `PlanDiagnosticService` onaylı araştırma niyeti olmadan başlamıyor.
- Frontend `ChatPanel` pending intent kartını bekliyor.
- Smoke guard `pendingPlanIntent`, `approvedResearchIntent`, `Onayla ve arastir` bağlarını doğruluyor.

Kalan not:
- Gerçek runtime'da network log ile "onay öncesi Korteks çağrısı yok" kanıtı ayrıca lifetest'e eklenmeli.

## 5. Korteks Doğru Niyet + Optimize Prompt ile mi Çalışıyor?

**Skor: 76 / 100 - PASS_WITH_NOTE**

Kanıt:
- `Approved study intent is required` backend guard.
- Niyet analizinden çıkan İngilizce research intent test ediliyor.
- Ham kullanıcı mesajının araştırma niyetiyle aynı olmaması ölçülüyor.

Kalan not:
- Korteks live çağrı prompt'unun gerçek provider trafiğinde tam snapshot'ı bu fazda alınmadı.
- Live proof için gated runtime test önerilir: `ORKA_RUN_LIFECYCLE=1` tarzı env flag ile 3 akış.

## 6. Korteks Çıktısı Kaç Puan?

**Skor: 74 / 100 - PASS_WITH_NOTE**

Fixture bazlı ölçüm:
- Source-aware notlar
- YouTube pedagogy referansı
- Ön koşullar
- Yaygın hatalar
- Pratik sırası
- Müfredat ipuçları

Kanıt:
- `KorteksCompression_PreservesLearningSignalsForSynthesis`
- `PlanResearchCompressor`
- `PlanIntelligenceBriefBuilder`

Kalan not:
- Bu skor fixture tabanlıdır. Gerçek provider kalitesi; Tavily/YouTube/public provider durumuna, query kalitesine ve rate limitlere bağlıdır.

## 7. Sentez Asistanı Bilgiyi Siliyor mu, Anlamlandırıyor mu?

**Skor: 80 / 100 - PASS**

Kanıt:
- Compressor `PrerequisiteHints`, `LikelyMisconceptions`, `CurriculumMapHints`, `YouTubeLearningReferences` alanlarını koruyor.
- Brief builder bunları plan/quiz tarafına taşınabilir hale getiriyor.

Kalan not:
- Çok uzun Korteks çıktısında `MaxBriefLength` nedeniyle bazı düşük öncelikli detaylar düşebilir. Bu kabul edilebilir ama ileride eval skoruyla izlenmelidir.

## 8. Quizler Gerçekten Seviye Ölçüyor mu?

**Skor: 79 / 100 - PASS_WITH_NOTE**

Ölçülenler:
- 15-25 soru bandı
- concept diversity
- question type diversity
- misconception probe
- teknik konularda code-reading/debugging sorusu
- duplicate soru reddi
- doğru/yanlış sızıntısı reddi

Kanıt:
- `DiagnosticQuizQualityGate_RejectsDuplicateThinObviousQuiz`
- `DiagnosticQuizFallback_IsDomainSpecificAndDoesNotLeakObviousAnswers`

Kalan not:
- Model tarafından üretilen canlı quizlerin pedagojik kalitesi ayrıca runtime eval ister.

## 9. Quiz Soru Sayısını Ne Belirliyor?

**Skor: 84 / 100 - PASS**

Sistem:
- `PlanDiagnosticService.DetermineDiagnosticQuestionCount`
- Broad sinyaller: algorithm, data structure, programming, KPSS, roadmap
- Narrow sinyaller: intro, syntax, single topic
- Sonuç: 15-25 arası clamp

Kanıt:
- Broad Java algoritma/veri yapıları narrow C# syntax'tan daha yüksek soru sayısı üretiyor.

## 10. Quiz Sonucu Bilinen/Eksik Konuları Çıkarıyor mu?

**Skor: 72 / 100 - PASS_WITH_NOTE**

Kanıt:
- Finalize tarafında `KnownConcepts`, `FastTrackConcepts`, `PracticeConcepts`, `WeakConcepts`, `MistakePatterns` satırları plan brief'ine taşınıyor.

Kalan not:
- Konsept bazlı mastery hâlâ daha zengin hale getirilebilir. Şu an sistem doğru/yanlış + skill/mistake signal üzerine çalışıyor; tam curriculum mastery graph V3 konusu.

## 11. Plan Quiz Sonucuna Göre mi Çıkıyor?

**Skor: 80 / 100 - PASS**

Kanıt:
- `PlanIntelligenceBrief_PreservesDiagnosticKnownWeakAndQualityContract`
- `GenerateAndSaveDeepPlanFromDiagnosticAsync`
- Brief içinde diagnostic priority korunuyor.

Kalan not:
- Runtime plan çıktısı canlı model kalitesine bağlıdır; model fallback dönerse kalite fallback modüllerine düşebilir.

## 12. Planda Konu/Başlık Kısıtı Var mı?

**Skor: 78 / 100 - PASS_WITH_NOTE**

Kanıt:
- Minimum 6 modül, 4 ders/modül, 24 ders kalite tabanı var.
- 3 başlıklı ince plan kalite kapısından geçmemeli.

Kalan not:
- `ApplyDiagnosticTraceability` zayıf kavramları öne taşırken ilk birkaç tanısal dersi önceliklendiriyor. Bu bilinçli öncelik olabilir, ama "tüm zayıfları ayrı takip etme" V3 mastery graph tarafında geliştirilmelidir.

## 13. Plan İnternetteki Gerçek Öğrenme Rotaları Gibi mi?

**Skor: 70 / 100 - PASS_WITH_NOTE**

Kanıt:
- Korteks source-aware araştırma sinyalleri compressor ve brief'e taşınıyor.
- Plan quality contract kaynak başlıklarını doğrudan modül adı yapmayı yasaklıyor.

Kalan not:
- Gerçek web araştırmasıyla üretilmiş planlar için canlı benchmark seti gerekir. Bu fazda fixture kalitesi ölçüldü, live quality tam hüküm değildir.

## 14. Anlamlandırılmış Veri Tutor Tarafında Kullanılıyor mu?

**Skor: 77 / 100 - PASS_WITH_NOTE**

Kanıt:
- Tutor aktif topic, memory, performance, wiki, piston, notebook, learning signals, YouTube ve review pressure context çekiyor.
- Orka IDE varsayılan coding ortamı olarak prompt kontratında yer alıyor.

Kalan not:
- Tutor'un canlı cevabında bu bağlamı ne kadar iyi kullandığı ayrı LLM response eval ister.

## 15. Wiki/OrkaLM Döngüye Bağlı mı?

**Skor: 75 / 100 - PASS_WITH_NOTE**

Kanıt:
- Frontend smoke OrkaLM source notebook yüzeyini, Wiki source graph'ını, citation/evidence panelini ve notebook refresh sinyalini doğruluyor.
- Tutor notebook context çekiyor.

Kalan not:
- Kullanıcıya özel her ders için otomatik Wiki üretim ve Redis/Wiki invalidation yaşam döngüsü daha kapsamlı V3 sistem testine ihtiyaç duyuyor.

## 16. UX Profesyonel E-learning Uygulaması Gibi mi?

**Skor: 68 / 100 - PASS_WITH_NOTE**

Kanıt:
- Intent gate, staged plan UX, quiz chat leakage guard, favicon, Mermaid fallback, OrkaLM/Wiki yüzeyi smoke ile korunuyor.

Kalan not:
- Bu faz UI tasarımını yeniden işlemedi. Profesyonel e-learning hissi için browser lifetest, görsel QA ve animasyon/akış polish ayrı frontend fazı olarak devam etmeli.

## 17. Kalan Gerçek Buglar

| Öncelik | Alan | Durum | Not |
| --- | --- | --- | --- |
| P1 | Runtime live plan akışı | PASS_WITH_NOTE | Onay öncesi Korteks yokluğu network log ile kanıtlanmalı. |
| P1 | Live quiz pedagojisi | PASS_WITH_NOTE | Fixture kalite kapısı var, canlı model quizleri ayrıca skorlanmalı. |
| P2 | Tutor response eval | PASS_WITH_NOTE | Prompt kontratı var, canlı cevap rubriği eklenmeli. |
| P2 | Wiki/OrkaLM otomatik ders hafızası | PRODUCT_ROADMAP | Bağlantılar var; her derse özel otomatik Wiki yaşam döngüsü V3. |
| P2 | UX görsel akış | UX_POLISH | Smoke var; gerçek browser/pixel QA yapılmalı. |

## 18. Düzeltme Önceliği

1. Live lifecycle eval: Java, KPSS, SQL için gerçek backend + frontend akışı.
2. LLM cevap rubriği: Tutor planı ve quiz sonucunu gerçekten kullanıyor mu?
3. Plan output scorer: üretilen modül/ders JSON'u kaynak + quiz profiline göre skorlansın.
4. Wiki/OrkaLM ders hafızası: topic bazlı kaynak/notebook yaşam döngüsü daha görünür olsun.
5. Frontend browser QA: intent card, quiz card, plan stage, OrkaLM, audio fallback görsel olarak test edilsin.

## 19. Genel Skor

**Genel sağlık skoru: 77 / 100**

Yorum:
- Sistem artık ölçüm kapısına sahip.
- Kritik akışların büyük kısmı deterministic testlerle korunuyor.
- Canlı LLM/provider kalitesi hâlâ ayrı lifetest gerektiriyor.
- Bu aşama Orka'yı "kolpa pass" seviyesinden "kanıt toplayan sistem" seviyesine taşır.

## 20. Devam / Dur / Yeniden Tasarla Kararı

**Karar: DEVAM**

Gerekçe:
- Sistem tamamen çöpe atılacak durumda değil.
- Ancak "her şey tamam" demek de doğru değil.
- V2.9 sonrası doğru hareket: live eval + frontend browser QA + Tutor response scoring.
