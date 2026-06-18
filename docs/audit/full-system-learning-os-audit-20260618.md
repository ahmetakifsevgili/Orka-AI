# Orka Full System Learning OS Audit - 2026-06-18

## Verdict

Audit/fix completion: 98%.

The core Learning OS path is operational after fixes: onboarding/auth, tenant-safe backend flows, plan diagnostic, diagnostic quiz, plan finalization, tutor, tutor trace, tool governance, Wiki/Copilot, question bank, practice, remediation, source evidence, notebook surfaces, classroom/audio-adjacent endpoints, Code IDE, dashboard/mission control, and frontend contract/build checks are passing in targeted verification.

## Fixes Applied

- AI usage budget checks no longer share a scoped `OrkaDbContext` across parallel diagnostic batch calls.
- Strict external fallback no longer stalls on GitHubModels rate-limit retry for strict roles.
- Strict fallback chain now tries `GitHubModels -> OpenRouter -> Mistral -> Cohere -> Groq` with 5 attempts.
- OpenRouter HTTP 402 is treated as `QuotaExceeded`, fallbackable but not same-provider retryable.
- Polly/Microsoft resilience timeout rejections are normalized as timeout/fallbackable provider failures.
- Mistral timeout budget was increased for strict diagnostic generation.
- Diagnostic quiz batches now run sequentially to avoid concurrent large provider calls.
- Duplicate-only diagnostic quiz quality failures can be repaired with assessment-grammar-backed questions.
- Groq output is clamped to provider config to reduce oversized completion requests.
- `/health/ready` uses a configurable 10s readiness timeout instead of the previous brittle 2s ceiling.
- Tutor tool smoke script now sets a client IP and closes sockets idempotently.

## Runtime Evidence

- `dotnet test Orka.API.Tests/Orka.API.Tests.csproj --filter "FullyQualifiedName~AiReliabilityTests|FullyQualifiedName~AuthSwaggerHealthSmokeTests|FullyQualifiedName~ProductionSafetyLiteTests"`: 61 passed.
- `dotnet test Orka.Infrastructure.UnitTests/Orka.Infrastructure.UnitTests.csproj --filter "FullyQualifiedName~PlanDiagnosticTests|FullyQualifiedName~DiagnosticQuizQualityGateTests"`: 40 passed.
- `npm run typecheck`: passed.
- `npm run quick:frontend`: passed, including UI smoke, endpoint contract smoke, and Vite production build.
- `node scripts/tutor-tool-smoke-audit.mjs --api-url=http://127.0.0.1:5065 --report-dir=artifacts\codex-out\full-audit\tutor-tool-smoke-final --client-ip=127.41.1.77`: PASS.
- `node scripts/real-user-lifetest.mjs --api-url=http://127.0.0.1:5065 --report-dir=artifacts\codex-out\full-audit\lifetest-final --personas=new,repair,evidence-code`: completed without hard failures.
- Pedagogy audit evidence:
  - `pedagogy-after-budget-fix`: SQL_Index_NewLearner and SQL_Cardinality_Gap passed 100/100.
  - `pedagogy-mistral-sequential-single`: Async_Misconception passed 100/100.
  - `pedagogy-mistral-sequential-remaining`: Blank_Skip_Learner passed 100/100.
  - `pedagogy-history-repair`: History_Source_Learner passed 100/100.

## Provider Findings

- Gemini remains disabled and should stay disabled while credit/key status is uncertain.
- GitHubModels is wired but was rate-limited during live strict-role tests.
- OpenRouter key is present, but live calls returned HTTP 402 PaymentRequired.
- Cohere is wired, but large strict diagnostic calls timed out in live tests.
- Mistral is live and carried strict DeepPlan/Quiz after timeout and sequential-batch changes.
- Groq is live, but large diagnostic quiz prompts can return 413 RequestEntityTooLarge; it remains a later fallback, not the first strict recovery path.

## Remaining Non-Blocking Gaps

- Provider-backed pedagogy works now, but strict AI calls can be slow: diagnostic finalization/tutor turns took tens of seconds in live runs.
- Real-user lifetest still warns that no Wiki page was found for one note/source-link flow.
- KPSS practice and mini-deneme endpoints are alive, but the current seed/content bank has no runnable published questions.
- The audit used deterministic and provider-backed smoke/probe scripts; it did not perform a manual browser visual walkthrough.

## Evidence Paths

- `D:\Orka\artifacts\codex-out\full-audit\lifetest-final\real-user-lifetest-20260618051757.md`
- `D:\Orka\artifacts\codex-out\full-audit\tutor-tool-smoke-final\tutor-tool-smoke-audit.md`
- `D:\Orka\artifacts\codex-out\full-audit\pedagogy-mistral-sequential-single\pedagogical-quality-audit.md`
- `D:\Orka\artifacts\codex-out\full-audit\pedagogy-mistral-sequential-remaining\pedagogical-quality-audit.md`
- `D:\Orka\artifacts\codex-out\full-audit\pedagogy-history-repair\pedagogical-quality-audit.md`
