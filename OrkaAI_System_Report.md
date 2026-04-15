# Orka AI: Kapsamlı Teknik ve Fonksiyonel Sistem Raporu

## 1. PROJE AMACI VE YÖNETİCİ ÖZETİ
**Orka AI**, .NET 8 ve React ile inşa edilmiş gelişmiş bir "Agentic AI" eğitim orkestrasyon platformudur. Statik bilgiyi aktif ve otonom öğrenme yolculuklarına dönüştürerek dijital öğrenme deneyimini modernize etmek amacıyla tasarlanmıştır.

*   **Temel Misyon**: Premium ve minimalist bir kullanıcı deneyimini korurken; karmaşık eğitim görevlerini (araştırma, bilgi sentezi ve performans değerlendirme) otonom olarak yöneten proaktif bir "Constitutional AI Mentor" sunmaktır.
*   **Yönetici Özeti**:
    *   **Albert Mode (Aktif Araştırma)**: Web üzerinde araştırma yapmak, teknik içeriği sentezlemek ve otomatik olarak kaynakçalı Wiki dökümanları oluşturmak için Semantic Kernel kullanan derin araştırma motoru.
    *   **Ajanik Ekosistem**: Eğitmen (eğitim), Analizör (niyet tespiti), Derin Plan (müfredat oluşturma) ve Wiki-Support (dökümantasyon yönetimi) birimlerinden oluşan çok ajanlı bir sistem.

## 2. SİSTEM MİMARİSİ VE ALTYAPI

### 🏗️ Backend Mimarisi (.NET 8)
Backend, ölçeklenebilirliği ve sürdürülebilirliği sağlamak için **Clean Architecture** (Temiz Mimari) prensiplerini takip eder:
*   **Orka.API**: Yüksek eşzamanlı streaming (SSE), JWT kimlik doğrulama ve Swagger dökümantasyonunu yöneten RESTful API katmanı.
*   **Orka.Core**: Domain modellerini, varlık tanımlarını (`Topic`, `Session`, `WikiPage`) ve temel domain soyutlamalarını içerir.
*   **Orka.Infrastructure**: Veri kalıcılığı (EF Core + SQL Server) ve AI Orkestrasyon katmanının uygulandığı bölümdür.
*   **Tasarım Desenleri**:
    *   **Strateji ve Failover**: Sağlayıcı hatalarını (örneğin Groq'tan Mistral veya Gemini'ye geçiş) zarif bir şekilde yöneten AI Servis Zinciri.
    *   **Middleware Deseni**: Merkezi hata yönetimi (`ExceptionMiddleware`) ve istek bazlı izleme.

### 🧠 LLM Orkestrasyonu (Semantic Kernel)
Proje, ajan tabanlı bir orkestrasyon modeline başarıyla geçiş yapmıştır:
*   **Framework**: Microsoft Semantic Kernel.
*   **Otonomi**: Ajanların manuel boru hatları (pipeline) olmadan araçları (Wiki yönetimi, Web araması) seçmesine ve yürütmesine olanak tanıyan `ToolCallBehavior.AutoInvokeKernelFunctions` özelliğini kullanır.
*   **Pluginler**: 
    - `TavilySearchPlugin`: Gerçek zamanlı bilgi erişimi.
    - `WikiPlugin`: Dinamik dökümantasyon yönetimi.

### 🗄️ Veritabanı ve Veri Akışı
*   **SQL Kalıcılığı**: Oturum maliyetlerini USD cinsinden takip etmek için 10,6 ondalık hassasiyet eşlemesine sahip Entity Framework Core kullanır.
*   **Şema**:
    - `Topic`: Hiyerarşik öğrenme konuları (Deep Planlar için ebeveyn-çocuk desteği).
    - `WikiPage/WikiBlock`: LLM sentezi için modüler bilgi depolama alanı.
    - `QuizAttempt`: Analitik panel için performans metrikleri.

## 3. AJAN DEĞİŞİM GÜNLÜĞÜ VE SİSTEM GEÇMİŞİ
AI Ajanı (Antigravity) tarafından uygulanan yapısal modernizasyonlar ve refaktörler:

