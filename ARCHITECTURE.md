# Orka AI — Kapsamlı Sistem UML Diyagramı & Kopukluk Analizi

---

## 1. Sistem Bileşen Diyagramı (Component Diagram)

```mermaid
graph TB
    subgraph Frontend["🖥️ Frontend (React + Vite)"]
        UI_Chat["ChatPanel"]
        UI_IDE["InteractiveIDE"]
        UI_Wiki["WikiMainPanel"]
        UI_Dashboard["DashboardPanel"]
        UI_Sidebar["LeftSidebar"]
        UI_Korteks["ResearchLibraryPanel"]
        UI_Settings["SettingsPanel"]
    end

    subgraph API["🌐 API Layer (ASP.NET Core)"]
        ChatCtrl["ChatController /api/chat"]
        CodeCtrl["CodeController /api/code"]
        WikiCtrl["WikiController /api/wiki"]
        KorteksCtrl["KorteksController /api/korteks"]
        TopicCtrl["TopicController /api/topics"]
        DashCtrl["DashboardController /api/dashboard"]
        QuizCtrl["QuizController /api/quiz"]
        AuthCtrl["AuthController /api/auth"]
    end

    subgraph Orchestrator["🧠 Orchestrator (State Machine)"]
        AGO["AgentOrchestratorService"]
    end

    subgraph Agents["🤖 Agent Swarm (12 Ajan)"]
        direction LR
        TUTOR["TutorAgent"]
        SUPERVISOR["SupervisorAgent"]
        INTENT["IntentClassifierAgent"]
        ANALYZER["AnalyzerAgent"]
        EVALUATOR["EvaluatorAgent"]
        GRADER["GraderAgent"]
        DEEPPLAN["DeepPlanAgent"]
        WIKI["WikiAgent"]
        SUMMARIZER["SummarizerAgent"]
        KORTEKS["KorteksAgent"]
        QUIZ["QuizAgent"]
        PROFILE["StudentProfileService"]
    end

    subgraph LLM["☁️ LLM Provider Chain"]
        CHAIN["AIServiceChain"]
        FACTORY["AIAgentFactory"]
        ROUTER["RouterService"]
        GH["GitHubModels"]
        GROQ["Groq"]
        GEMINI["Gemini"]
        OR["OpenRouter"]
        MISTRAL["Mistral"]
    end

    subgraph Data["💾 Data Stores"]
        DB["PostgreSQL (EF Core)"]
        REDIS["Redis"]
        PISTON["Judge0 CE (Sandbox)"]
    end

    UI_Chat --> ChatCtrl
    UI_IDE --> CodeCtrl
    UI_Wiki --> WikiCtrl
    UI_Korteks --> KorteksCtrl
    UI_Dashboard --> DashCtrl

    ChatCtrl --> AGO
    CodeCtrl --> PISTON
    CodeCtrl --> REDIS

    AGO --> SUPERVISOR
    AGO --> TUTOR
    AGO --> DEEPPLAN
    AGO --> EVALUATOR
    AGO --> SUMMARIZER
    AGO --> ANALYZER

    SUPERVISOR --> INTENT
    ANALYZER --> INTENT
    TUTOR --> GRADER
    TUTOR --> REDIS
    DEEPPLAN --> GRADER
    KORTEKS --> GRADER

    INTENT --> FACTORY
    TUTOR --> CHAIN
    EVALUATOR --> FACTORY
    GRADER --> FACTORY
    DEEPPLAN --> FACTORY
    SUMMARIZER --> FACTORY
    KORTEKS --> CHAIN

    CHAIN --> ROUTER
    ROUTER --> GH
    ROUTER --> GROQ
    ROUTER --> GEMINI
    ROUTER --> OR

    AGO --> DB
    WIKI --> DB
    EVALUATOR --> REDIS
    TUTOR --> REDIS
    AGO --> REDIS
```

---

## 2. Session State Machine (Durum Makinesi)

```mermaid
stateDiagram-v2
    [*] --> Learning : Yeni Session

    Learning --> QuizPending : Analyzer "UNDERSTOOD" + Quiz tetik
    Learning --> QuizMode : "anladım" / "kavradım" / Supervisor AI
    Learning --> AwaitingChoice : /plan komutu
    Learning --> PlanDiagnosticMode : Plan teyit

    AwaitingChoice --> BaselineQuizMode : "Evet, plan yap"
    AwaitingChoice --> Learning : "Hayır, sohbet devam"

    BaselineQuizMode --> Learning : 20 soru bitti → Plan oluşturuldu
    PlanDiagnosticMode --> BaselineQuizMode : Hedef onaylandı

    QuizPending --> QuizMode : Quiz üretildi (TutorAgent)
    QuizMode --> Learning : Quiz geçti (≥%60)
    QuizMode --> RemedialOfferPending : Quiz kaldı (<%60)

    RemedialOfferPending --> Learning : Telafi dersi kabul/red
    
    Learning --> TopicCompleted : Tüm alt konular bitti
    TopicCompleted --> [*]
```

