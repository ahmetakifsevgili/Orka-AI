# docs/database.md — Orka Veritabanı ve Hafıza Şeması (Memory Schema)

Orka'nın veritabanı sadece veri saklamaz; sistemin "hafızası" olarak görev yapar. AI, her mesajda bu şemadaki metadata alanlarını okuyarak bağlamı hatırlar.

---

## 🏗️ Temel Hafıza Tabloları

### 1. Topic (Konu ve Durum Hafızası)
Her konu, kendi başına yaşayan bir "Öğrenme Serüveni"dir.

| Kolon | Tip | Açıklama |
|-------|-----|----------|
| `LanguageLevel` | String | `Beginner`, `Intermediate`, `Advanced` (Assessment sonrası set edilir). |
| `LastStudySnapshot` | String | **Ders Durum Kaydı.** En son hangi adımda kalındığı ve anahtar kavramların kısa özeti burada tutulur. |

### 🛠️ Metadata JSON Şeması (Katı Kural)
`PhaseMetadata` alanı rastgele değil, şu şemada olmalıdır:
```json
{
  "current_goal": "Konuyu anlama",
  "assessment_results": { "score": 80, "questions_answered": 5 },
  "active_step_index": 2,
  "interrupted_at_phase": null
}
```

**Yasa:** AI, `CurrentPhase`'e bakmadan asla bir sonraki adımı tetikleyemez.

### 2. Message (Diyalog Geçmişi)
| Kolon | Tip | Açıklama |
|-------|-----|----------|
| `Intent` | String | AI'nın o mesaj için belirlediği niyet (örn: `explain`). |
| `PhaseAtTime` | String | Mesajın gönderildiği andaki konu fazı. |

---

## 📚 Interaktif Wiki Şeması

Wiki, artık statik bir döküman değildir.

### WikiPage & WikiBlock
- **Reinforcement Block:** Her `WikiPage` tamamlandığında, sonuna Mistral tarafından üretilen 3-5 adet **"Pekiştirme Sorusu" (Quiz/Note)** eklenmek zorundadır.
- **Status:** `pending`, `learning`, `completed`, `review`.

---

## ⚙️ .NET Entity Yapısı (Örnek)

```csharp
public class Topic {
    public Guid Id { get; set; }
    public string Title { get; set; }
    public TopicPhase CurrentPhase { get; set; } = TopicPhase.Discovery;
    
    // JSON Hafıza Alanı
    public string? PhaseMetadata { get; set; } 
}
```

## 🚨 KRİTİK VERİ KURALLARI
1.  **Metadata Kaydı:** Assessment (seviye ölçme) bittiği an, analiz sonucu `PhaseMetadata` içine `JSON` olarak gömülmeli ve `Planner` bu veriyi kullanarak plan sentezlemelidir.
2.  **Otomatik Temizlik:** 30 günden eski pasif session'lar sistem tarafından otomatik olarak "Arşiv" olarak işaretlenmeli ama memory silinmemelidir.

---
> Orka'nın hafızası, onun mentörlük gücünün kaynağıdır. Şema değişikliği yapılmadan önce bu doküman güncellenmelidir.
