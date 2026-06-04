# OrkaOS v1 - UML Diyagramlari, Toplu Sistem Tasarimi ve Roadmap

Tarih: 2026-06-04  
Durum: Sistem ve ozellik UML dokumani  
Kapsam: Iki toplu UML, ozellik bazli diagramlar, data flow, deployment, gelecek fazlar

## 1. Toplu UML #1 - OrkaOS Sistem Context Diyagrami

```mermaid
flowchart LR
  User["Ogrenci / Ogretmen / Kurum"]
  Frontend["Orka Frontend\nReact/Vite UI"]
  API["Orka API\nASP.NET Controllers"]
  Tutor["Tutor & Chat Orchestration"]
  Planner["Planlama\nDeepPlan / Sequencing"]
  Quiz["Quiz & Question Bank"]
  Wiki["Orka Wiki\nDers/Kavram Akisi"]
  OrkaLM["OrkaLM\nSource Notebook"]
  Studio["Notebook Studio\nArtifacts"]
  Audio["Audio Study Room\nTranscript/Caption/TTS"]
  Memory["Learning Memory\nSignals/Mastery/Trace"]
  Evidence["Source Evidence\nCitation/Lifecycle"]
  Providers["AI Providers\nGemini/GitHub/Groq/etc."]
  DB["SQL Database"]
  Cache["Redis / Background Queue"]

  User --> Frontend
  Frontend --> API
  API --> Tutor
  API --> Planner
  API --> Quiz
  API --> Wiki
  API --> OrkaLM
  API --> Studio
  API --> Audio

  Tutor --> Memory
  Planner --> Memory
  Quiz --> Memory
  Wiki --> Memory
  OrkaLM --> Evidence
  Studio --> Wiki
  Studio --> OrkaLM
  Audio --> Studio

  Tutor --> Providers
  Planner --> Providers
  Quiz --> Providers
  Studio --> Providers
  Audio --> Providers

  API --> DB
  API --> Cache
  Memory --> DB
  Evidence --> DB
```

## 2. Toplu UML #2 - Kapsamli Feature/Data Flow

```mermaid
flowchart TB
  Start["Kullanici hedefi / mesaj / kaynak / wiki sayfasi"]

  subgraph Intake["Intake ve Context"]
    Intent["Intent Gate"]
    Topic["Topic / Session"]
    Surface["Surface Contract\nwiki | orkalm"]
    Context["Context Adapter\nWikiFeatureContext | OrkaLmFeatureContext"]
  end

  subgraph Learning["Learning Core"]
    Diagnostic["Diagnostic Quiz"]
    Plan["DeepPlan + Sequencing"]
    Tutor["Tutor Response"]
    Signals["Learning Signals"]
    Mastery["Concept Mastery"]
  end

  subgraph Knowledge["Knowledge Surfaces"]
    Wiki["Wiki\nPage/Block/Graph"]
    Sources["OrkaLM\nSource/Chunk/Citation"]
    Notebook["Notebook Packs"]
    Artifacts["Artifacts\nbriefing/study/quiz/slide/UML/audio"]
  end

  subgraph Quality["Quality and Safety Gates"]
    Auth["Auth Token Hardening"]
    Privacy["Public Projection"]
    Provider["AI Reliability"]
    Assessment["Assessment Quality Gate"]
    Export["Safe Export Preview"]
  end

  subgraph Output["User Output"]
    UI["Frontend Panels"]
    Audio["Audio + Caption + Study Room"]
    Practice["Quiz / Flashcard / Repair"]
    Graph["Graph / UML / Slides"]
  end

  Start --> Intent --> Topic --> Surface --> Context
  Context --> Diagnostic
  Context --> Plan
  Context --> Tutor
  Context --> Wiki
  Context --> Sources
  Diagnostic --> Signals --> Mastery
  Plan --> Notebook
  Tutor --> Notebook
  Wiki --> Notebook
  Sources --> Notebook
  Notebook --> Artifacts
  Artifacts --> Export
  Artifacts --> UI
  Artifacts --> Audio
  Artifacts --> Practice
  Artifacts --> Graph
  Auth --> UI
  Privacy --> UI
  Provider --> Diagnostic
  Provider --> Plan
  Provider --> Tutor
  Assessment --> Practice
```

## 3. Wiki-OrkaLM Ayrim Diyagrami

