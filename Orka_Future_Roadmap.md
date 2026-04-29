# Orka AI: Gelecek Vizyonu ve Yol Haritası (4-5 Yıllık Plan)

Bu doküman, Orka AI'nın "Kişiselleştirilmiş Çok Modlu Eğitim" platformu olarak büyüme stratejilerini, entegre edilecek yenilikçi özellikleri ve uzun vadeli iş/altyapı kurgusunu kayıt altında tutmak için hazırlanmıştır.

---

## 📌 Mevcut Sistem Envanteri (Zaten Var Olanlar)
Bu özellikler **halihazırda çalışmaktadır**, aşağıdaki yeni fikirlerde bunlar tekrarlanmayacaktır:
- **TutorAgent** — Derinlemesine sohbet bazlı özel ders
- **QuizAgent** — Otomatik quiz üretimi ve değerlendirme
- **DeepPlanAgent** — Konuyu 4 alt başlığa bölme ve müfredat üretimi
- **SummarizerAgent** — Wiki (Not Defteri) üretimi
- **KorteksAgent** — Tavily web araştırması (Semantic Kernel)
- **EvaluatorAgent** — AI cevap kalitesi puanlama (1-10)
- **IntentClassifierAgent** — Öğrenci niyeti sınıflandırma (UNDERSTOOD/CONFUSED/QUIZ_REQUEST)
- **SupervisorAgent** — Ajan yönlendirme (Router)
- **AnalyzerAgent** — Konu tamamlanma analizi
- **GraderAgent** — Quiz puanlama
- **PistonService (Judge0)** — Canlı kod derleme (RCE) - 16 dil
- **LearningSignalService** — Öğrenci beceri sinyalleri izleme
- **SkillMasteryService** — Alt konu ustalık kaydı
- **AudioOverviewService** — Podcast/ses özeti üretimi
- **ClassroomService** — Sınıf oturumu yönetimi
- **LearningSourceService** — Dosya yükleme ve kaynak grounding
- **RedisMemoryService** — Kısa/uzun süreli hafıza, altın örnekler
- **Mermaid diyagramları** — Akış/mimari diyagramları (Frontend render)
- **Pollinations.ai** — Görsel üretimi (img embed)
- **WikiDrawer / WikiMainPanel** — Wiki okuma ve soru sorma
- **InteractiveIDE** — Kod editörü + Judge0 entegrasyonu
- **SystemHealthHUD** — Sistem sağlık monitörü
- **Onboarding Tour (Joyride)** — Yeni kullanıcı tanıtımı

---

## 🔮 YENİ ÖZELLİKLER VE API ENTEGRASYONLARI

### ━━━━━━━━━━ BÖLÜM A: EKSİK KRİTİK ALTYAPI VE ANALİTİK (ÖNCELİK 1) ━━━━━━━━━━

**1. Öğrenci Analitik Dashboard (Kendi Veri Altyapımız)**
- Öğrenci kaç dk çalıştı, hangi konuda kaç quiz çözdü, haftalık trend grafiği. SkillMastery ve LearningSignal verilerini görselleştirme.

**2. Duygu Analizi / Sentiment Detection (Hume AI API veya LLM-based)**
- Öğrenci sıkıldı mı, sinirli mi, heyecanlı mı? TutorAgent tonu buna göre değişmeli. Pedagojik adaptasyonun temeli.

**3. Bildirim Sistemi / Push Notification (Firebase Cloud Messaging - FCM)**
- Spaced Repetition, Daily Challenge, streak hatırlatma için şart. PWA ile push notification entegrasyonu.

**4. Öğrenci Profil Sayfası**
- XP, rozet, skill tree, çalışma geçmişini bir arada gösteren sosyal/kişisel görünüm.

**5. Rate Limiting & API Maliyet İzleme Middleware**
- Çoklu API entegrasyonları için bütçe kontrolü ve kota bitiminde fallback mekanizmaları.

### ━━━━━━━━━━ BÖLÜM B: VERİ BESLEMELİ GERÇEK DÜNYA ÖĞRENİMİ ━━━━━━━━━━

**6. YouTube Transcript RAG Sistemi (YouTube Data API v3 + Transcript API)**
- En çok izlenen eğitim videolarının altyazısı çekilir. TutorAgent referans alarak ders anlatır.

