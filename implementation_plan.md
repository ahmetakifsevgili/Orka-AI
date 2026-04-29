# Orka AI Multi-Agent Sistem ve Entegrasyon Audit & Çözüm Planı

Bu döküman, Orka AI sistemindeki giriş (auth/swagger) sorunlarını çözmek ve agent'ların birbirleriyle olan iletişimini (Swarm & Feedback Loops), kişiselleştirilmiş eğitim planlarını, quiz adaptasyonlarını ve NotebookLM benzeri görsel/işitsel özellikleri "uçtan uca uyumlu ve çalışır" hale getirmek için hazırlanmış kapsamlı bir eylem planıdır.

## Öncelikli Tespitler ve Bağlam
1. **Giriş ve Swagger Problemi**: Frontend üzerinden giriş yapılamaması büyük ihtimalle `Vite Proxy` / `CORS` ayarlarından veya Backend'deki `Program.cs`'de Swagger/Migration hatalarının API'yi ayağa kaldırmasını (veya Swagger UI'ı production modda gizlemesini) engellemesinden kaynaklanmaktadır.
2. **Agent Orkestrasyonu (AgentOrchestratorService)**: Sistem oldukça detaylı yazılmış. Ancak asıl soru, `EvaluatorAgent` veya `AnalyzerAgent` tarafından Redis'e atılan "Düşük Kalite (Score < 7)" uyarılarının veya "Öğrenci Anlama Puanının (Understanding Score)" `TutorAgent`, `QuizAgent` ve `DeepPlanAgent` tarafından *gerçekten tüketildiği ve davranış değiştirdiği* bir köprü mekanizmasının eksiksiz çalışıp çalışmadığıdır.
3. **Plan ve Quiz Kalitesi**: Kullanıcıların "KPSS, Hackerrank" gibi özel ihtiyaçları için planların jenerik kalmaması, quiz'lerin yanlış cevaplara göre "telafi derslerine (Remedial)" dönüşmesi gerekiyor.
4. **UX/UI Senkronizasyonu**: Agent'lar kendi aralarında anlaşıp durumu değiştirse bile (örneğin arka planda Session State değişimi), Frontend (React) tarafı bu State'leri anlık yakalayamadığında arayüzde kırılmalar veya donmalar ("Thinking..." takılmaları) yaşanmaktadır.

> [!IMPORTANT]
> **Kullanıcı Onayı Gereklidir:** Aşağıdaki eylem planı sistemde hem backend mimari köprülerini (Prompt ve Service katmanı) hem de frontend entegrasyonlarını etkileyecektir. Adımlar sırasıyla incelenip onaylandıktan sonra icra edilecektir.

## 1. Hızlı Onarım (Giriş & Bağlantı Hataları)

### Swagger ve Auth Çözümleri
- **[MODIFY] Orka.API/Program.cs:**
  - `Swagger` middleware'ini ortam bağımsız hale getirip erişilebilirliğini sağlamak (Production/Development ayrımı hatalarını gidermek).
  - CORS politikalarını Frontend'in çalıştığı port ile (Vite genelde 5173'tür) tam uyumlu hale getirmek.
- **[MODIFY] Orka-Front/vite.config.ts & api.ts:**
  - Proxy ayarlarını kontrol edip `/api` isteklerinin sorunsuz `localhost:5065` portuna gittiğini teyit etmek. Token refresh mekanizmasındaki sonsuz döngü ihtimallerini düzeltmek.

## 2. Multi-Agent Köprülerinin (Feedback Loop) İnşası

Şu anda `EvaluatorAgent` düşük puanlı durumlarda Redis'e bayrak (`SetLowQualityFeedbackAsync`) bırakıyor. Ancak `TutorAgent` veya `QuizAgent` bu bayrakları her yanıt öncesinde aktif olarak kontrol etmeli.

### Agent İletişim Entegrasyonları
- **[MODIFY] Orka.Infrastructure/Services/TutorAgent.cs:**
  - Yanıt üretmeden önce Redis'teki öğrenci profili (`Weaknesses`, `UnderstandingScore`) ve `LowQualityFeedback` bayraklarını okuyan bir köprü (bridge) eklemek. Eğer öğrencinin bir konudaki anlama puanı düşükse (örneğin quiz'de 15 doğru 10 yanlış yaptıysa), prompt'u "Telafi Dersi (Remedial Lesson)" moduna sokacak sistem komutları eklemek.
