# Orka AI — Claude Code & AI Asistan Anayasası

Orka AI, 12-ajanlı (Swarm) bir eğitim platformudur. Bu belge, bu projeye dokunan tüm yapay zeka asistanlarının (Claude, Cursor, Copilot) uyması gereken **katı davranış kurallarını** içerir.

## 👑 1. AI DAVRANIŞ BİÇİMİ (Persona & Kurallar)

1. **Kök Nedeni Çöz (Root-Cause First):** "Yama yama" (patch-by-patch) iş yapma. Bir dosyayı değiştiriyorsan, o dosyanın tetiklediği tüm zinciri (call-graph) analiz et. İzole `catch` blokları veya `null` check'lerle hata susturma.
2. **Mimariyi Koru:** Yeni bir özellik eklerken var olan 12-ajanlı yapıyı (`ORKA_MASTER_GUIDE.md`) bozma. Yeni ajan eklerken daima `AIAgentFactory` üzerinden geçir.
3. **Konuşma, Kod Yaz (No Yapping):** Uzun uzun "şöyle yaptım, böyle yaptım" deme. Sorunu tespit et ve **eksiksiz tam kod bloğunu** ver. Kodları `// ... (mevcut kodlar)` şeklinde bölme, kullanıcının kopyala-yapıştır yapabileceği tam dosyayı veya tam fonksiyonu ver.
4. **Token Ekonomisi:** Yalnızca senden istenen dosyayı oku. `ls`, `tree` gibi komutlarla tüm dizini tarama.

## 🧭 2. KURAL YÜKLEME HARİTASI (Path-Scoped Rules)

Projede farklı klasörlere dokunduğunda aşağıdaki kurallar otomatik yüklenir. İki farklı katmana dokunuyorsan her ikisini de oku.

| Kural Dosyası | İlgili Katmanlar (Globs) | Odak Noktası |
|---|---|---|
| `backend.md` | `Orka.API/**`, `Orka.Core/**` | .NET 8 API, DI, Exception Middleware, MediatR |
| `database.md`| `Orka.Infrastructure/Data/**`, `Entities/**` | EF Core, SQL Server LocalDB, Redis, Migrations |
| `frontend.md`| `Orka-Front/src/**` | React 19, Tailwind v4, Component kısıtlamaları |
| `agents.md`  | `Services/*Agent*.cs`, `SemanticKernel/**` | LLM Failover, Evaluator Skorlaması, Swarm Mantığı |

## 🏗️ 3. REFERANS DOKÜMANLAR

- **Sistem Mimarisi ve Karakter:** `docs/architecture/ORKA_MASTER_GUIDE.md`
- **UML Haritası:** `docs/architecture/ORKA_SYSTEM_ARCHITECTURE.md`
- **Tam Sağlık Testi:** `node scripts/healthcheck.mjs`
- **Admin Yetkisi Verme:** `sqlcmd -S "(localdb)\mssqllocaldb" -d OrkaDb -i scripts/promote_admin.sql`

## 🚀 4. GİT STANDARTLARI
Tüm commit mesajları "Semantic Commit" standardına uyacaktır:
- `feat:` (Yeni özellik)
- `fix:` (Hata düzeltmesi, uçtan uca test edilmiş)
- `chore:` (Temizlik, konfigürasyon, ruleset)
- `refactor:` (Kod yapısı değişimi)
