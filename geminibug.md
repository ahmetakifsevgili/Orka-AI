# Orka AI - Comprehensive Systems Audit & X-Ray Report

**Tarih:** 2026-05-25  
**Kapsam:** `D:\Orka` Çalışma Ağacı  
**Durum:** Salt-Okunur Denetleme Raporu (Kod Değişikliği İçermez)  
**Hazırlayan:** Antigravity Swarm Orchestrator & Ajan Swarmı:
*   **DeepArchitectureAuditor** (Algoritma ve Pedagoji Denetçisi)
*   **SystemArchitectAgent** (Veritabanı, EF Core ve State Mimarisi Denetçisi)
*   **PrivacySafetyOfficer** (Güvenlik, GDPR ve Mahremiyet Denetçisi)
*   **FluidUXDeveloper** (Frontend, SSE Entegrasyonu ve Medya UX Denetçisi)

---

## MÜHENDİSLİK RAPORU ÖZETİ

Bu rapor; Orka platformunda yer alan C# backend API, EF Core ilişkisel veritabanı şemaları, Redis bellek/önbellek katmanı, React frontend entegrasyonu ve entegrasyon testlerinin sıfır pazarlama dili (zero-hype) kullanılarak gerçekleştirilen detaylı sistem incelemesidir.

Yapılan analizlerde, mevcut testlerin 310/310 oranında başarıyla (yeşil) yanmasına rağmen, üretim ortamında (production) ciddi servis kesintilerine (DoS), veri sızıntılarına (GDPR ihlali) ve kullanıcı arayüzü çökmelerine yol açabilecek sinsi açıklar tespit edilmiştir. Testlerin bir kısmının karmaşık pedagojik geçişleri gerçekten ölçmek yerine statik veri tohumlaması (seeding) veya string karşılaştırmaları ile yüzeysel olarak kurgulandığı doğrulanmıştır.

---

## 1. BÖLÜM: Pedagojik Algoritmalar ve Test Kapsamı Denetimi (`DeepArchitectureAuditor`)