---

## 3. Ana Mesaj Akışı (Sequence Diagram)

```mermaid
sequenceDiagram
    participant Ö as Öğrenci
    participant FE as Frontend
    participant API as ChatController
    participant AGO as Orchestrator
    participant SUP as SupervisorAgent
    participant INT as IntentClassifier
    participant TUT as TutorAgent
    participant EVL as EvaluatorAgent
    participant ANL as AnalyzerAgent
    participant RED as Redis
    participant DB as PostgreSQL

    Ö->>FE: Mesaj yazar
    FE->>API: POST /api/chat/stream
    API->>AGO: ProcessMessageStreamAsync

    Note over AGO: State Check
    alt Learning State
        AGO->>SUP: DetermineActionRouteAsync
        SUP->>INT: ClassifyAsync (LLM Call #1)
        INT-->>SUP: {intent, confidence}
        SUP-->>AGO: "TUTOR" / "QUIZ" / "CONFUSED"
        
        AGO->>TUT: GetResponseStreamAsync
        TUT->>RED: FetchPerformanceProfile
        TUT->>RED: FetchPistonContext
        TUT->>RED: GetGoldExamples
        TUT-->>AGO: Stream<string>
        AGO-->>API: SSE Stream
        API-->>FE: Chunks
        
        Note over AGO: Background Tasks
        AGO->>EVL: EvaluateInteractionAsync (Task.Run)
        EVL->>RED: RecordEvaluation
        AGO->>ANL: AnalyzeCompletionAsync (Task.Run)
        ANL->>INT: ClassifyAsync (Cache HIT!)
        INT-->>ANL: cached result
        ANL-->>AGO: {isComplete}
        AGO->>RED: RecordStudentProfile

    else QuizMode
        AGO->>TUT: EvaluateQuizAnswerAsync
        Note over AGO: IDE? → Redis Piston result eklenir
        TUT-->>AGO: {score, feedback}
        alt score ≥ 0.6
            AGO->>DB: CompletedSections++
            AGO->>AGO: TransitionToNextTopic
        else score < 0.6
            AGO-->>Ö: "Telafi dersi ister misin?"
        end

    else BaselineQuizMode
        AGO->>AGO: HandleBaselineQuizMode
        Note over AGO: 20 soruluk seviye testi
        AGO->>DB: Plan oluştur (DeepPlanAgent)
    end
```

---

## 4. IDE + Piston Akışı

```mermaid
sequenceDiagram
    participant Ö as Öğrenci
    participant IDE as InteractiveIDE
    participant API as CodeController
    participant J0 as Judge0 CE
    participant RED as Redis
    participant Chat as ChatPanel
    participant AGO as Orchestrator
    participant TUT as TutorAgent

    Ö->>IDE: Kod yazar
    IDE->>API: POST /api/code/run {code, language, sessionId}
    API->>J0: ExecuteAsync
    J0-->>API: {stdout, stderr, success}
    API->>RED: SetLastPistonResult (başarı+hata)
    API->>RED: RecordAgentMetric("IDE_Piston")
    API-->>IDE: {stdout, stderr, success}
    IDE-->>Ö: Terminal output gösterilir

    Ö->>IDE: "Hocaya Gönder" tıklar
    IDE->>Chat: onSendToChat(kod + quiz sorusu + dil)
    Chat->>AGO: ProcessMessageStream
    AGO->>RED: GetLastPistonResult
    RED-->>AGO: {code, stdout, stderr}
    AGO->>TUT: EvaluateQuizAnswerAsync(kod + [PISTON SONUCU])
    TUT-->>AGO: {score, feedback}
    AGO-->>Chat: Değerlendirme sonucu
```

---

## 5. Korteks Araştırma Akışı