| Dönüm Noktası | Açıklama | Etki |
| :--- | :--- | :--- |
| **Semantic Kernel Geçişi** | Çekirdek araştırma ve wiki ajanları, SK yerel araç çağrısı kullanacak şekilde refaktör edildi. | Albert Mode için daha yüksek otonomi ve daha iyi yedekleme (fallback) mantığı sağlandı. |
| **Dashboard Modernizasyonu** | Özel SVG sparkline grafikleri içeren "Silver Glow" HUD uygulandı. | Ağır grafik kütüphanesi bağımlılıkları kaldırıldı ve performans artırıldı. |
| **SSE Stream Düzelmesi** | Oturum başlatma mantığı refaktör edilerek streaming yanıtlarındaki 500 hataları giderildi. | Gerçek zamanlı AI etkileşimleri stabilize edildi. |
| **WikiDrawer Dayanıklılığı** | Özellik isimlendirmeleri (`pageId` -> `id`) senkronize edildi ve çökme döngüleri düzeltildi. | Bilgi yönetimi sırasında UI kararlılığı artırıldı. |

## 4. LLM YAPILANDIRMASI VE TOKEN LİMİTLERİ
Orka AI, sağlam bir maliyet ve context yönetim stratejisi uygular:

*   **Model Yapılandırması**:
    - **Birincil**: Groq / `llama-3.3-70b-versatile` (Hız ve Araç Doğruluğu).
    - **Planlayıcı**: Gemini-2.5-Flash (Derin Planlama ve Geniş Context Yönetimi).
    - **Yedek**: Mistral / `mistral-small-latest`.
*   **Context Yönetimi**:
    - `MaxContextMessages`: 10 (Kayan context penceresi).
    - `MaxContextTokens`: 2000.
    - `Albert Çıktı Limiti`: Derin sentez için 4096 token.
*   **Optimizasyon**: Selamlamalar için hızlı ve önbelleğe alınmış yanıtları tetikleyen "SmallTalk" tespiti uygulanarak önemli ölçüde LLM maliyet tasarrufu sağlandı.

## 5. KULLANIM DURUMLARI VE SİSTEM ÖZELLİKLERİ
1.  **Otonom Derin Araştırma**: Teknik kavramlar için web'i tarayan ve bulguları dökümante eden Albert Mode tetiklemesi.
2.  **Çok Fazlı Öğrenme (Deep Plan)**: Mantık tabanlı bir müfredat (Faz 1'den Faz N'e) oluşturma ve kullanıcıyı dersler boyunca yönlendirme.
3.  **Wiki Yönetimi**: Kullanıcıların not ekleyebildiği ve AI'nın dökümantasyonu geliştirebildiği kalıcı bir bilgi tabanı sağlama.
4.  **Entegre Değerlendirmeler**: Öğrenme sonrası teknik quizler oluşturma ve pedagojik geri bildirim sağlama.
5.  **Analitik HUD**: Öğrenme ilerlemesinin, başarı oranlarının ve token tüketiminin gerçek zamanlı takibi.

## 6. KULLANICI DENEYİMİ (UX) VE UI HARİTALAMA
Platform, **Silver Glow** olarak adlandırılan "Premium Minimalist" bir estetiği takip eder.

*   **Navigasyon Akışı**:
    1. **Kimlik Doğrulama**: Minimalist giriş/kayıt süreci.
    2. **Dashboard Hub**: Konu genel bakışı ve analitik özeti.
    3. **Öğrenme Ortamı**: Bölünmüş ekranlı Chat ve Wiki çekmecesi.
*   **Bileşen Yer Tutucuları**:
    *   `[BURAYA EKRAN GÖRÜNTÜSÜ EKLE: Cam efekti (glassmorphic) elemanlara sahip Landing Page Hero bölümü]`
    *   `[BURAYA EKRAN GÖRÜNTÜSÜ EKLE: Başarı Oranı Sparkline grafiklerini içeren Öğrenme Paneli]`
    *   `[BURAYA EKRAN GÖRÜNTÜSÜ EKLE: Albert Mode anahtarının aktif olduğu bir oturum ekranı]`
    *   `[BURAYA EKRAN GÖRÜNTÜSÜ EKLE: AI tarafından üretilmiş teknik bir dökümanı gösteren Wiki Çekmecesi]`

---
*Raporu Hazırlayan: Antigravity AI (Lead Analyst)*
*Zaman Damgası: 2026-04-11T22:58:00+03:00*