**7. Wikipedia Özet Entegrasyonu (Wikipedia REST API)**
- Tarih, biyoloji gibi alanlarda güvenilir ansiklopedik bağlam. KorteksAgent'ı zenginleştirir.

**8. Haber Akışı Entegrasyonu (NewsAPI / GNews API)**
- Ekonomi/coğrafya çalışırken bugünkü gerçek haberlerden örnek verilir.

**9. Hava Durumu ile Coğrafya/Fizik (OpenWeatherMap API)**
- Canlı verilerle (sıcaklık, basınç) iklim veya termodinamik konularını somutlaştırma.

**10. Kesin Matematik Çözümleri (Wolfram Alpha API)**
- LLM halüsinasyonunu engeller: Adım adım, %100 doğru matematiksel hesaplamalar ve grafikler.

**11. Canlı Borsa/Kripto Verisi ile Kod Görevleri (CoinGecko / Alpha Vantage API)**
- Gerçek veri kümeleri üzerinden algoritma/kodlama pratikleri.

**12. GitHub Açık Kaynak Kod Okuryazarlığı (GitHub REST API)**
- Design Pattern veya mimari öğrenirken açık kaynak projelerden anlık kod örnekleri çekme.

**13. Akademik Makale Erişimi (Semantic Scholar / arXiv API)**
- İleri seviye öğrenciler için güncel makale abstract'larını özetleyip Wiki'ye ekleme.

### ━━━━━━━━━━ BÖLÜM C: GAMİFİCATION & MOTİVASYON ━━━━━━━━━━

**14. Yetenek Ağacı / Skill Tree (Frontend)**
- Tamamlanan konular yeşil, devam edenler sarı, kilitli olanlar gri. Açılış animasyonlarıyla motivasyon.

**15. XP / Seviye / Rozet Sistemi**
- Quiz başarısı, konu tamamlama ve streak durumuna göre ödüllendirme.

**16. Aralıklı Tekrar Motoru / Spaced Repetition Engine**
- Ebbinghaus Unutma Eğrisi'ne göre "Anlaşılmayanlar" listesini belirli periyotlarla tekrar sorma.

**17. Günlük Meydan Okuma / Daily Challenge**
- Seviyeye uygun günlük görevler ve streak tutma.

**18. Otomatik Flashcard Üretimi (Anki Mantığı)**
- Wiki'deki kavramlardan anında ön-arka ezber kartları oluşturma.

**19. Liderlik Tablosu / Leaderboard (Opsiyonel Sosyal)**
- Sınıf içi veya global rekabet (isteyen kapatabilir).

### ━━━━━━━━━━ BÖLÜM D: GÖRSEL, SES & ETKİLEŞİM İNOVASYONLARI ━━━━━━━━━━

**20. İnteraktif 3D Model Render (React Three Fiber + Sketchfab API)**
- Algoritma ağaçları, veri yapıları, hücre yapısı gibi konuları mekansal olarak inceleme. (Sadece bir 3D kütüphanesine odaklanılacak).

**21. WebGL Tabanlı Matematik Grafikleri (Desmos API / Function Plot)**
- Etkileşimli 2D/3D fonksiyon grafikleri (parametre kaydırma ile anlık değişim).

**22. TTS: Sesli Ders Anlatımı (Edge TTS / OpenAI TTS)**
- Başlangıçta düşük maliyetli ve kaliteli ses sentezi ile eller serbest öğrenim.

**23. STT: Sesli Soru Sorma (Whisper / Web Speech API)**
- Öğrencinin mikrofondan soru sorabilmesi.

**24. Kod Diff Görselleştirme (Monaco Diff Editor)**
- Öğrenci kodu ile AI'ın önerdiği doğru çözümün yan yana karşılaştırılması.

**25. Çoklu Dosya IDE Desteği**
- HTML+CSS+JS gibi web projelerini aynı anda düzenleyebilme.

**26. Regex / SQL Playground**
- Kod bloklarında regex101 benzeri anlık test ortamları.

**27. Dil Öğrenimi İçin Pronunciation Checker (Web Speech API)**
- Yabancı dil veya kavram telaffuz doğrulama.

**28. Kimya Molekül Görselleştirme (PubChem API + 3Dmol.js)**
- Molekülleri 3D döndürülebilir şekilde tarayıcıya basma.

### ━━━━━━━━━━ BÖLÜM E: İLERİ EĞİTİM & PLATFORM YETENEKLERİ ━━━━━━━━━━