### 1.1 Entegrasyon Testlerinin Yüzeysellik Analizi
*   **NLP ve Değerlendirme Motorunun Bypass Edilmesi:**
    *   **Konum:** [QuizLearningPipelineTests.cs:L17-40](file:///D:/Orka/Orka.API.Tests/QuizLearningPipelineTests.cs#L17-L40)
    *   **Bulgu:** `ChatQuizCompletion_RecordsDurableAttemptAndCanonicalLearningState` entegrasyon testinde, asenkron NLP ve akıllı puanlama motorunu simüle etmek yerine teste sert kodlanmış bir string gönderilmektedir: `content = "**Quiz Cevabim:** 1/1 Dogru"`. 
    *   **Etki:** Bu string yapısı, tüm semantik değerlendirmeyi ve gerçek BKT (Bayesian Knowledge Tracing) akışını bypass ederek doğrudan 100% ilerleme (progress) üretilmesini sağlar. Test, yapay zekanın gerçek serbest metin analiz yeteneğini ölçmemekte; sadece veritabanına kayıt atılıp atılmadığını kontrol eden yüzeysel bir kontrattır.
*   **Döngüsel Tohumlama Hilesi (Student Simulation):**
    *   **Konum:** [StudentSimulationEvaluationTests.cs:L612-630](file:///D:/Orka/Orka.API.Tests/StudentSimulationEvaluationTests.cs#L612-L630)
    *   **Bulgu:** `SeedAdaptiveJourneyAsync` metodu, gerçek bir öğrencinin zaman içerisindeki dinamik öğrenme adımlarını hesaplayıp simüle etmek yerine, veritabanına doğrudan pre-calculated (önceden hesaplanmış nihai) değerleri tohumlar (Örn: `MasteryProbability = 0.24m`, `Confidence = 0.72m`, `RemediationNeed = "high"`).
    *   **Etki:** Test, bu tohumlanan değerleri API üzerinden geri okuyup doğrulamaktadır. Test kendi yazdığı statik veriyi assert etmektedir, dinamik bir öğrenme akışı simüle edilmemektedir.
*   **Yüzeysel Güvenlik Kontrolleri:**
    *   **Konum:** `StudentSimulationEvaluationTests.cs:L114` (`AssertSafePayload`)
    *   **Bulgu:** Güvenlik ve mahremiyet politikalarının denetimi sadece `"rawPrompt"`, `"apiKey"`, `"ConnectionStrings"` gibi kelimelerin serileştirilmiş string içerisinde aratılmasıyla kısıtlıdır. Gerçek sınır ve yetki denetimleri yapılmamaktadır.

### 1.2 Algoritmik ve Matematiksel Sapmalar
*   **Ayrımcılık Katsayısı (Item Discrimination Estimate) İnversiyonu:**
    *   **Konum:** `AssessmentCalibrationServices.cs`
    *   **Bulgu:** Ayrımcılık katsayısı formülü (`Math.Abs(stat.CorrectRate - 0.50m) * 2m - stat.SkipRate * 0.35m`), standart psikometri teorisine tamamen terstir. Varyansı en yüksek olan (öğrencilerin %50'sinin doğru, %50'sinin yanlış yaptığı ve bilenle bilmeyeni en iyi ayıran) soruları $0.0$ (sıfır ayrımcılık) olarak değerlendirirken; herkesin doğru veya herkesin yanlış yaptığı (varyanssız) sorulara maksimum ayrımcılık katsayısı vermektedir.
*   **BKT Zamansal Unutma Eğrisi Hatası:**
    *   **Konum:** `KnowledgeTracingService.cs:L242-249`
    *   **Bulgu:** BKT unutma faktörünü hesaplarken zaman aşımını doğrusal (linear) bir şekilde %40 ile sınırlamaktadır. Bu, insan belleğinin zamana bağlı üstel (exponential) unutma doğasını yansıtmadığı gibi, ardışık olmayan (out-of-order) denemelerde kronolojik tutarsızlığa yol açmaktadır.
*   **Topological Plan Bypassı:**
    *   **Konum:** `AdaptiveStudyPlannerService.cs:L40-144`
    *   **Bulgu:** Önkoşul (prerequisite) ilişkilerine sahip plan maddelerinde eşit önceliğe sahip elemanlar alfabetik başlıklarına göre (`Title`) sıralanmaktadır. Bu durum, temel bir konunun ileri seviyedeki bağımlı konudan sonra zamanlanmasına yol açabilir.

---

## 2. BÖLÜM: İlişkisel Veritabanı, EF Core ve Önbellek Mimarisi Denetimi (`SystemArchitectAgent`)

### 2.1 Testlerde İlişkisel Bütünlük Hataları
*   **InMemory Database Sağlayıcısının Yetersizliği:**
    *   **Konum:** [ApiSmokeFactory.cs:L120](file:///D:/Orka/Orka.API.Tests/ApiSmokeFactory.cs#L120)
    *   **Bulgu:** API entegrasyon testlerinin neredeyse tamamı `InMemory` veritabanı kullanmaktadır. Ancak bu sağlayıcı **Foreign Key (Dış Anahtar) kısıtlamalarını denetlemez** ve ilişkisel veritabanı Transaction'larını desteklemez.
    *   **Etki:** Testlerde var olmayan bir `TopicId` veya `SessionId` ile ilişkili kayıtlar atılsa bile testler hata vermeden yeşil yanmaktadır. Canlı ortamdaki SQL Server'da bu durum patlayacaktır.
*   **Migration Dosyalarının Test Edilmemesi:**
    *   **Konum:** [DataLifecycleTests.cs:L618](file:///D:/Orka/Orka.API.Tests/DataLifecycleTests.cs#L618)
    *   **Bulgu:** Yaşam döngüsü testleri veritabanını şemadan (`EnsureCreatedAsync`) türetmektedir; yani gerçek EF Core Migration betiklerimiz (`Orka.Infrastructure/Migrations`) test edilmemektedir. Migration dosyalarında olabilecek yazım hataları veya veri kaybı (data truncation) riskleri CI sürecinde kör noktadır.

### 2.2 Concurrency, Deadlock ve Race Conditions
*   **Read-Then-Insert Yarış Koşulları:**
    *   **Konum:** `KnowledgeTracingService.cs:L58-84`
    *   **Bulgu:** Hızlı öğrenci simülasyonları altında, iki eşzamanlı istek aynı anda `FirstOrDefaultAsync` yaptığında ikisi de kayıt bulamayıp `.Add()` çağırıyor ve veritabanına yazarken unique index çarpışmasıyla (Unique Constraint Collision Exception) çöküyor.
*   **Lost Update Anomali Riski (Eksik Concurrency Tokens):**
    *   **Konum:** `KnowledgeTracingState.cs` ve `ConceptMastery.cs`
    *   **Bulgu:** Öğrenme durumu tablolarında `RowVersion` iyimser kilit (optimistic lock) jetonu bulunmamaktadır. Concurrency yükü altında iki paralel istek aynı anlık veriyi çekip güncellediğinde biri diğerinin verisini sessizce ezecektir.
*   **SQL Server 900-Byte Limit İhlali:**
    *   **Konum:** `OrkaDbContext.cs:L1179-1180`
    *   **Bulgu:** `ConceptKey` alanı composite unique index'te yer aldığı için `HasMaxLength(450)` uzunluğuyla (ve UTF-16 karakter alanıyla) 900 byte limitini aşıyor, bu da eski MSSQL sürümlerinde veya üretim ortamlarında indeks ağacı aramalarında performans kaybı yaratır.

### 2.3 Redis Önbellek Mimarisi Açıkları
*   **Ölü Önbellek (Dead Cache - Sadece Yazılan Ama Hiç Okunmayan Key):**
    *   **Konum:** `KnowledgeTracingService.cs:L230`
    *   **Bulgu:** Her denemede son 20 öğrenme durumu çekilip Redis'e `orka:v2:learner-state` anahtarıyla yazılmaktadır. Ancak **bu anahtar tüm codebase genelinde hiçbir yerde okunmamaktadır!** Ciddi veritabanı okuma (DB read IOPS) ve serialization CPU yükü yaratmaktadır.
*   **Redis List Key Sızıntısı:**
    *   **Konum:** `RedisMemoryService.cs` (L44-50)
    *   **Bulgu:** Sıralı write komutları (`ListLeftPushAsync` + `ListTrimAsync` + `KeyExpireAsync`) atomik çalışmamaktadır. Olası bir istisna durumunda `KeyExpireAsync` çağrısına ulaşılamazsa key sonsuz TTL (`-1`) ile Redis'te kalıp bellek sızıntısına yol açmaktadır.

---

## 3. BÖLÜM: Mahremiyet, Güvenlik ve GDPR Denetimi (`PrivacySafetyOfficer`)

### 3.1 Canlı Ortam Kilitlenmesi (Universal Rate Limit DoS - En Kritik Risk!)
*   **Konum:** [AuthController.cs:L261-274](file:///D:/Orka/Orka.API/Controllers/AuthController.cs#L261-L274) ve [Program.cs](file:///D:/Orka/Orka.API/Program.cs)
*   **Bulgu:** Üretim ortamında hız limitlerini (rate limit) IP bazlı bölmek için istemcinin IP adresi (`RemoteIpAddress`) okunmaktadır. Ancak `Program.cs` içinde **Forwarded Headers Middleware (`UseForwardedHeaders()`) aktif edilmemiştir!**
*   **Etki:** Sistem canlı ortamda Nginx, Cloudflare veya AWS Load Balancer arkasına alındığında, tüm dünya kullanıcılarının IP adresi proxy'nin IP'si (örneğin `127.0.0.1`) olarak görünecektir. Tek bir kötü niyetli istek veya bot limiti doldurduğunda, **sistemdeki tüm gerçek kullanıcıların login/register yapması engellenecek ve evrensel 429 kilitlenmesi yaşanacaktır.**
*   **Sınırsız In-Memory Fallback Hafıza Sızıntısı (OOM):**
    *   **Konum:** `AuthAttemptRateLimiter.cs:L16-42`
    *   **Bulgu:** Redis çöktüğünde devreye giren yerel in-memory fallback mekanizması istekleri static bir `ConcurrentDictionary` nesnesinde tutmaktadır. Ancak bu sözlüğü temizleyen bir arka plan worker'ı veya TTL mantığı bulunmamaktadır. Saldırgan sürekli değişen IP ve email başlıklarıyla istek atarak belleği (RAM) şişirip uygulamayı çökertebilir.

### 3.2 Çoklu-Kiracılık (Cross-Tenant) Veri Sızıntısı (GDPR İhlali)
*   **Konum:** [RedisMemoryService.cs:L254](file:///D:/Orka/Orka.Infrastructure/Services/RedisMemoryService.cs#L254) & [TutorAgent.cs:L959](file:///D:/Orka/Orka.Infrastructure/Services/TutorAgent.cs#L959)
*   **Bulgu:** AI few-shot diyalog örnekleri Redis'e global `orka:gold:{topicId}` anahtarıyla yazılmaktadır. Ancak **bu anahtarda `userId` kırılımı yoktur!**
*   **Risk:** `TutorAgent` bu verileri çekerken sahiplik doğrulaması yapmamaktadır. Eğer konu ID'leri tahmin edilirse, User A'nın özel chat geçmişi (içinde şifreler, PII veya sırlar olabilir) User B'nin sistem prompt'una enjekte edilecektir. Bu durum ciddi bir veri sızıntısı ve GDPR ihlalidir.

### 3.3 Regex Sanitization Bypass Vektörü
*   **Konum:** [RedisMemoryService.cs:L250-265](file:///D:/Orka/Orka.Infrastructure/Services/RedisMemoryService.cs#L250-L265)
*   **Bulgu:** Dialog verileri Redis'e yazılmadan önce **sanitizer'dan (`ScrubPii`) geçirilmeden ÖNCE substring ile kırpılmaktadır.**
*   **Risk:** Eğer hassas bir API key veya şifre tam kırpılma sınırına (300 veya 800. karaktere) denk gelip ikiye bölünürse regex şablonlarıyla eşleşmeyeceği için temizleme filtresini aşarak Redis önbelleğine sızacaktır.

---

## 4. BÖLÜM: Frontend Entegrasyonu, SSE ve Medya UX Denetimi (`FluidUXDeveloper`)

### 4.1 Durdurulamayan Ses Trap'i (Browser-native TTS Asenkron Cancel Hatası)
*   **Konum:** [ClassroomAudioPlayer.tsx:L307-311](file:///D:/Orka/Orka-Front/src/components/ClassroomAudioPlayer.tsx#L307-L311)
*   **Bulgu:** Tarayıcı yerel ses motoru durdurulmak istendiğinde `window.speechSynthesis.cancel()` asenkron olarak çalışmaktadır. Tarayıcı, iptal işlemini gerçekleştirdikten sonra bile aktif utterance için `onend` callback'ini tetikler.
*   **Etki:** `onend` callback'i içinde player durumunun hala aktif (`status === "playing"`) olup olmadığı kontrol edilmediği için, **kullanıcı ses oynatıcısını kapatsa veya "STOP" butonuna bassa bile tarayıcı sonraki metin satırlarını okumaya asenkron şekilde devam eder!** Kullanıcı sesi kapatamaz.
*   **G Shielding / WebView Çökmeleri:**
    *   Brave/Tor gibi korumalı tarayıcılarda `window.speechSynthesis.speak(u)` çağrısı doğrudan güvenlik istisnası (security exception) fırlatmaktadır. Kod `try-catch` bloğuna sarılmadığı için tüm React render ağacını çökertmektedir.

### 4.2 Axios Interceptor Çökme Bug'ı
*   **Konum:** [services/api.ts:L199-205](file:///D:/Orka/Orka-Front/src/services/api.ts#L199-L205)
*   **Bulgu:** İnternet kesintisi veya CORS hatası gibi durumlarda Axios `error.config` nesnesini doldurmaz. Interceptor içinde `original._retry = true` atanmaya çalışıldığında `TypeError: Cannot set property '_retry' of undefined` hatası fırlatılarak interceptor çöker ve hatanın UI üzerinde catch edilmesini tamamen engeller.

### 4.3 SSE Paket Parçalanması ve Ham JSON Sızıntısı
*   **Konum:** [ChatPanel.tsx:L486-598](file:///D:/Orka/Orka-Front/src/components/ChatPanel.tsx#L486-L598)
*   **Bulgu:** Eğer gelen SSE event paketi çift kodlama veya parsing uyumsuzluğu nedeniyle `parseTutorStreamEvent(data)` tarafından ayrıştırılamazsa ve `null` dönerse, sistem doğrudan `else` bloğuna düşer ve ham paketi diyalog ekranına yazar.
*   **Etki:** Kullanıcı ekranda aniden `{"type": "tool_started", "toolId": "Search", "status": "running"}` gibi ham backend JSON paketlerinin yazıldığını görecektir.

---

## SONUÇ VE TAVSİYE

Orka AI platformunun test kapsamı (310 adet test) ve katmanlı yapısı oldukça güçlü bir temel sunmaktadır. Ancak, bu audit raporunun gösterdiği üzere **sadece testlerin geçiyor olmasına güvenmek üretim ortamında yanıltıcıdır.** Özellikle reverse proxy IP çakışmaları, Redis gold caching PII sızıntıları, Axios interceptor çökmeleri ve durdurulamayan asenkron ses döngüsü gibi bulgular ivedilikle düzeltilmesi gereken kritik blokörlerdir.

Mevcut dirty snapshot son derece stabildir ve tüm testler bu koşullar altında yeşildir. Değişiklik yetkisi alındığı takdirde bu bulgular adım adım temizlenecektir.
