# Technical Due-Diligence Summary

Phase: Investor-grade system optimization and due-diligence gate

## Architecture Overview

Orka is a .NET 8 plus React/Vite adaptive learning system. The backend coordinates Tutor, DeepPlan, Korteks, Wiki, source/RAG, IDE/Piston, review/SRS, flashcards, daily challenge, bookmarks, audio/classroom and provider tools through durable SQL state, Redis memory/cache, Semantic Kernel plugins and a capability-driven frontend contract.

## Backend Capabilities

- Auth, topics, sessions, chat and dashboard APIs.
- Source upload/query/delete with document citation contracts.
- Wiki/summarizer/context generation with grounding separation.
- Tutor, DeepPlan and EducatorCore orchestration with source priority.
- IDE/Piston sandbox execution integrated as teaching context.
- Mistake classification and learning signal persistence.
- Review/SRS, flashcards, daily challenge, XP, badges, bookmarks and notifications.
- Tool capabilities, provider fallback metadata, tool telemetry and cost records.

## Frontend Capabilities

- Vite + React 19 + TypeScript frontend.
- Centralized API client and capability-driven tool visibility.
- Chat metadata chips, citations and fallback notices.
- IDE phase-aware output panels and Tutor handoff.
- LearningPanel for flashcards, review, daily challenge and bookmarks.
- Source/wiki surfaces with source evidence and citation display.

## AI / Tool Runtime

- Semantic Kernel plugins are registered for sources, review, flashcards, daily challenge, bookmarks, visual generation, Wolfram, IDE, weather, news, crypto, YouTube and Mermaid-style explanations.
- Risky tools are backend-gated; frontend does not call providers directly.
- Provider failures become safe fallback metadata rather than hard user-facing crashes.

## Data, SQL And Redis Posture

- SQL Server is the durable source for users, topics, sessions, messages, learning signals, sources, review items, flashcards, daily challenges, bookmarks, notifications, telemetry and cost records.
- Redis is used for short-lived memory/context, feedback loops and cache-style behavior.
- Redis and SQL health are exposed via readiness endpoints.

## QA / Testing Posture

- .NET test suite covers API contracts, tool activation, telemetry, scheduled workers, grounding and final core intelligence.
- Python contract tests validate runtime API behavior against a live local backend.
- Frontend smoke scripts validate UI contract and endpoint coverage.
- This phase adds TypeScript typecheck as a low-cost QA guard.

## Security Posture

- JWT auth protects user-scoped APIs.
- User isolation is covered by contract tests.
- IDE execution is backend-only and sandbox-oriented; no host shell execution path should be exposed.
- Secrets are expected through user-secrets/environment/provider config, not committed files.
- Firebase/provider absence must not crash startup.

## Observability / Cost

- ToolTelemetryEvents capture tool use, fallback, provider status and errors.
- CostRecords capture agent/provider/model cost estimates.
- Metadata is size-capped and telemetry failures are non-blocking.

## Current Limitations

- This is a prototype with production-hardening gates, not a certified enterprise deployment.
- Live Wolfram and YouTube transcript proofs require configured provider keys.
- Public provider fallbacks may rate-limit.
- YouTube Data API v3 metadata/search is locally proven when configured, but public transcript availability is separate and may degrade.
- Production SLOs, SOC2/HIPAA/GDPR compliance and cloud-credit approval are not claimed.

## Roadmap Items

- staging deployment and production provisioning
- provider key proof and quota strategy
- Redis/provider chaos testing
- personal study-coach, motivation and adaptive routine loops
- advanced analytics and observability dashboards
- Google Cloud staging, provider quotas and Redis/provider chaos repeat