```mermaid
sequenceDiagram
    participant Ö as Öğrenci
    participant FE as ResearchLibrary
    participant API as KorteksController
    participant KA as KorteksAgent
    participant GR as GraderAgent
    participant RED as Redis
    participant Web as Web Scraping
    participant LLM as AIServiceChain

    Ö->>FE: Araştırma başlat
    FE->>API: POST /api/korteks/start
    API->>KA: StartResearchAsync (Background Job)
    
    KA->>Web: Brave Search API
    Web-->>KA: URLs + snippets
    KA->>Web: Scrape top sources
    Web-->>KA: Raw content
    KA->>GR: IsContextRelevantAsync (goalContext)
    GR-->>KA: relevant? true/false
    KA->>LLM: Synthesize report
    LLM-->>KA: Markdown report
    KA->>RED: SetKorteksResearchReport
    KA-->>API: Job complete
    
    Note over FE: Polling ile status check
    FE->>API: GET /api/korteks/status/{jobId}
    API-->>FE: {phase, result}
```

---

## 6. Plan Mode (Deep Plan) Akışı

```mermaid
sequenceDiagram
    participant Ö as Öğrenci
    participant AGO as Orchestrator
    participant DP as DeepPlanAgent
    participant SP as StudentProfileService
    participant GR as GraderAgent
    participant DB as PostgreSQL

    Ö->>AGO: "KPSS Matematik çalışmak istiyorum"
    AGO->>AGO: State → PlanDiagnosticMode
    AGO->>SP: BuildExamScaffolding(user, "KPSS")
    SP-->>AGO: [SINAV ODAKLI MÜFREDAT — KPSS]
    
    AGO->>AGO: State → AwaitingChoice
    AGO-->>Ö: "Plan oluşturayım mı?"
    Ö->>AGO: "Evet"
    
    AGO->>DP: GenerateBaselineQuizAsync("KPSS Matematik")
    DP-->>AGO: 20 soruluk JSON quiz
    AGO->>AGO: State → BaselineQuizMode
    
    loop 20 Soru
        AGO-->>Ö: Soru göster
        Ö->>AGO: Cevap
        AGO->>AGO: Skoru kaydet
    end
    
    AGO->>DP: GenerateCurriculumAsync(başarı oranı, hedef)
    DP->>SP: SuggestLessonCountRange(user, "KPSS")
    SP-->>DP: {min: 80, max: 150}
    DP->>GR: IsContextRelevantAsync
    DP-->>AGO: Modül + Ders yapısı
    AGO->>DB: Topics + SubTopics oluştur
    AGO->>AGO: State → Learning
```

---

## 7. Ajan Bağımlılık Matrisi

| Ajan | Tükettiği Ajanlar | Kullandığı Data Store | Tetiklenme Zamanı |
|---|---|---|---|
| **SupervisorAgent** | IntentClassifier | — | Her mesaj (Learning state) |
| **IntentClassifierAgent** | — | — (cache: ConcurrentDict) | Supervisor + Analyzer çağrısı |
| **TutorAgent** | GraderAgent | Redis (perf, piston, gold, profile) | Her Learning mesajı |
| **EvaluatorAgent** | — | Redis (eval, topic_score, gold) | Background task (her mesaj) |
| **AnalyzerAgent** | IntentClassifier | — | Background task (her mesaj) |
| **GraderAgent** | — | — | TutorAgent, DeepPlanAgent, KorteksAgent |
| **DeepPlanAgent** | GraderAgent | DB (Topics) | Plan mode tetiklendiğinde |
| **WikiAgent** | — | DB (WikiPages, WikiBlocks) | Summarizer tetiklemesiyle |
| **SummarizerAgent** | — | DB (Messages, WikiPages) | Background task (topic complete) |
| **KorteksAgent** | GraderAgent | Redis (research report) | Kullanıcı araştırma başlattığında |
| **QuizAgent** | — | DB (QuizAttempts) | Frontend quiz kayıt |
| **StudentProfileService** | — | DB (Users) | TutorAgent, DeepPlanAgent |

---

## 8. UML Tabanlı Kopukluk Analizi

UML diyagramlarını izleyerek tespit ettiğim **hala mevcut potansiyel kopukluklar**:

### 🟡 Potansiyel Kopukluk 1: SummarizerAgent → WikiAgent Tek Yönlü

**UML İzi:** SummarizerAgent modül bittiğinde (`IsMastered = true`) wiki özeti oluşturuyor ama WikiAgent'ın çıktısı TutorAgent'a geri beslenmiyor. Öğrenci aynı konuya geri dönerse Tutor wiki özetinden habersiz kalabilir.

**Durum:** `FetchWikiContextAsync` zaten var — bu kısmen çözülmüş. **Riski düşük.**

---

### 🟡 Potansiyel Kopukluk 2: Korteks Raporu → Quiz Entegrasyonu Yok

