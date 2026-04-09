# Skills Klasörü

## Bu Klasör Ne İçin?

Skills, Claude Code'un tekrar tekrar yapması gereken işlemler için
hazır kod şablonları içerir. Bir işlem yapılacaksa önce ilgili
skill dosyasını oku, o pattern'i birebir uygula.

---

## Mevcut Skills

| Dosya | Ne Zaman Kullanılır |
|-------|---------------------|
| `filter-removal.md` | Frontend filtreleri kaldırılacaksa |
| `add-ai-service.md` | Yeni AI API eklenecekse |
| `wiki-block.md` | WikiBlock oluşturulacaksa |
| `topic-plan.md` | Yeni konu + plan oluşturulacaksa |

---

## Nasıl Kullanılır?

```
Örnek: WikiBlock oluşturmam lazım
→ skills/wiki-block.md oku
→ Oradaki pattern'i birebir uygula
→ Değiştirme, sadece uygula
```

---

## Öncelik Sırası

Bir iş yapılacaksa:
1. `CLAUDE.md` oku
2. `docs/` içindeki ilgili dosyayı oku
3. `skills/` içindeki ilgili dosyayı oku
4. Kodu yaz
