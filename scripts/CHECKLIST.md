# Orka — Deploy Sonrası Doğrulama Kılavuzu

Kod fix'leri sonrası yapılması gerekenleri tek yerde toplar. Backend
restart + healthcheck + runtime doğrulama + opsiyonel LLM eval.

## 1) Backend Restart (zorunlu)

Yeni agent davranışları (SummarizerAgent kişiselleştirme, TutorAgent coding
kuralı, AgentOrchestratorService guard'ları) ancak yeniden başlatma
sonrası devreye girer.

```bash
# Çalışan backend terminalinde: Ctrl+C
cd D:/Orka
dotnet run --project Orka.API
```

`Now listening on: http://localhost:5065` görünene kadar bekle.

## 2) Healthcheck (zorunlu)

```bash
cd D:/Orka
node scripts/healthcheck.mjs
```

Hedef: **145/145 PASS, Grade A**. JSON + Markdown raporlar
`scripts/reports/` altına yazılır.

## 3) Runtime Doğrulama (manuel test)

### (a) Admin Sekmesi Görünüyor mu
- Login ol → üst menüde "Sistem Analitiği" sekmesi var mı?
- Yoksa hesabı admin yap:
  ```bash
  sqlcmd -S "(localdb)\mssqllocaldb" -d OrkaDb -i promote_admin.sql
  ```
- Logout → tekrar login → sekme gelmeli.

### (b) Wiki Kişiselleştirmesi
1. Yeni bir konu başlat.
2. Quiz'de kasıtlı **1-2 yanlış** ver.
3. Konu tamamlanınca wiki üret.
4. Wiki'de zayıf noktana özel vurgu/tekrar bölümü olmalı.
5. Yoksa backend log'unda `[Summarizer]` satırlarını ve profil
   okuma davranışını kontrol et.

### (c) Coding Sorusu Son Index'te mi
1. Programlama konusu başlat, quiz'i getir.
2. Coding sorusu varsa **en sondaki** olmalı.
3. Ortada görürsen:
   - UI'da QuizCard sort fallback sonunda gösterecek → yine ekranda
     en sonda olur.
   - Ama bu LLM prompt itaatsizliği işaretidir → prompt kuralını tekrar
     güçlendirmek gerekebilir.

### (d) Auto-Progression Çift Tetiklenmiyor mu
1. Bir konuyu quiz geçerek bitir.
2. Backend log'unda tek bir `[TOPIC_COMPLETE:` ve tek bir
   `[Summarizer]` satırı olmalı.
3. İki kere basıyorsa guard çalışmamış → `HandleTopicProgressionAsync`
   kontrol edilmeli.

## 4) LLM-Eval (Opsiyonel — promptfoo)

> Not: Windows'ta `better-sqlite3` native build sorunu çıkabilir. 3 yoldan
> biri:

### Seçenek A — En hızlı (native build atla)
```bash
cd D:/Orka/scripts/llm-eval
rm -rf node_modules package-lock.json
npm install --ignore-scripts

# Windows PowerShell:
$env:PROMPTFOO_DISABLE_CACHE="true"
npx promptfoo eval --env-file .env.local
```

### Seçenek B — Node 20 LTS'e düş (temiz)
```bash
nvm install 20.18.0
nvm use 20.18.0
cd D:/Orka/scripts/llm-eval
rm -rf node_modules package-lock.json
npm install
```

### Seçenek C — VS Build Tools kur (kalıcı)
```bash
winget install Microsoft.VisualStudio.2022.BuildTools --override "--add Microsoft.VisualStudio.Workload.VCTools --includeRecommended --quiet"
```
Terminali yeniden aç → `npm install` sorunsuz çalışır.

### Eval Koşma (A/B/C'den biri bitince)
```bash
cd D:/Orka/scripts/llm-eval

# JWT hazırla (orka_eval_runner@orka.ai)
node prepare-token.mjs

# Groq anahtarını user-secrets'tan al:
# cd D:/Orka/Orka.API && dotnet user-secrets list
# .env.local'a ekle:
#   GROQ_API_KEY=gsk_...

npx promptfoo eval --env-file .env.local
npx promptfoo view   # tarayıcıda sonuç raporu
```

Hedef eşikler (`.claude/rules/testing.md`):
- LLMOps avg score: ≥ 7.0/10
- Primary provider ratio: ≥ 85%

## 5) Hızlı Komut Özeti (kopyala-yapıştır)

```bash
# Backend
cd D:/Orka && dotnet run --project Orka.API

# Healthcheck
node scripts/healthcheck.mjs

# Frontend (ayrı terminal)
cd D:/Orka/Orka-Front && npm run dev

# Admin promote (gerekirse)
sqlcmd -S "(localdb)\mssqllocaldb" -d OrkaDb -i promote_admin.sql

# User-secrets (API anahtarları)
cd D:/Orka/Orka.API && dotnet user-secrets list

# Migration ekle
cd D:/Orka/Orka.Infrastructure && dotnet ef migrations add <İsim> --startup-project ../Orka.API
```

## 6) Bu Oturumda Yapılan Fix'ler (referans)

**Backend:**
- `AgentOrchestratorService.HandleTopicProgressionAsync` — çift
  `CompletedSections` increment guard (son AI mesajında
  `[TOPIC_COMPLETE:` sniff)
- `AgentOrchestratorService.HandleQuizModeAsync` — IDE kod cevabı
  (`**Quiz Sorusu:**` + ` ``` ` pattern) algılama ve `EvaluateQuizAnswerAsync`
  üzerinden değerlendirme
- `AgentOrchestratorService` — raw JSON array quiz detection
  (`[{...}]` pattern) `MessageType.Quiz` işaretlemesi
- `SummarizerAgent` — `IRedisMemoryService` inject, öğrenci profili
  (weakness) + son 5 yanlış `QuizAttempts` wiki prompt'una enjekte
- `TutorAgent.GenerateTopicQuizAsync` — coding sorusu yalnızca son
  index'te + en fazla 1 tane kuralı prompt'a eklendi

**Frontend:**
- `quizParser.ts` — `type` alanı korunuyor (coding sorular için şart)
- `QuizCard.tsx` — palet ihlalleri (green/red/blue → emerald/amber),
  coding-sort fallback (render öncesi sort)
- `InteractiveIDE.tsx` — palet ihlalleri (violet/blue/red → amber)
- `DashboardPanel.tsx` — `bg-gradient-to-br` kaldırıldı
- `SettingsPanel.tsx` — bozuk useEffect wrapper onarıldı
- `ChatMessage.tsx` — `quizData.topic` öncesi `Array.isArray` guard
- `lib/types.ts` — `quiz?: QuizData | QuizData[]` tip genişletmesi

**Yeni Dosyalar:**
- `scripts/llm-eval/promptfooconfig.yaml` (7 senaryo + LLM-as-judge)
- `scripts/llm-eval/prepare-token.mjs`
- `scripts/llm-eval/package.json`
- `scripts/llm-eval/README.md`
- `scripts/llm-eval/.gitignore`