**29. Otomatik Müfredat Eşleştirme (OpenSyllabus / MEB Kurgusu)**
- DeepPlan'in mevcut altyapısına dış kaynaklı sabit müfredat JSON'ları entegre etme.

**30. Otomatik Hata Sınıflandırma (Error Taxonomy)**
- Öğrencinin tekrarlayan hatalarını tespit edip bunlara özel "remedial" (telafi) dersleri üretme.

**31. Otomatik Not / Bookmark Sistemi**
- Öğrencinin ders anında belirli mesajları kendi özel listesine kaydedebilmesi.

**32. Topluluk Soru Bankası**
- Kullanıcıların kendi ürettiği quizleri havuzda paylaşabilmesi.

**33. Öğretmen Modu / Teacher Dashboard**
- Sınıf ortalaması, zayıf konular ve öğrenci bazlı ilerleme raporları.

**34. A/B Test Motoru / Pedagoji Optimizasyonu (İleri Aşama)**
- Farklı anlatım stillerinin etkililiğini ölçme.

**35. Collaborative Learning (Yjs - İleri Aşama)**
- Aynı soru üzerinde eşzamanlı çalışma (conflict resolution gerektirir).

**36. AI Ödev Kontrol Sistemi (İleri Aşama)**
- Yüklenen dosyaları intihal ve doğruluk açısından değerlendirme.

---

## 🚀 İŞ VE BÜYÜME PLANI (GÜNCELLENMİŞ SOMUT FAZLAR)

### Faz 1A: Stabilite ve Temel Analitik (ŞU AN — 1. Ay)
- ✅ Sistem altyapısının stabilizasyonu (RCE, Wiki, Görselleştirme fixleri yapıldı).
- **Aksiyon 1:** YouTube Transcript RAG eklenmesi (En hızlı değer katan dış kaynak).
- **Aksiyon 2:** Öğrenci Analitik Dashboard arayüzünün yapılması (Veri zaten mevcut).
- **Aksiyon 3:** Bildirim Altyapısı (FCM) entegrasyonu (Sonraki gamification adımları için şart).

### Faz 1B: Gamification Temeli (2-3. Ay)
- XP/Rozet sistemi ve Skill Tree (Yetenek Ağacı) arayüzlerinin inşası.
- Spaced Repetition (Aralıklı Tekrar) motorunun devreye alınması (Bildirim sistemi ile).
- Daily Challenge (Günün Sorusu) ve Streak (Seri) mekaniklerinin eklenmesi.
- Öğrenci Profil sayfasının yayına alınması.

### Faz 2: Veri Beslemeli Öğrenme (3-6. Ay)
- Wolfram Alpha ile matematiksel doğruluk motoru.
- Wikipedia, NewsAPI ve OpenWeatherMap ile derslerin güncel verilerle zenginleştirilmesi.
- Duygu Analizi modülü ile TutorAgent'ın empati yeteneğinin artırılması.
- MEB/Standart müfredat eşleştirmelerinin sisteme gömülmesi.

### Faz 3: Görsel, Ses ve Hibe Dönemi (6-12. Ay)
- Edge TTS / OpenAI TTS ile sesli ders ve Whisper ile sesli girdi.
- React Three Fiber + Sketchfab ile 3D model etkileşimleri.
- Desmos / Function Plot ile matematik grafikleri.
- Google for Startups, AWS Activate, Microsoft Founders Hub gibi platformlara hibe başvuruları ve cloud kredisi temini.

### Faz 4: Ekosistem ve Genişleme (1-2 Yıl)
- Mobil Uygulama (React Native ile App Store & Google Play).
- Teacher Dashboard (Öğretmen Paneli) ve Otomatik Sınav Üretici.
- Collaborative Learning (Yjs tabanlı eşzamanlı çoklu kullanıcı etkileşimi).
- LMS Entegrasyonları (Moodle, Google Classroom, Canvas).

### Faz 5: Kurumsal B2B ve İleri İnovasyonlar (2-5 Yıl)
- Üniversite ve şirketler için **White-label SaaS** çözümleri.
- AR/VR (Artırılmış/Sanal Gerçeklik) entegrasyonları.
- AI Avatar (Video Eğitmen - HeyGen/Synthesia vb.)
- Hibe ve yatırımlarla kendi fine-tuned LLM sunucularımızın (Private Cloud) kurulması.
