# Orka System Architecture

This document describes the current Orka Learning OS architecture with
implementation-aligned UML. It intentionally avoids database table contents,
secret names, provider payloads, raw prompts, and operational identifiers.

## 1. System Context

```mermaid
flowchart LR
    Learner["Learner"] --> Orka["Orka Learning OS"]
    Orka --> AiProviders["AI Providers via Routing Policy"]
    Orka --> RedisRuntime["Redis-Compatible Runtime"]
    Orka --> DurableStore["Durable Learning Store"]
    RedisRuntime --> Orka
    DurableStore --> Orka
```

## 2. Container View

```mermaid
flowchart TB
    subgraph Client["Client"]
        Web["React App"]
        BrowserTests["Playwright Browser Gates"]
    end

    subgraph Api["API"]
        Auth["Auth, Tenant, Ownership"]
        LearningEndpoints["Learning Endpoints"]
        ContentEndpoints["Wiki, Sources, Quiz, Classroom, Code"]
        BoundaryGuards["Payload and Privacy Guards"]
    end

    subgraph Core["Core Services"]
        Projection["Learning Projection Service"]
        Mission["Mission Control Service"]
        Coach["Study Coach Service"]
        Tutor["Tutor Orchestration"]
        Plan["Plan Diagnostic Service"]
        Quiz["Quiz Attempt Recorder"]
        Wiki["Wiki and Source Services"]
        Provider["AI Provider Routing"]
    end

    subgraph Infra["Infrastructure"]
        Durable["Durable Learning Store"]
        Redis["Redis-Compatible Runtime"]
        ExternalAi["External AI Providers"]
    end

    Web --> LearningEndpoints
    Web --> ContentEndpoints
    BrowserTests --> Web
    LearningEndpoints --> Auth
    ContentEndpoints --> Auth
    Auth --> BoundaryGuards
    BoundaryGuards --> Projection
    BoundaryGuards --> Mission
    BoundaryGuards --> Coach
    BoundaryGuards --> Tutor
    BoundaryGuards --> Plan
    BoundaryGuards --> Quiz
    BoundaryGuards --> Wiki
    Tutor --> Provider
    Plan --> Provider
    Wiki --> Provider
    Provider --> ExternalAi
    Projection --> Durable
    Mission --> Projection
    Coach --> Projection
    Quiz --> Durable
    Wiki --> Durable
    Core --> Redis
```

## 3. Learning Projection Binding

The central projection is the shared product truth for Home, Tutor, Wiki,
Sources, Review, and Quiz.

```mermaid
sequenceDiagram
    participant H as Home
    participant W as Workspace Hook
    participant LS as Learning State API
    participant MC as Mission Control API
    participant CP as Context Pack API
    participant Surface as Tutor/Wiki/Quiz

    H->>W: Build workspace state
    W->>LS: Fetch learning state
    W->>MC: Fetch mission control
    opt Surface needs bounded context
        W->>CP: Fetch context pack
    end
    W-->>H: Merged projection
    H-->>Surface: Pass projection and refresh callback
    Surface->>Surface: Learner action
    Surface-->>H: Projection changed
    H->>W: Refresh with new key
```

## 4. Diagnostic to Remediation Loop

```mermaid
stateDiagram-v2
    [*] --> IntentCaptured
    IntentCaptured --> DiagnosticStarted
    DiagnosticStarted --> DiagnosticFinalized
    DiagnosticFinalized --> PlanMaterialized
    PlanMaterialized --> TutorTurn
    TutorTurn --> RemediationStarted: learner shows gap
    RemediationStarted --> RepairTraceWritten
    RepairTraceWritten --> CorrectAttemptVerified
    CorrectAttemptVerified --> RemediationCompleted
    RemediationCompleted --> ProjectionRefreshed
    ProjectionRefreshed --> [*]
```

The loop is closed by durable signals and a refreshed projection. Completion is
only recorded after a verified correct attempt follows a remediation start for
the same learning area.

