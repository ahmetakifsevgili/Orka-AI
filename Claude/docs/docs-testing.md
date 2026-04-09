# docs/testing.md — Orka Senaryo Bazlı Test ve Doğrulama Protokolü

Orka'da test süreci, sadece fonksiyonların çalışıp çalışmadığını değil; "Diyalog Yöneticisi"nin (Brain) ne kadar başarılı bir mentorluk yaptığını ölçer.

---

## 🎭 Kritik Davranış Senaryoları (The Golden Paths)

Sistem aşağıdaki senaryolardan başarıyla geçmelidir:

### 1. Niyet Arama ve Keşif (Discovery Test)
- **Girdi:** "Selam, Python öğrenmek istiyorum."
- **Beklenen Davranış:** AI pat diye plan üretmemeli. "Python harika! Seviyeni ölçerek sana özel bir plan mı yapalım yoksa direkt sohbet mi edelim?" diye sormalıdır.

### 2. Konu Değişimi ve Bağlam (Context Switching Test)
- **Durum:** C dili çalışılıyor.
- **Girdi:** "Şimdi biraz da Osmanlı Tarihi konuşalım."
- **Beklenen Davranış:** AI, mevcut C çalışmasını güvenli bir şekilde (kaydederek) kapatmayı teklif etmeli ve yeni konunun kurulum fazına (Phase: Setup) geçmelidir.

### 3. Seviye Analizi ve Plan Sentezi (Assessment Test)
- **Girdi:** "Seviyemi ölç."
- **Beklenen Davranış:** AI, o konuyla ilgili 3-5 tane stratejik soru sormalı, cevapları analiz etmeli ve "Seviyeni Başlangıç olarak belirledim, işte senin için hazırladığım plan..." diyerek planı sunmalıdır.

---

## 🧪 Teknik Test Katmanları

- **Unit Tests (.NET):** `ChatService`'in state geçişlerini (Phase transitions) hatasız yaptığı doğrulanmalıdır.
- **Integration Tests:** AI'dan dönen JSON yanıtların `TopicData` alanına doğru işlendiği kontrol edilmelidir.
- **Diyalog Testleri (Manual):** AI'nın "sa" gibi kelime hataları yapıp yapmadığı gerçek mesajlarla test edilmelidir.

---

## 🚫 GEÇERSİZ TEST SONUÇLARI

- AI, kullanıcıyı sormadan bir sürece sokuyorsa test **FAILED**.
- AI, "osmanlı" mesajına "selam" diyorsa test **FAILED** (Keyword collision).
- Wiki güncellenmiyorsa test **FAILED**.

---
> Testler, Orka'nın "şişirme balon" olmadığını, her adımda ne yaptığını bilen bir otomasyon olduğunu kanıtlar.