**UML İzi:** KorteksAgent araştırma raporu oluşturuyor ve Redis'e yazıyor. TutorAgent `GetKorteksResearchReportAsync` ile bu raporu okuyor ve ders anlatımına dahil ediyor. **AMA** quiz üretiminde `researchContext` parametresine bu rapor **otomatik olarak geçirilmiyor** — orkestratörde `GenerateTopicQuizAsync` çağrısında `researchContext` yok.

**Etki:** Korteks'in bulduğu güncel bilgiler ders anlatımına giriyor ama quizlere yansımıyor. Öğrenci güncel bilgiden soru görmüyor.

**Önerilen Çözüm:** Quiz üretiminden önce `redis.GetKorteksResearchReportAsync()` ile raporu çekip `researchContext` parametresine geçirmek.

---

### 🟡 Potansiyel Kopukluk 3: QuizAgent vs TutorAgent — Çift Yollu Quiz Üretimi

**UML İzi:** İki farklı quiz üretim yolu var:
1. **Orkestratör yolu:** `_tutorAgent.GenerateTopicQuizAsync()` — aktif akışta kullanılıyor (satır 990)
2. **Event yolu:** `TopicCompletedHandler` → `_quizAgent.GeneratePendingQuizAsync()` — MediatR event ile tetikleniyor

**Problem:** İki ajan farklı prompt'lar, farklı kurallar ve farklı kalite kontrolü kullanıyor:
- TutorAgent: `weaknessContext` + `pastQuestionsWarning` + `goalContext` → Adaptive
- QuizAgent: OpenTrivia DB + Grader peer review → Daha geniş ama adaptif değil

**Risk:** Race condition — ikisi aynı anda quiz üretirse `session.PendingQuiz` birbirini ezebilir.

---

### 🟡 Potansiyel Kopukluk 4: TopicDetectorService → Orkestratöre Bağlı Değil

**UML İzi:** `TopicDetectorService` DI'da kayıtlı (`Program.cs:95`) ama ne orkestratörde ne de herhangi bir Controller'da kullanılıyor. Bu servis null-topic modunda kullanıcının ilk mesajından konu tespit etmek için tasarlanmış ancak orkestratör kendi iç mantığıyla konu belirliyor.

**Durum:** Gerçek dead code. DI'dan kaldırılabilir veya orkestratöre entegre edilebilir.

---

### 🟢 Doğrulandı: SkillMasteryService Aktif

`SkillMasteryService` orkestratörde aktif olarak kullanılıyor (satır 1129): quiz geçildikten sonra `RecordMasteryAsync(userId, subTopicId, title, score)` çağrılıyor. **Kopukluk yok.**

---

### 🟢 Çözülmüş Kopukluklar (Bu Oturumda)

| # | Kopukluk | Çözüm | Dosya |
|---|---|---|---|
| ✅ 1 | IntentClassifier çift LLM call | Session-scoped cache | IntentClassifierAgent.cs |
| ✅ 2 | Supervisor CONFUSED sinyali iletilmiyor | supervisorHint | AgentOrchestratorService.cs |
| ✅ 3 | Quiz weakness context yok | Adaptive Quiz (Redis) | TutorAgent.cs, AgentOrchestratorService.cs |
| ✅ 4 | Konu geçişi salt string-match | AI + String hibrit | AgentOrchestratorService.cs |
| ✅ 5 | IDE sessionId gönderilmiyor | Frontend → Backend zinciri | api.ts, InteractiveIDE.tsx, Home.tsx |
| ✅ 6 | IDE quiz Piston sonucu dahil değil | Redis'ten enrichment | AgentOrchestratorService.cs |
| ✅ 7 | EvaluatorAgent kodlama metriği yok | Kod-özel değerlendirme | EvaluatorAgent.cs |
| ✅ 8 | IDE dil bağlamı kopuyor | Dil metadata eklendi | InteractiveIDE.tsx |
| ✅ 9 | IDE sonuçları profile kaydedilmiyor | Metrik + hata kayıt | CodeController.cs |

---

## 9. Kalan Aksiyonlar Özeti

| Öncelik | Kopukluk | Karmaşıklık | Tahmini Etki |
|---|---|---|---|
| 🟡 Orta | Korteks raporu quiz'e dahil değil | Düşük (tek satır ekleme) | Quizler güncel kaynaklardan soru soramıyor |
| 🟡 Orta | QuizAgent/TutorAgent çift quiz üretimi | Orta (mimari karar) | Race condition riski + tutarsız quiz kalitesi |
| 🟡 Düşük | TopicDetectorService dead code | Çok düşük (temizlik) | Kod karmaşıklığı |