## 5. Provider Routing and Degradation

```mermaid
flowchart LR
    Request["AI Role Request"] --> Budget["Budget and Policy Check"]
    Budget --> Attempts["Provider Attempt Builder"]
    Attempts --> Primary["Primary Provider"]
    Primary -->|ok| Normalize["Normalize Response"]
    Primary -->|rate, size, server, quota when allowed| Fallback["Fallback Provider"]
    Fallback --> Normalize
    Primary -->|auth or disabled| Degrade["Safe Degraded Result"]
    Fallback -->|failed| Degrade
    Normalize --> Safety["Public Payload Safety"]
    Degrade --> Safety
    Safety --> Response["Response or Stream"]
```

The provider layer classifies failure kinds and keeps live provider checks
separate from deterministic gates. Gemini remains opt-in when disabled by
configuration. Cohere can participate in configured fallback paths.

## 6. Redis-Compatible Runtime Behavior

Redis-compatible runtime paths are used for short-lived memory and invalidation,
not as the sole source of learning truth.

```mermaid
flowchart TD
    Service["Domain Service"] --> DurableWrite["Durable Evidence Write"]
    Service --> RuntimeWrite["Runtime Memory / Invalidation"]
    RuntimeWrite --> ProjectionStale["Projection Marked Stale"]
    DurableWrite --> ProjectionBuild["Projection Rebuild"]
    ProjectionStale --> ProjectionBuild
    ProjectionBuild --> PublicDto["User-Safe DTO"]
```

If Redis is unavailable, durable writes must remain safe. Runtime convenience
can degrade, but learner state must not be corrupted by cache failure.

## 7. Service Responsibilities

```mermaid
classDiagram
    class LearningProjectionService {
        +Build learner state
        +Expose user-safe next actions
        +Surface warnings and conflicts
    }

    class MissionControlService {
        +Select primary mission
        +Prepare module cards
        +Embed projection version
    }

    class TutorOrchestration {
        +Read bounded context
        +Teach adaptively
        +Write safe traces
    }

    class PlanDiagnosticService {
        +Capture intent
        +Create diagnostic
        +Materialize plan
    }

    class QuizAttemptRecorder {
        +Verify attempts
        +Record learning signals
        +Close remediation lifecycle
    }

    class WikiSourceServices {
        +Maintain concept pages
        +Build source-aware summaries
        +Append repair traces
    }

    LearningProjectionService <-- MissionControlService
    LearningProjectionService <-- TutorOrchestration
    LearningProjectionService <-- PlanDiagnosticService
    LearningProjectionService <-- QuizAttemptRecorder
    LearningProjectionService <-- WikiSourceServices
```

## 8. Public Payload Rules

All public DTOs should be learner-safe. They may include labels, summaries,
readiness states, next actions, warnings, and opaque references. They must not
include hidden prompts, provider payloads, raw source chunks, raw tool data,
local paths, stack traces, secrets, bearer tokens, JWTs, unsafe owner
identifiers, or answer keys before submission.

## 9. Release Gate Shape

```mermaid
flowchart LR
    Source["Source Changes"] --> Static["Static Guards"]
    Static --> Backend["Backend Quick Gate"]
    Backend --> Frontend["Frontend Typecheck and Smoke"]
    Frontend --> Browser["Browser Evidence"]
    Browser --> LifeProof["Provider-Free Life Proof"]
    LifeProof --> Review["Human Review for Live Provider Quality"]
```

Default gates are deterministic and provider-free. Live provider checks are an
explicit operator action and are not required for normal local regression.

## 10. Design Principles

- One central projection for learner state.
- Safe degradation over silent corruption.
- Provider routing behind abstractions.
- Redis-compatible runtime behavior without exposing runtime internals.
- Durable learning evidence first; cache and runtime memory second.
- Public payloads are summaries, labels, references, and actions, not internals.
- Browser evidence is required for UI interaction confidence.