```mermaid
flowchart LR
  subgraph WikiSurface["Wiki Surface"]
    WP["WikiPage"]
    WB["WikiBlock"]
    WG["WikiGraph"]
    WArtifacts["Wiki Artifacts"]
    WAudio["Wiki Audio"]
  end

  subgraph OrkaLmSurface["OrkaLM Surface"]
    LS["LearningSource"]
    SC["SourceChunk"]
    CIT["Citation"]
    SN["SourceNotebook"]
    SArtifacts["Source Artifacts"]
    SAudio["OrkaLM Audio"]
  end

  WP --> WB --> WG --> WArtifacts --> WAudio
  LS --> SC --> CIT --> SN --> SArtifacts --> SAudio

  WArtifacts -. "no automatic sync" .- SArtifacts
  WAudio -. "crossSurfaceSync=false" .- SAudio
```

## 4. Audio Study Room Sequence Diyagrami

```mermaid
sequenceDiagram
  participant U as User
  participant UI as Frontend
  participant A as AudioController
  participant S as AudioOverviewService
  participant P as AI Provider
  participant T as TTS
  participant C as ClassroomController
  participant CS as ClassroomService

  U->>UI: Sesli Ozet iste
  UI->>A: POST /audio/overview surface/context ids
  A->>S: CreateOverviewAsync
  S->>S: Validate wikiPageId/sourceId isolation
  S->>P: Generate dialogue script
  S->>T: Generate audio bytes
  T-->>S: Audio or failure
  S-->>A: AudioOverviewJobDto transcript/caption/fallback
  A-->>UI: Job DTO
  UI->>U: Audio player + captions
  U->>UI: Burayi anlamadim
  UI->>C: POST /classroom/session with audio job context
  C->>C: Hard fail if context mismatch
  C->>CS: Start session / ask
  CS->>P: Generate answer script
  CS-->>UI: Answer + optional audio fallback
```

## 5. Diagnostic + Plan Sequencing Sequence Diyagrami

```mermaid
sequenceDiagram
  participant U as User
  participant API as Plan/Quiz API
  participant I as Intent Gate
  participant D as Diagnostic Service
  participant Q as Diagnostic Quality Gate
  participant P as Provider
  participant S as PlanSequencingService
  participant M as Learning Memory

  U->>API: Plan hedefi girer
  API->>I: Intent approval
  I-->>API: Approved learning intent
  API->>D: Build diagnostic request
  D->>P: Strict diagnostic generation
  P-->>D: Quiz blueprint
  D->>Q: Validate blueprint
  Q-->>D: Valid or fail-fast
  U->>API: Quiz cevaplari
  API->>M: QuizAttempt + LearningSignal
  API->>S: Build sequence
  S->>M: Read mastery/tracing/evidence
  S-->>API: needs_diagnostic | needs_repair | ready | thin_plan
```

## 6. Notebook Studio Artifact Flow

```mermaid
flowchart TD
  Request["Notebook action\nbriefing/study/slide/UML/audio"]
  Contract["Feature Contract\nsurface/context/id"]
  Adapter{"surface?"}
  WikiCtx["WikiFeatureContext\nWikiPage/Concept/Tutor/QuestionBank"]
  SourceCtx["OrkaLmFeatureContext\nSource/Chunk/Citation/Notebook"]
  Generator["Artifact Generator"]
  Quality["Safety + Evidence Metadata"]
  Artifact["LearningArtifact"]
  Preview["Professional Preview"]
  Export["Export Preview"]

  Request --> Contract --> Adapter
  Adapter -- wiki --> WikiCtx
  Adapter -- orkalm --> SourceCtx
  WikiCtx --> Generator
  SourceCtx --> Generator
  Generator --> Quality --> Artifact
  Artifact --> Preview
  Artifact --> Export
```

## 7. Soru Bankasi ve Learning Evidence Diyagrami

```mermaid
flowchart LR
  Question["Question Item"]
  Attempt["QuizAttempt"]
  Signal["LearningSignal"]
  Trace["KnowledgeTracingState"]
  Mastery["ConceptMastery"]
  Repair["Repair Loop"]
  Plan["Plan Readiness"]
  Tutor["Tutor Next Action"]

  Question --> Attempt
  Attempt --> Signal
  Signal --> Trace
  Trace --> Mastery
  Mastery --> Plan
  Plan --> Repair
  Repair --> Tutor
  Tutor --> Question
```

## 8. Class Diagram - Ana Domain Model

