<div align="center">

# Orka AI

**Kişiselleştirilmiş Öğrenme Orkestratörü**

*Konuyu sen söyle — öğrenme yolunu Orka çizsin.*

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com)
[![React 19](https://img.shields.io/badge/React-19-61DAFB?style=flat-square&logo=react)](https://react.dev)
[![EF Core 8](https://img.shields.io/badge/EF_Core-8.0-1A1A2E?style=flat-square)](https://learn.microsoft.com/ef)
[![Redis](https://img.shields.io/badge/Redis-7-DC382D?style=flat-square&logo=redis&logoColor=white)](https://redis.io)
[![Semantic Kernel](https://img.shields.io/badge/Semantic_Kernel-1.x-512BD4?style=flat-square)](https://learn.microsoft.com/semantic-kernel/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow?style=flat-square)](LICENSE)

</div>

---

## 1. Orka Nedir?

Orka AI, kullanıcının **doğal dilde söylediği herhangi bir konuyu** anlayıp kendisine özel bir müfredat hazırlayan, dersleri akıcı biçimde anlatan, sınav yapan, kod çalıştıran ve öğrendikçe bir **kişisel wiki** dolduran çok-ajanlı (multi-agent) bir AI öğrenme platformudur.

Klasik online kursların aksine Orka:

- **Sabit içerik sunmaz** — her kullanıcıya ve her konuya özel bir yol çizer.
- **Tek bir LLM'e bağımlı değildir** — görevin doğasına göre farklı ajanlar farklı modelleri kullanır.
- **Kendi cevap kalitesini ölçer** — bir `EvaluatorAgent` her anlatımı 3 boyutta puanlar; düşük puanlı cevaplar bir sonraki prompt'ta **ders niteliğinde geri besleme** olarak geri gelir.
- **Öğrenciyi ezberlemez, modeller** — "Öğrenci Profili" (Yaşayan Organizasyon) kullanıcının zayıf noktalarını canlı tutarak anlatımı bunlara göre bükler.

Kısaca: Orka, cevap veren değil, **cevabını sürekli iyileştiren** bir sistemdir.

---

## 2. Organizasyon Yapısı — Yaşayan Bir Organizma

Orka'nın çekirdeği, klasik "iste-cevapla" akışının üzerine kurulmuş bir **kapalı geri besleme döngüsüdür**. Her kullanıcı etkileşimi aşağıdaki zinciri tetikler:

```
Kullanıcı mesajı
      │
      ▼
┌──────────────────────┐
│ AgentOrchestrator    │  ← merkezi yönlendirme (state machine + intent)
└──────────┬───────────┘
           │  (rol + görev)
           ▼
┌──────────────────────┐
│  İş Ajanı            │  Tutor · DeepPlan · Quiz · Wiki · Korteks · ...
│  (cevap üretir)      │
└──────────┬───────────┘
           │  response
           ▼
┌──────────────────────┐
│ EvaluatorAgent       │  → pedagoji / faktual / bağlam (her biri 1-5)
│ (LLMOps Kalite)      │  → overall (1-10) + hallucinationRisk
└──────────┬───────────┘
           │  skor + gerekçe
           ▼
┌──────────────────────────────────────────────┐
│ Redis — "Canlı Hafıza"                       │
│  • orka:feedback:{sessionId}      (son 20)   │ ← session notları
│  • orka:topic_score:{topicId}     (avg)      │ ← konu kümülatif kalite
│  • orka:student_profile:{topicId} (anlık)    │ ← zayıf noktalar, seviye
│  • orka:gold:{topicId}            (≥9 puan)  │ ← başarılı anlatım örneği
│  • orka:metrics:{agentRole}       (TTFT vs.) │ ← LLMOps telemetri
└──────────┬───────────────────────────────────┘
           │  bir sonraki mesajda…
           ▼
┌──────────────────────┐
│ TutorAgent prompt'u  │  ← geri besleme + altın örnek + öğrenci profili
│ (gelişen prompt)     │    enjekte edilir — "dynamic few-shot"
└──────────────────────┘
```

### 2.1 Çok Boyutlu Değerlendirme (RAG-Triad Esintili)

`EvaluatorAgent` her ajan cevabını tek puan yerine **3 boyutta** skorlar:

| Boyut | Ölçek | Ne ölçer |
|---|---|---|
| **pedagogy** | 1–5 | Açıklama öğretici mi, seviyeye uygun mu, gereksiz gevezelik var mı? |
| **factual**  | 1–5 | İçerik doğru mu, uydurma bilgi / hallucination var mı? |
| **context**  | 1–5 | Kullanıcının sorusuyla gerçekten alakalı mı? |

`overall = ((pedagogy + factual + context) / 15) × 10` formülüyle 1–10 arası normalize edilir. `factual < 3` → `hallucinationRisk = true` otomatik.

### 2.2 Üç Katmanlı Hafıza

Orka üç farklı zaman ölçeğinde hafıza tutar:

| Katman | Nerede | Ömür | Amaç |
|---|---|---|---|
| **Mesaj bazlı** | SQL `AgentEvaluation` + `Message` | Kalıcı | Audit log, dashboard |
| **Session bazlı** | Redis `orka:feedback:{sessionId}` | 7 gün | Anlık tonlama, "sade ol" uyarısı |
| **Topic bazlı** | Redis `orka:topic_score:{topicId}` + `student_profile` | 30 gün | Konu kalitesi, öğrenci modeli |

### 2.3 Gelişen Prompt (Dynamic Few-Shot)

`TutorAgent` her cevap öncesi Redis'ten şu dört katmanı okuyup prompt'una enjekte eder:

1. Son 5 **EvaluatorAgent** geri bildirimi (session)
2. Konu için **ortalama kalite puanı** (topic)
3. **Öğrenci Profili** — kavrama seviyesi + zayıf noktalar (topic)
4. Geçmişte ≥9 puan almış **altın örnekler** (topic, max 10)

Böylece aynı konuyu ikinci kez anlatırken sistem zaten daha iyidir — kopya prompt değil, **öğrenen prompt**.

### 2.4 Altın Örnek Kütüphanesi

`EvaluatorAgent` bir `TutorAgent` cevabına 9 veya 10 verdiyse ve `hallucinationRisk=false` ise, bu konuşma `orka:gold:{topicId}` anahtarına kaydedilir. Halüsinasyon riskli yüksek-puanlı cevaplar **kasten** örnek kütüphanesine alınmaz — yanlış bilgi pekişmesin diye.

### 2.5 Sağlayıcı Zinciri + Telemetri

`AIAgentFactory`, her ajan rolü için `appsettings.json`'daki **model yönlendirme tablosuna** bakar. Birincil sağlayıcı çökerse zincir otomatik fallback'e düşer (GitHub Models → Groq → Gemini → OpenRouter → Mistral). Her çağrı `orka:metrics:{role}` anahtarına **TTFT, gecikme, başarı, sağlayıcı** olarak yazılır. Dashboard'da:

- Sağlayıcı dağılımı (Primary ≥ %85 sağlıklı sayılır)
- Ajan başına ortalama gecikme
- Hata oranı & son aktif sağlayıcı

gerçek zamanlı görülebilir.

### 2.6 State Machine — Session Yaşam Döngüsü

```
Learning ──(Analyzer: konu bitti)──► TopicCompleted
    │                                      │
    │                                      ▼
    │                                 QuizPending
    │                                      │
    │                                      ▼
    │                                  QuizMode ──(Grader)──► Learning (bir sonraki)
    │
    └──(yeni konu)──► AwaitingChoice ──► BaselineQuizMode ──► Planning ──► Learning
```

Session state geçişleri **yalnızca** `AgentOrchestratorService`'in `Handle*` metotlarından yapılır — başka yerden müdahale yasak.

---

## 3. Sistem UML — Tam Mimari

```mermaid
graph TB
    subgraph Client["🖥️ Frontend — React 19 + Vite + Tailwind v4"]
        UI[Home · Landing · Courses]
        CP[ChatPanel<br/>SSE consumer]
        WP[WikiMainPanel<br/>polling]
        IDE[InteractiveIDE<br/>Monaco]
        HUD[SystemHealthHUD<br/>admin only]
        API_TS[services/api.ts<br/>Axios + fetch]
    end

    subgraph API[".NET 8 Web API — Orka.API"]
        AUTH[AuthController<br/>JWT + refresh]
        CHAT[ChatController<br/>SSE stream]
        QUIZ[QuizController]
        TOPICS[TopicsController]
        WIKI[WikiController<br/>SSE stream]
        KORT[KorteksController]
        CODE[CodeController<br/>Judge0/Piston]
        DASH[DashboardController<br/>admin HUD]
        HEALTH[HealthController<br/>/health/ready]
        USERC[UserController]
        SKILL[SkillMasteryController]
        MW[CorrelationId +<br/>ExceptionMiddleware]
    end

    subgraph Orchestrator["🧠 Agent Orchestrator — Infrastructure"]
        ORCH[AgentOrchestratorService<br/>state machine + routing]
        ROUTER[RouterService<br/>intent classifier]
        INTENT[IntentClassifierAgent]
        CTX[ContextBuilder]
    end

    subgraph Agents["🤖 AI Agents — Orka.Infrastructure.Services"]
        TUTOR[TutorAgent<br/>anlatım + quiz]
        DEEP[DeepPlanAgent<br/>müfredat]
        ANLZ[AnalyzerAgent<br/>completion detect]
        SUM[SummarizerAgent<br/>wiki idempotent]
        WIKIAG[WikiAgent<br/>Q&A stream]
        KORTEX[KorteksAgent<br/>Tavily + Wiki]
        SUPER[SupervisorAgent]
        GRADER[GraderAgent<br/>quiz puanla]
        EVAL[EvaluatorAgent<br/>3-dim scoring]
        QZA[QuizAgent]
    end

    subgraph SK["Semantic Kernel Plugins"]
        TAV[TavilySearchPlugin]
        WIKIP[WikipediaPlugin]
        TP[TopicPlugin]
        WIKIPL[WikiPlugin]
    end

    subgraph Chain["AI Service Chain — Failover"]
        FAC[AIAgentFactory<br/>role → model]
        CHAIN[AIServiceChain]
        GHM[GitHubModels<br/>Primary]
        GROQ[GroqService]
        GEM[GeminiService]
        MIS[MistralService]
        SAM[SambaNovaService]
        CER[CerebrasService]
        OPR[OpenRouterService]
        COH[CohereEmbeddingService]
    end

    subgraph Data["💾 Persistence"]
        SQL[(SQL Server LocalDB<br/>EF Core 8)]
        REDIS[(Redis 7<br/>StackExchange.Redis)]
    end

    subgraph External["🌐 External APIs"]
        TAVILY[Tavily Search]
        WIKI_EXT[Wikipedia REST]
        JUDGE[Judge0 CE<br/>sandbox exec]
        AIAPI[LLM Providers]
    end

    UI --> API_TS
    CP -->|SSE| CHAT
    WP --> WIKI
    IDE --> CODE
    HUD --> DASH

    API_TS --> AUTH
    API_TS --> TOPICS
    API_TS --> QUIZ
    API_TS --> USERC
    API_TS --> SKILL

    CHAT --> ORCH
    QUIZ --> ORCH
    TOPICS --> ORCH
    WIKI --> WIKIAG
    KORT --> KORTEX
    CODE --> AGENTS_PISTON[PistonService]

    ORCH --> ROUTER
    ORCH --> INTENT
    ORCH --> CTX
    ORCH --> TUTOR
    ORCH --> DEEP
    ORCH --> ANLZ
    ORCH --> SUM
    ORCH --> QZA
    ORCH --> GRADER

    TUTOR --> EVAL
    DEEP --> EVAL
    WIKIAG --> EVAL
    KORTEX --> EVAL
    SUM --> EVAL

    WIKIAG --> SK
    KORTEX --> SK
    SK --> TAV
    TAV --> TAVILY
    WIKIP --> WIKI_EXT
    AGENTS_PISTON --> JUDGE

    TUTOR --> FAC
    DEEP --> FAC
    EVAL --> FAC
    WIKIAG --> FAC
    KORTEX --> FAC
    SUPER --> FAC
    GRADER --> FAC

    FAC --> CHAIN
    CHAIN --> GHM
    CHAIN --> GROQ
    CHAIN --> GEM
    CHAIN --> MIS
    CHAIN --> SAM
    CHAIN --> CER
    CHAIN --> OPR
    GHM --> AIAPI
    GROQ --> AIAPI
    GEM --> AIAPI

    ORCH --> SQL
    EVAL --> REDIS
    TUTOR --> REDIS
    DASH --> REDIS
    DASH --> SQL
    HEALTH --> REDIS
    HEALTH --> SQL

    style ORCH fill:#0f766e,color:#fff
    style EVAL fill:#b45309,color:#fff
    style REDIS fill:#991b1b,color:#fff
    style SQL fill:#1e3a8a,color:#fff
    style FAC fill:#065f46,color:#fff
```

---

## 4. Redis UML — Anahtar Topolojisi

Redis, Orka'nın **canlı hafızasıdır**. Kalıcı veri SQL'dedir; Redis yalnızca **hızla değişen, yüksek TTL'li** öğrenme sinyallerini tutar.

```mermaid
graph LR
    subgraph Writers["✍️ Yazan Servisler"]
        EVAL[EvaluatorAgent]
        ORCH[AgentOrchestrator]
        SUM[SummarizerAgent]
        TUTOR[TutorAgent]
        FAC[AIAgentFactory<br/>telemetri]
        RL[RateLimitMiddleware]
        GRADER[GraderAgent]
        PISTON[PistonService]
    end

    subgraph RedisDB["🔴 Redis DB 0 — StackExchange.Redis"]
        FB["orka:feedback:{sessionId}<br/>📋 LIST · last 20 · TTL 7d<br/>{score, feedback, at}"]

        TS["orka:topic_score:{topicId}<br/>📋 LIST · last 50 · TTL 30d<br/>{score, feedback, at}"]

        SP["orka:student_profile:{topicId}<br/>🔑 STRING · TTL 30d<br/>{understandingScore, weaknesses, at}"]

        GE["orka:gold:{topicId}<br/>📋 LIST · last 10 · TTL 30d<br/>{userMsg, agentResp, score≥9}"]

        MET["orka:metrics:{agentRole}<br/>📋 LIST · last 100 · TTL 24h<br/>{latencyMs, success, provider, at}<br/>roles: Tutor/Eval/Super/Sum/Korteks/Grader/DeepPlan"]

        GP["orka:globalPolicy<br/>🔑 STRING · no expiry<br/>(admin override prompt prefix)"]

        WR["orka:wiki-ready:{topicId}<br/>🔑 STRING · TTL 1h<br/>(frontend polling sinyali)"]

        PS["orka:piston:{sessionId}:last<br/>🔑 STRING · TTL 30m<br/>{code, stdout, stderr, lang}"]

        RLK["orka:rateLimit:{clientIp}<br/>🔢 COUNTER · TTL=window<br/>(INCR + EXPIRE; fail-open)"]
    end

    subgraph Readers["👁️ Okuyan Servisler"]
        TUTOR_R[TutorAgent<br/>prompt enjeksiyonu]
        DASH[DashboardController<br/>System Health HUD]
        HEALTH[HealthController<br/>Redis ping]
        WIKI_C[WikiController<br/>ready sinyali]
        ADMIN[AdminController<br/>policy read]
    end

    EVAL -->|RecordEvaluation| FB
    EVAL -->|RecordTopicScore| TS
    EVAL -->|≥9 & !hall| GE
    ORCH -->|RecordStudentProfile| SP
    SUM -->|SetWikiReady| WR
    FAC -->|RecordAgentMetric| MET
    GRADER -->|RecordAgentMetric| MET
    TUTOR -->|RecordAgentMetric| MET
    PISTON -->|SetLastPistonResult| PS
    RL -->|CheckRateLimit INCR| RLK

    TUTOR_R -->|GetRecentFeedback| FB
    TUTOR_R -->|GetTopicScore| TS
    TUTOR_R -->|GetStudentProfile| SP
    TUTOR_R -->|GetGoldExamples| GE
    TUTOR_R -->|GetLastPistonResult| PS
    TUTOR_R -->|GetGlobalPolicy| GP

    DASH -->|GetSystemMetrics| MET
    DASH -->|GetProviderUsage| MET
    DASH -->|GetRecentEvaluatorLogs| FB
    HEALTH -.->|PING| RedisDB
    WIKI_C -->|HasWikiReady| WR
    ADMIN -->|SetGlobalPolicy| GP

    style RedisDB fill:#7f1d1d,color:#fff
    style EVAL fill:#b45309,color:#fff
    style TUTOR_R fill:#065f46,color:#fff
    style DASH fill:#1e40af,color:#fff
```

**Tasarım Prensipleri:**

- **Key namespace'i** her zaman `orka:<amaç>:<scope-id>` — hem pattern taraması hem silme için.
- **Hiçbir anahtar kalıcı değil** (globalPolicy hariç) — TTL her yazımda yenilenir.
- **Fail-open rate limit** — Redis düşerse sistem kilitlenmez, limit devre dışı kalır.
- **LPUSH + LTRIM** pattern'i — listelerde sonsuz büyüme önlenir, en yeni en üstte.

---

## 5. Database UML — SQL Server Şeması

```mermaid
erDiagram
    User ||--o{ RefreshToken : has
    User ||--o{ Topic : owns
    User ||--o{ Session : owns
    User ||--o{ QuizAttempt : attempts
    User ||--o{ AgentEvaluation : receives
    User ||--o{ SkillMastery : masters
    User ||--o{ WikiPage : owns

    Topic ||--o{ Topic : "parent (DeepPlan)"
    Topic ||--o{ Session : runs
    Topic ||--o{ WikiPage : generates
    Topic ||--o{ SkillMastery : tracks
    Topic ||--o{ QuizAttempt : tested

    Session ||--o{ Message : contains
    Session ||--o{ AgentEvaluation : scored
    Session ||--o{ QuizAttempt : contains

    Message ||--o{ AgentEvaluation : evaluated

    WikiPage ||--o{ WikiBlock : composed
    WikiPage ||--o{ Source : references

    User {
        Guid Id PK
        string Email UK
        string PasswordHash
        UserPlan Plan "Free|Pro"
        bool IsAdmin "admin HUD gate"
        int DailyMessageCount
        DateTime DailyMessageResetAt
        double StorageUsedMB
        double StorageLimitMB
        int TotalXP "gamification"
        int CurrentStreak
        DateTime LastActiveDate
        string Theme
        string Language
        string FontSize
        bool QuizReminders
        bool WeeklyReport
        bool NewContentAlerts
        bool SoundsEnabled
        DateTime CreatedAt
        DateTime LastLoginAt
    }

    RefreshToken {
        Guid Id PK
        Guid UserId FK
        string Token UK
        DateTime ExpiresAt
        bool IsRevoked
        DateTime CreatedAt
    }

    Topic {
        Guid Id PK
        Guid UserId FK
        Guid ParentTopicId FK "self-ref · DeepPlan"
        string Title
        string Emoji
        string Category
        TopicPhase CurrentPhase "Discovery|Assessment|Planning|ActiveStudy|Completed"
        string PhaseMetadata "JSON"
        string LanguageLevel "Beginner|Intermediate|Advanced"
        string LastStudySnapshot
        int Order "deterministic sort"
        int TotalSections
        int CompletedSections
        int SuccessScore "0-100"
        double ProgressPercentage
        bool IsMastered
        bool IsArchived
        DateTime LastAccessedAt
        DateTime CreatedAt
    }

    Session {
        Guid Id PK
        Guid UserId FK
        Guid TopicId FK "nullable"
        int SessionNumber
        string Summary
        SessionState CurrentState "Learning|QuizPending|QuizMode|AwaitingChoice|BaselineQuizMode"
        string PendingQuiz
        string BaselineQuizData "5 soru JSON"
        int BaselineQuizIndex
        int BaselineCorrectCount
        int TotalTokensUsed
        decimal TotalCostUSD
        DateTime CreatedAt
        DateTime EndedAt
    }

    Message {
        Guid Id PK
        Guid SessionId FK
        Guid UserId
        string Role "user|assistant"
        string Content
        MessageType MessageType "Explain|Plan|Quiz|Research|..."
        string Intent
        string PhaseAtTime
        bool IsNewTopic
        string TopicTitle
        string ModelUsed
        int TokensUsed
        decimal CostUSD
        DateTime CreatedAt
    }

    WikiPage {
        Guid Id PK
        Guid TopicId FK
        Guid UserId FK
        string Title
        string Content "nvarchar(max) markdown"
        int OrderIndex
        string Status "pending|ready"
        DateTime CreatedAt
        DateTime UpdatedAt
    }

    WikiBlock {
        Guid Id PK
        Guid WikiPageId FK
        WikiBlockType BlockType "Concept|Complexity|UseCase|Note|Quiz|Sources|UserNote"
        string Title
        string Content
        string Source
        int OrderIndex
        DateTime CreatedAt
        DateTime UpdatedAt
    }

    Source {
        Guid Id PK
        Guid WikiPageId FK
        string Type
        string Title
        string Url
        int DurationMinutes
        bool IsWatched
        DateTime CreatedAt
    }

    QuizAttempt {
        Guid Id PK
        Guid UserId FK
        Guid SessionId FK "nullable"
        Guid TopicId FK "nullable"
        string Question
        string UserAnswer
        bool IsCorrect
        string Explanation
        DateTime CreatedAt
    }

    AgentEvaluation {
        Guid Id PK
        Guid SessionId FK
        Guid UserId FK
        Guid MessageId FK
        string AgentRole "Tutor|DeepPlan|..."
        string UserInput
        string AgentResponse
        int EvaluationScore "1-10"
        string EvaluatorFeedback "[HALL] [F:x P:y C:z] feedback"
        DateTime CreatedAt
    }

    SkillMastery {
        Guid Id PK
        Guid UserId FK
        Guid TopicId FK
        string SubTopicTitle "snapshot"
        int QuizScore "0-100"
        DateTime MasteredAt
    }
```

**Önemli Kısıtlar:**

- `User.Email` — **unique index**
- `RefreshToken.Token` — **unique index**
- `Topic.ParentTopicId` — self-reference (silme zinciri `NoAction` — cascade cycle engeli)
- `Message (SessionId, CreatedAt)` — composite index (chat listesi sıralı okuma)
- `Session (UserId, TopicId)` — composite index
- `Topic (UserId, Order)` — composite index (sidebar sıralama)
- `SkillMastery (UserId, TopicId)` — composite index
- Enum alanları **string** olarak saklanır (`HasConversion<string>()`)
- `decimal` maliyet alanları — `precision(10, 6)`

---

## 6. Teknolojiler & Mimari Desenler

### Backend (.NET 8)

| Katman | Teknoloji |
|---|---|
| Runtime | **.NET 8** (LTS) |
| Web API | **ASP.NET Core 8** Minimal hosting + Controller pattern |
| ORM | **Entity Framework Core 8** (Code-First, auto-migrate at boot) |
| Veritabanı | **SQL Server 2022 LocalDB** (prod: Azure SQL) |
| Cache / Canlı Hafıza | **Redis 7** via `StackExchange.Redis` |
| AI Orchestration | **Microsoft Semantic Kernel** (plugin bazlı) |
| Mediator | **MediatR** — domain event'leri (`TopicCompletedEvent`) |
| Auth | **JWT Bearer** + Refresh Token rotation (60 dk + 30 gün) |
| HTTP Resilience | **Microsoft.Extensions.Http.Resilience** — retry + circuit breaker + timeout |
| Health | `AspNetCore.HealthChecks.Redis` + `AddDbContextCheck` |
| Docs | Swagger / OpenAPI |
| Secrets | `dotnet user-secrets` (appsettings'de API anahtarı yasak) |

### Frontend (React 19)

| Katman | Teknoloji |
|---|---|
| Framework | **React 19** + **TypeScript 5.8** |
| Build | **Vite 6** |
| Styling | **Tailwind CSS v4** (config-less, `@tailwindcss/vite` plugin) |
| Typography | `@tailwindcss/typography` (prose-invert) |
| Router | **wouter** (react-router kullanılmaz) |
| HTTP | **Axios** (tek instance + 401 refresh queue) + native `fetch` (SSE için) |
| State | Local `useState` + React Context (Theme / Language / FontSize / QuizHistory) |
| Animation | **framer-motion** (sayfa/modal) + Tailwind transitions (mikro) |
| Markdown | **react-markdown** + **remark-gfm** + **react-syntax-highlighter** |
| Kod Editör | **@monaco-editor/react** (InteractiveIDE) |
| Icons | **lucide-react** |
| Toast | **react-hot-toast** |
| Layout | **react-resizable-panels** (SplitPane) |

### AI Katmanı

| Ajan / Servis | Rol |
|---|---|
| **AgentOrchestratorService** | Tüm `ProcessMessage*` akışlarının merkezi — state machine + routing |
| **TutorAgent** | Ders anlatımı, cevap değerlendirme |
| **DeepPlanAgent** | Müfredat (hiyerarşik topic tree) üretimi |
| **AnalyzerAgent** | "Konu tamamlandı mı?" kararı |
| **SummarizerAgent** | Wiki markdown üretimi (idempotent, `ConcurrentDictionary` lock) |
| **QuizAgent** / **GraderAgent** | Quiz üretimi + puanlama |
| **WikiAgent** | Wiki üzerine SSE stream Q&A |
| **KorteksAgent** | Tavily + Wikipedia ile grounded deep research |
| **SupervisorAgent** | Meta-kontrol, kritik cevap denetimi |
| **EvaluatorAgent** | **LLMOps kalite — 3 boyutlu skorlama** |
| **IntentClassifierAgent** | Mesajın niyetini (Plan/Explain/Quiz/...) etiketler |
| **RouterService** | Intent → ajan yönlendirme (orchestrator bağımlılığı yasak) |
| **IAIAgentFactory** | Rol → model eşleme + TTFT telemetri |
| **IAIServiceChain** | Provider failover zinciri |
| **TokenCostEstimator** | 2026 fiyat tablosu, her mesaj/session kümülatif |

### LLM Sağlayıcıları (Failover Sıralı)

1. **GitHub Models** (Azure AI Inference) — *Primary*
2. **Groq** — düşük gecikme fallback
3. **Google Gemini 2.5 Flash**
4. **Mistral**
5. **SambaNova**
6. **Cerebras**
7. **OpenRouter** — son çare
8. **Cohere Embed** — embedding (paralel, failover değil)

### Semantic Kernel Plugins

- `TavilySearchPlugin` — web arama (KorteksAgent)
- `WikipediaPlugin` — grounding / halüsinasyon önleme
- `WikiPlugin` — kullanıcının kendi wiki'sinden soru cevaplama
- `TopicPlugin` — topic CRUD AI üzerinden

### Harici Servisler

- **Tavily Search API** — deep research
- **Wikipedia REST API** — fact grounding
- **Judge0 CE** — sandbox kod çalıştırma (Python, C#, JS, Go, Rust, ... 70+ dil)

### Mimari Desenler

- **Clean Architecture** — `Core → Infrastructure → API` tek yönlü bağımlılık
- **Mediator Pattern** — MediatR ile domain event'ler
- **Factory Pattern** — `AIAgentFactory` rol bazlı model seçimi
- **Chain of Responsibility** — `AIServiceChain` provider failover
- **State Machine** — `SessionState` + `TopicPhase` enum driven
- **CQRS-lite** — okuma/yazma servis ayrımı (özellikle Redis)
- **Plugin Pattern** — Semantic Kernel plugin'leri dinamik yüklenebilir
- **Observability** — Correlation ID middleware, structured logging (`ILogger`)
- **Chaos Engineering** — `X-Chaos-Fail` header ile provider başarısızlığı simülasyonu
- **Rate Limiting** — Redis Token Bucket (IP + Plan bazlı)
- **Fail-Open Security** — Redis/cache çökse de sistem çalışmaya devam eder
- **SSE (Server-Sent Events)** — chat + wiki akışı için; özel sinyaller: `[THINKING:]`, `[PLAN_READY]`, `[TOPIC_COMPLETE:guid]`, `[ERROR]:`, `[DONE]`

### Test & Kalite

- **LLM Eval** — `promptfoo` (scenario-based, LLM-as-judge)
- **Healthcheck** — `scripts/healthcheck.mjs` (Node.js, cross-platform, PASS/FAIL/BONUS raporu)
- **Type Check** — `dotnet build` + `npx tsc --noEmit` (commit öncesi zorunlu)

---

## 7. Kurulum

### Ön Gereksinimler

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Node.js 18+](https://nodejs.org)
- SQL Server LocalDB (Visual Studio ile gelir)
- Redis 7 — yerel instance veya Docker (`docker run -p 6379:6379 redis:7`)

### Backend

```bash
# API anahtarlarını user-secrets'a ekle
cd Orka.API
dotnet user-secrets set "AI:GitHubModels:Token" "ghp_..."
dotnet user-secrets set "AI:Groq:ApiKey" "gsk_..."

# Migration otomatik uygulanır — direkt çalıştır
dotnet run
# → http://localhost:5065
```

### Frontend

```bash
cd Orka-Front
npm install
npm run dev
# → http://localhost:5173
```

### Sağlık Kontrolü

```bash
# Backend ayaktayken
node scripts/healthcheck.mjs
```

### Admin Yapma

```bash
sqlcmd -S "(localdb)\mssqllocaldb" -d OrkaDb -i promote_admin.sql
```

---

## 8. Lisans

[MIT](LICENSE) — Ahmet Akif Sevgili
