# docs/api-endpoints.md — Orka İletişim Protokolü (API)

Bu doküman, Orka'nın "Dinamik Diyalog" mimarisine hizmet eden tüm API endpoint'lerini ve onların "Durum Bilgisi" (Phase) içeren yeni yapısını tanımlar.

---

## 💬 Chat API (Ana İletişim Hattı)

### POST `/api/chat/send`
Kullanıcının her türlü mesajını (selam, konu belirtme, test cevabı, ders devamı) karşılar.

**İstek (Request):**
```json
{
  "content": "C# öğrenmek istiyorum",
  "topicId": "optional-guid",
  "sessionId": "optional-guid"
}
```

**Yanıt (Response - Zeki Yapı):**
```json
{
  "content": "C# harika! 😊 Plan mı yapalım yoksa sadece üzerine mi konuşalım?",
  "currentPhase": "Setup",
  "suggestedActions": ["Plan Yap", "Sohbet Et"],
  "wikiUpdated": false,
  "isTopicSwitch": false
}
```

---

## 📚 Wiki & Topic API

### GET `/api/topics/{id}`
Konunun tüm detaylarını, Wiki sayfalarını ve AI'nın o konuyla ilgili çıkardığı **"Seviye Bilgisini"** döner.

### GET `/api/wiki/{pageId}/reinforcements`
O Wiki sayfasıyla ilgili AI tarafından üretilmiş **"Pekiştirme Soruları"**nı çeker.

---

## 🚦 Endpoint Yasaları

1.  **Daima [Authorize]:** Giriş yapmamış hiçbir kullanıcı AI ile konuşamaz.
2.  **Phase Bilgisini Dön:** Her chat cevabı, o anki `CurrentPhase` bilgisini frontend'e bildirmek zorundadır.
3.  **Hata Kodu Değil, Fallback Mesajı:** Sistem hatası olsa bile kullanıcıya "Şu an bağlantı kuramadım, tekrar dener misin?" gibi samimi bir AI mesajı dönülmeli, JSON hatası fırlatılmamalıdır.

---
> Orka API, basit bir veri yolu değil; mentorun sesidir. Her endpoint, mentorun hafızasını (DB) ve zihnini (AI) yansıtmalıdır.