```mermaid
classDiagram
  class Topic {
    Guid Id
    string Title
  }

  class Session {
    Guid Id
    Guid TopicId
  }

  class WikiPage {
    Guid Id
    string Title
    string ConceptKey
  }

  class WikiBlock {
    Guid Id
    Guid WikiPageId
    string BlockType
  }

  class LearningSource {
    Guid Id
    string Title
    string Status
  }

  class SourceChunk {
    Guid Id
    Guid LearningSourceId
    int PageNumber
  }

  class LearningNotebookPack {
    Guid Id
    Guid TopicId
    Guid? WikiPageId
    string PackType
  }

  class LearningArtifact {
    Guid Id
    string ArtifactType
    string SourceBasis
  }

  class AudioOverviewJob {
    Guid Id
    string Status
    string Surface
  }

  class ClassroomSession {
    Guid Id
    Guid? AudioOverviewJobId
    string Surface
  }

  class QuizAttempt {
    Guid Id
    Guid TopicId
    bool IsCorrect
  }

  class LearningSignal {
    Guid Id
    string SignalType
  }

  Topic --> Session
  Topic --> WikiPage
  WikiPage --> WikiBlock
  Topic --> LearningSource
  LearningSource --> SourceChunk
  Topic --> LearningNotebookPack
  WikiPage --> LearningNotebookPack
  LearningNotebookPack --> LearningArtifact
  LearningArtifact --> AudioOverviewJob
  AudioOverviewJob --> ClassroomSession
  QuizAttempt --> LearningSignal
```

## 9. State Diagram - Plan Readiness

```mermaid
stateDiagram-v2
  [*] --> NoEvidence
  NoEvidence --> NeedsDiagnostic: no learner evidence
  NeedsDiagnostic --> EvidenceCollected: quiz/practice signal
  EvidenceCollected --> NeedsRepair: weak evidence
  EvidenceCollected --> Ready: sufficient mastery
  EvidenceCollected --> ThinPlan: graph/output too thin
  NeedsRepair --> RepairLoop
  RepairLoop --> EvidenceCollected: checkpoint attempt
  ThinPlan --> BuildConceptGraph
  BuildConceptGraph --> EvidenceCollected
  Ready --> ActivePlan
```

## 10. Deployment Diyagrami

```mermaid
flowchart TB
  Browser["Browser"]
  Front["Orka Frontend\nVite build"]
  Api["Orka API\nASP.NET"]
  Sql["SQL Database"]
  Redis["Redis / Queue"]
  Tts["Edge-TTS / TTS Provider"]
  Ai["AI Providers"]

  Browser --> Front
  Front --> Api
  Api --> Sql
  Api --> Redis
  Api --> Tts
  Api --> Ai
```

## 11. Gelecek Roadmap

### 11.1 Release temizlik fazi

- Dirty worktree kapsam ayrimi
- PR/commit stratejisi
- Life-proof skipped test karari
- CI workflow standardizasyonu

### 11.2 Audio kalite fazi

- Studio voice preset
- Multi-language voice pairs
- Audio generation observability
- Caption editing
- Segment-based asking
- Audio retention dashboard

### 11.3 Manual bridge fazi

Bu faz otomatik sync degil, kullanici kontrollu kopru olur.

Olasiliklar:

- OrkaLM artifact -> "Wiki'ye not olarak ekle"
- Wiki page -> "OrkaLM source notebook ile iliskilendir"
- Citation -> Wiki block reference
- Wiki concept -> Source concept link

Kurallar:

- Varsayilan sync kapali.
- Kullanici onayi olmadan yazma yok.
- Audit trail zorunlu.

### 11.4 Video/visual overview fazi

- Video overview
- Infographic
- Animated concept map
- Slide deck export
- Teacher presentation mode

### 11.5 Kurumsal faz

- Teacher/admin dashboard
- Class/cohort analytics
- Standards coverage
- Question bank operations
- Institution-safe data lifecycle

## 12. OrkaOS v1 Kapanis Notu

OrkaOS v1 icin temel mimari artik su hale geldi:

- Wiki normal ders akisi olarak kalir.
- OrkaLM kaynak yukleme ve source notebook olarak kalir.
- Ozellikler iki yuzeyde de vardir.
- Context'ler karismaz.
- AI strict roller fake fallback yapmaz.
- Diagnostic/planlama kalite kapilari fail-fast davranir.
- Sesli ozet ve sesli calisma odasi transcript/caption/fallback ile profesyonel contract'a baglanir.
- Sistem release testlerinden gecmistir.