- **[MODIFY] Orka.Infrastructure/Services/DeepPlanAgent.cs:**
  - `/plan` istendiğinde, rastgele başlıklar yerine kullanıcının profilindeki "Hackerrank Algoritma" veya "KPSS Matematik" hedef kelimelerine göre özel mimariler (ve seviye testi sonuçlarına göre eksik odaklı müfredat) çıkartan derinleştirilmiş prompt'ları uygulamak.
- **[MODIFY] Orka.Infrastructure/Services/QuizAgent.cs:**
  - Aynı soruların tekrarlanmasını önlemek için Session veya Topic tabanlı bir "Sorulan Sorular Hash'i" (History) tutmak ve Agent prompt'una "Önceki yanlışlara odaklan, daha önce sorulanları sorma" bilgisini geçirmek.

## 3. Dinamik UX, NotebookLM ve Sınıf Ortamı Entegrasyonu

### Görsel ve İşitsel Özellikler (NotebookLM & Diagram)
- **[MODIFY] Orka.Infrastructure/Services/TutorAgent.cs:**
  - Konu anlatımında görselleştirme istendiğinde, metin içine entegre edilmiş ve Frontend tarafından yorumlanabilen **Mermaid JS** diyagram (örneğin: ````mermaid ````) promptlarını standartlaştırmak.
- **[MODIFY] Orka.Infrastructure/Services/SummarizerAgent.cs & WikiService.cs:**
  - Konu bittiğinde üretilen Wiki dökümanına (NotebookLM özellikleri gibi) otomatik olarak "Pekiştirme Önerileri", "Sık Yapılan Hatalar" ve "Kısa Bilgi Kartları (Study Cards)" modüllerini eklemek.
- **[MODIFY] Orka.API/Controllers/ClassroomController.cs:**
  - Sınıf ortamı podcasting/audio dinleme modu için asistan ve öğrenci etkileşimini, kesintili soru sorma anlarını doğru işleyecek API endpoint'lerini check etmek. (Audio overview stream ve STT/TTS entegrasyonu bağlantıları).

## 4. Frontend UX ve State Eşitlemesi

- **[MODIFY] Orka-Front/src/pages/Chat/ChatBox.tsx (ve ilgili view'lar):**
  - Session state `BaselineQuizMode` veya `QuizPending` olduğunda UI'ın bunu doğru algılayıp "Test Modu"na geçmesini sağlamak.
  - Mermaid markdown'larını `react-markdown` ve `mermaid` eklentileri ile hatasız (WOW efekti uyandıracak şıklıkta) render etmek.

## Doğrulama Planı (Verification Plan)

### Otomatik & Backend Testleri
1. `dotnet run` ile API'yi başlatıp `/swagger` adresinin geldiğini ve `api/auth/login` endpointinin 200/401 döndürdüğünü (500 veya CORS dönmediğini) doğrulamak.
2. Redis üzerinden bir mock "Düşük Kalite Puanı" ve "Öğrenci Zayıflığı" yükleyip, `TutorAgent`ın telafi odaklı yanıt verip vermediğini loglardan izlemek.

### Manuel UX & Senaryo Testleri
1. **Frontend Giriş**: UI üzerinden kayıt olma ve login işlemlerinin sorunsuz çalıştığını, token'ın localStorage'a kaydedildiğini görmek.
2. **Kişiselleştirilmiş Plan & Quiz**: `/plan` yazarak KPSS veya Hackerrank odaklı bir konu için "Seviye Tespit Sınavı"na girmek, kasten birkaç soruyu yanlış cevaplamak ve sistemin planı buna göre güncelleyip güncellemediğini izlemek.
3. **Sınıf/Wiki Modu**: Konuyu tamamladıktan sonra oluşan Wiki'de pekiştirme kartlarının çıkmasını doğrulamak. Ders esnasında sistemin Markdown/Mermaid ile şema çizdiğini frontend üzerinde test etmek.

> [!NOTE]
> Bu plan, sistemi tam entegre ( Swarm zekasıyla çalışan) bir yapıya dönüştürecektir. Plan tarafınızdan onaylandığında, 1. Madde'den (Auth/Swagger) başlayarak kodu düzenlemeye geçeceğim.
