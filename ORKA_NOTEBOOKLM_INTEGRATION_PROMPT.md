# 🎯 ORKA AI: NOTEBOOK_LM ENTEGRASYON PROMPTU (PHASE 20)

**GÖREV TANIMI:**
Orka AI'nın mevcut N-to-N Swarm mimarisini bozmadan, sistemi bir "Google NotebookLM" rakibine dönüştürmek için aşağıdaki 4 ana özelliği (Audio Overview, Document Grounding, Auto-Glossary ve Source Pinning) sisteme entegre edeceksin. Lütfen adımları sırayla izle ve C# (.NET 8) backend ile React frontend arasındaki bağları eksiksiz kur.

---

## ADIM 1: GERÇEK SESLİ ÇİFT-SUNUCULU PODCAST (AUDIO OVERVIEW)
Mevcut durumda `TutorAgent.cs` içinde `[VOICE_MODE: PODCAST]` tetikleyicisi metin tabanlı (HOCA ve ASİSTAN diyalogları) çalışıyor. Bunu gerçek sese çevirmelisin.

**Yapılacaklar:**
1. `Orka.Infrastructure` içine `EdgeTtsPodcastService.cs` ekle.
2. TutorAgent'tan dönen metni Regex ile `[HOCA]` ve `[ASISTAN]` bloklarına göre ayrıştır.
3. Hoca için kalın bir ses (örn: `tr-TR-AhmetNeural`), Asistan için daha dinamik bir ses (örn: `tr-TR-EmelNeural`) ayarla.
4. İki farklı ses dosyasını bellekte (MemoryStream) birleştirerek tek bir `.wav` veya `.mp3` akışı (Stream) olarak dışarı ver.
5. `ClassroomVoiceController.cs` içine `[GET] {sessionId}/podcast/stream` endpoint'i yazarak frontend'in bunu bir `<audio>` etiketiyle çalmasını sağla.

---

## ADIM 2: STRICT DOCUMENT GROUNDING (KENDİ BELGENLE SOHBET)
Öğrencilerin dış dünyadan soyutlanıp *SADECE* yükledikleri PDF/Word belgesine göre AI ile konuşmasını sağla.

**Yapılacaklar:**
1. **Belge Yükleme Endpoint'i:** `DocumentController.cs` oluştur. `[POST] /upload` endpoint'i ile IFormFile üzerinden PDF kabul et.
2. **Text Extraction:** `PdfPig` veya `iText7` NuGet paketini kur. PDF içindeki metinleri Sayfa (Page) numaralarıyla birlikte oku.
3. **Chunking & Vectorization:** Okunan metni 500 token'lık parçalara (Chunk) böl. `CohereEmbeddingService` (zaten Program.cs'te var) ile vektörle ve Redis Vector veritabanına kaydet.
4. **Strict Context Enjeksiyonu:** `TutorAgent.cs` içindeki `systemPrompt`'a şu kuralı ekle: 
   *"Kullanıcı sana soru sorduğunda SADECE aşağıdaki [BELGE İÇERİĞİ] kısmında verilen bilgileri kullan. Eğer cevap belgede yoksa, 'Bu bilgi belgede yer almıyor' de. Asla dış bilgi kullanma."*

---

## ADIM 3: OTOMATİK SÖZLÜK (GLOSSARY) VE TIMELINE ÜRETİMİ
NotebookLM'deki belgenin ruhunu anlama araçlarını sisteme dahil et.

**Yapılacaklar:**
1. `SummarizerAgent.cs` içerisine `GenerateGlossaryAsync(Guid topicId)` metodu ekle.
2. Model olarak `Groq` (Llama-3.3-70B) kullan. Prompt: *"Verilen Wiki metnindeki veya belgedeki en zor/teknik 10 terimi bul ve JSON olarak {terim, basit_aciklama} formatında dön."*
3. Aynı şekilde tarihsel konular için `GenerateTimelineAsync(Guid topicId)` yaz. *"Metindeki olayları kronolojik olarak {yil, olay} şeklinde JSON dön."*
4. `WikiController.cs`'ten bunları dışarı aç ve Redis'te 24 saat Cache'le (`_glossaryCache`).

---

## ADIM 4: INLINE SOURCE PINNING (METİNDEN KAYNAĞA ZIPLAMA)
AI'ın verdiği cevapların havada kalmamasını, tam olarak PDF'in hangi satırından alındığının kanıtlanmasını sağla.

**Yapılacaklar:**
1. `KorteksAgent` veya `TutorAgent` (Document Grounding modundayken) cevap üretirken her cümlenin sonuna referans ID'si eklemeli: Örn: *"Hücrenin merkezi çekirdektir [page:3]."*
2. Frontend'de bu `[page:X]` taglerini yakalayan bir Markdown Custom Parser yaz (React `react-markdown` plugin).
3. Öğrenci bu linke tıkladığında, React'ta sol taraftaki PDF okuyucu (örneğin `react-pdf`) otomatik olarak 3. sayfaya geçmeli ve ilgili metni sarı (highlight) renge boyamalı.

---

**TEKNİK KURALLAR:**
- Asla yeni bir HttpClient yaratma, `AIAgentFactory` ve `IAIService` üzerinden mevcut yapıları kullan.
- Liyakat kuralı geçerlidir: Sözlük için Groq, Belge analizi için OpenRouter (Claude-Opus), hızlı işlemler için Cerebras kullan.
- Kodlarda `try-catch` kullanmayı ve `_logger` ile hata durumlarını loglamayı unutma.

Hazırsan **ADIM 1 (Edge-TTS Podcast Servisi)** ile kodlamaya başla!
