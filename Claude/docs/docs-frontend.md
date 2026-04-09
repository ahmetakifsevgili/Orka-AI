# docs/frontend.md — Orka Görünmez Arayüz (Invisible UI) Yasaları

Orka'nın arayüzü, karmaşık butonlar ve filtreler için değil; saf bir diyalog ve öğrenme deneyimi için tasarlanmıştır.

---

## 🎨 Tasarım Felsefesi: "Mentor Odaklılık"
Arayüz, kullanıcının dikkatini dağıtacak her türlü gereksiz öğeden arındırılmalıdır.
- **Buton Yok, Diyalog Var:** "Yeni Konu" butonu yerine, "Neyi öğrenmeye başlayalım?" diyen bir chat kutusu vardır.
- **Filtre Yasaktır:** Model seçimi, mode seçimi (quiz, research vb.) frontend'de asla gösterilmez.

---

## 💬 Chat Deneyimi (The Conversation)

- **Proaktif Öneri Kartları:** AI, "Plan mı yapalım sohbet mi?" diye sorduğunda, chat içinde tıklanabilir küçük öneri butonları (Bubble) çıkabilir.
- **Durum (Phase) Göstergeleri:** Chat başlığında hafif bir ibareyle hangi fazda olduğumuz (Örn: "Seviye Belirleniyor...", "C# Çalışılıyor...") gösterilebilir.
- **Toast Bildirimleri:** Kritik her olay (Wiki güncellendi, Plan hazırlandı) sağ alt köşedeki premium toast mesajlarıyla kullanıcıya bildirilmelidir.

---

## 📚 İnteraktif Wiki (The Living Library)

Wiki, sadece metin okunan bir yer değil; interaktif bir çalışma masasıdır.
- **Auto-Sync:** Chat'te konuşulan her şey, kullanıcı sayfayı yenilemeden Wiki'ye "akmalıdır".
- **Pekiştirme Alanı:** Her sayfanın sonunda, AI'nın hazırladığı interaktif "Pekiştirme Soruları" yer alır. Kullanıcı burayı çözdüğünde AI chat üzerinden tebrik eder.

---

## 🚦 Frontend Kuralları (Laws)

1.  **Hız (Speed):** UI her zaman 100ms'nin altında tepki vermelidir.
2.  **Sadelik:** Sadece bir Sidebar (Topics) ve bir Main Content (Chat & Wiki) yapısı korunacaktır.
3.  **Hata Yönetimi:** İnternet koptuğunda veya AI geç cevap verdiğinde "Yapay zeka şu an derin düşüncelerde..." gibi samimi yükleme animasyonları gösterilmelidir.

---
> Orka UI, teknolojiyi saklayıp "insanı" ön plana çıkaran bir sessiz asistan olmalıdır.
