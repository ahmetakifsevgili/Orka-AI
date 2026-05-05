# Tool Activation + Tutor Consumption Hardening

Branch: `feature/backend-production-hardening-dirty-orka-parity`  
Base accepted commit: `c0413cc Close scheduled workers push grounding signals`

## Executive Summary

This phase corrects the product interpretation of "risky tools":

- Risky does not mean useless.
- Risky means the tool must be activated through explicit auth, sandbox/provider gates, telemetry, fallback behavior, and pedagogy rules.
- IDE/Piston is a core learning tool and is enabled through authenticated sandbox API endpoints.
- Semantic Kernel auto-execution for IDE remains disabled so the model cannot run arbitrary code by itself.
- External current-data tools remain provider-gated and must fail closed with explicit metadata instead of hallucinating.

## Tool Decision Matrix

| Tool | Current capability status | Product role | Safety model | Tutor consumption path | Telemetry / signals | Final decision |
|---|---|---|---|---|---|---|
| `ide_execution` / Piston | Enabled via `/api/code/run` and `/api/code/execute`; SK auto-run disabled | Core learning tool for code practice, debugging, misconceptions | Auth required, Judge0/Piston sandbox, language allowlist, code/stdin limits, no host shell | Code result is stored in Redis for Tutor context, including success, compile/runtime errors, phase, safeTutorSummary | `IdeRunCompleted`, `IdeCompileError`, `IdeRuntimeError`, `IdeExecutionTimeout`, `IdeProviderUnavailable`; tool capability telemetry | `CORE_ENABLED_BEHIND_AUTH_AND_SANDBOX` |
| `wolfram_alpha` | Disabled unless AppId exists | Computation grounding for math/physics verification | Provider key required, timeout/fallback, no claim if unavailable | Metadata/tool detection reports disabled/gated state; live computation remains provider gate | Tool telemetry via runtime detection/plugin filter | `INTEGRATED_BEHIND_PROVIDER_GATE` when configured; otherwise `SAFE_FALLBACK_ONLY` |
| `news` | Disabled unless provider key exists | Current-info grounding | Provider key required, source/date/citation required, neutral summary | Tutor must not answer current-news from memory if provider unavailable | Tool telemetry, fallback metadata | `INTEGRATED_BEHIND_PROVIDER_GATE` when configured; otherwise `SAFE_FALLBACK_ONLY` |
| `weather` | Beta/provider gated | Factual context for geography/environment examples | Config gate, timeout, safe location fallback | Tutor labels as current weather/context data when available | Tool telemetry, fallback metadata | `INTEGRATED_BEHIND_PROVIDER_GATE` |
| `crypto` / finance | Beta/provider gated | Market-data teaching reference | Config gate, factual data only, no investment advice | Tutor may explain concepts, not recommend trades | Tool telemetry, no real spend on disabled fallback | `INTEGRATED_BEHIND_PROVIDER_GATE` |
| `youtube_pedagogy` | Beta/gated, transcript-aware | Pedagogy reference for teaching flow/examples/misconceptions | Pedagogy-only by default; factual claims require source evidence | EducatorCore normalizes flow/examples/analogies/common mistakes/practice ideas | `YouTubeReferenceUsed`, `TeachingMoveApplied` | `INTEGRATED_BEHIND_PROVIDER_GATE` |
| `tavily_web_search` | Provider gated | Web/source grounding for Korteks research | API key required; sources separated from user docs | Korteks/tool metadata exposes degraded/disabled state when unavailable | Tool telemetry/fallback metadata | `INTEGRATED_BEHIND_PROVIDER_GATE` |
| `visual_generation` | Beta/gated | Illustrative visual support | Provider/image URL generation only when enabled; Mermaid text preferred | Chat metadata detects Pollinations image URLs | usedTools metadata | `BETA_ADMIN_OR_DEV_ONLY` / beta gate |
| `mermaid` | Enabled text generation | Low-risk diagram-ready explanation | Text-only fenced code block, no provider | Chat content preserves Mermaid; metadata detects fenced blocks | usedTools metadata | `CORE_ENABLED_BEHIND_AUTH_AND_SANDBOX` equivalent low-risk text path |

## IDE / Piston Learning Loop

The IDE execution path is intentionally active and is not a disabled toy:

1. Authenticated user calls `/api/code/run` or `/api/code/execute`.
2. `CodeController` sends code to `IPistonService`.
3. `PistonService` uses Judge0 CE sandbox; it never runs host shell commands.
4. The execution result is normalized into:
   - `phase`: `compile`, `run`, `timeout`, `blocked`, `provider_missing`
   - `stdout`
   - `stderr`
   - `compileError`
   - `runtimeError`
   - `exitCode`
   - `durationMs`
   - `truncated`
   - `safeTutorSummary`
5. If `sessionId` is provided, the result is stored in Redis even when execution failed.
6. Tutor fetches `[SON KOD ÇIKTISI]` context and must explain the real sandbox result without inventing output.
7. Learning signals are written:
   - success -> `IdeRunCompleted`
   - compile error -> `IdeCompileError`
   - runtime error -> `IdeRuntimeError`
   - timeout -> `IdeExecutionTimeout`
   - provider unavailable -> `IdeProviderUnavailable`

## Chat / Tool Metadata Contract

`UsedToolDto` remains backward-compatible and now exposes optional structured fields:

- `toolId`
- `success`
- `fallbackUsed`
- `provider`
- `latencyMs`
- `citations`
- `sourceConfidence`
- `errorCode`
- `safeMessage`

Existing clients can continue using:

- `name`
- `status`
- `evidence`
- `fallbackReason`

## Provider-Gated Tools

Wolfram, News, Weather, Crypto/finance, Tavily, YouTube, and visual generation must not hallucinate when unavailable.

Rules:

- Missing provider config returns explicit disabled/gated fallback.
- Startup must not crash when keys are absent.
- Tutor metadata should expose `usedTools` and fallback reason when a user asks for the tool.
- Disabled/fallback tools do not create fake spend in cost records.
- Crypto/finance data is educational market data only and must not produce buy/sell advice.
- News/current events require provider/source evidence; model memory is not a valid current-news source.

## Test Evidence

New deterministic tests:

- compile error result is stored for Tutor and creates `IdeCompileError`
- runtime error result is stored for Tutor and creates `IdeRuntimeError`
- success stores stdout for Tutor and creates positive `IdeRunCompleted`
- oversized stdin is blocked before provider execution
- Tutor prompt contains Piston pedagogy guard

Updated capability tests:

- `ide_execution` is now `Enabled` with decision `CORE_ENABLED_BEHIND_AUTH_AND_SANDBOX`
- provider-heavy tools remain gated/disabled without provider keys

Latest focused validation:

- `dotnet build` -> PASS, 0 warnings, 0 errors.
- `dotnet test --no-build` -> PASS, 51 passed.
- `python -m pytest contract_tests/ -q` -> PASS, 37 passed, 1 skipped.

## Runtime / API Contract Notes

The runtime API surface is intentionally split:

- Student code execution is active through authenticated API routes:
  - `POST /api/code/run`
  - `POST /api/code/execute`
- Semantic Kernel IDE auto-execution remains a disabled/safe stub. This prevents a model/tool planner from running arbitrary code without an explicit user IDE action.
- `GET /api/tools/capabilities/ide_execution` now reports:
  - `status`: `Enabled`
  - `decision`: `CORE_ENABLED_BEHIND_AUTH_AND_SANDBOX`
  - `fallbackMode`: `sandbox_api_fallback`

Runtime smoke on `http://127.0.0.1:5101`:

| Endpoint | Expected | Actual | Result |
|---|---:|---:|---|
| `/swagger/index.html` | 200 | 200 | PASS |
| `/health/live` | 200 | 200 | PASS |
| `/health/ready` | 200 | 200 | PASS |
| `/api/korteks/ping` without token | 401 | 401 | PASS |
| `/api/tools/capabilities` | 200 | 200 | PASS |
| `/api/tools/capabilities/ide_execution` | 200 | 200 | PASS |
| `/api/tools/capabilities/wolfram_alpha` | 200 | 200 | PASS |
| `/api/tools/capabilities/news` | 200 | 200 | PASS |
| `/api/tools/capabilities/weather` | 200 | 200 | PASS |
| `/api/tools/capabilities/crypto` | 200 | 200 | PASS |
| `/api/tools/capabilities/youtube_pedagogy` | 200 | 200 | PASS |

Observed capability statuses:

- `ide_execution`: `Enabled`, `CORE_ENABLED_BEHIND_AUTH_AND_SANDBOX`, `sandbox_api_fallback`
- `wolfram_alpha`: `Disabled`, `DISABLED_WITH_RUNTIME_STUB`, `disabled_stub`
- `news`: `Disabled`, `DISABLED_WITH_RUNTIME_STUB`, `disabled_stub`
- `weather`: `Disabled`, `DISABLED_WITH_RUNTIME_STUB`, `disabled_stub`
- `crypto`: `Disabled`, `DISABLED_WITH_RUNTIME_STUB`, `disabled_stub`
- `youtube_pedagogy`: `Disabled`, `DISABLED_WITH_RUNTIME_STUB`, `disabled_stub`

Expanded code execution response fields:

- `phase`: `compile`, `run`, `timeout`, `blocked`, `provider_missing`
- `compileError`
- `runtimeError`
- `exitCode`
- `durationMs`
- `truncated`
- `safeTutorSummary`
- `runtime`

Tutor consumption proof is deterministic:

- Failed executions are stored for Tutor when `sessionId` exists.
- Compile errors are classified separately from runtime errors.
- Tutor prompt explicitly instructs the agent to use the real sandbox output and not invent stdout/stderr/error details.
- Code execution result storage and learning-signal creation are covered by `ToolActivationTutorConsumptionTests`.

## Provider Tool Activation Boundary

Wolfram, News, Weather, Crypto/finance, Tavily, YouTube, and visual generation are not removed and not treated as useless. They are production-safe provider-gated tools:

- If configuration is absent, capability status is disabled/gated and calls degrade safely.
- If configuration is present, the backend contract can expose the provider path without changing API shape.
- Tests use fake providers or disabled-path assertions; no real provider key is required.
- News/current events must be sourced and dated; model memory is not accepted as current-news proof.
- Crypto/finance output is educational market data only and must not include buy/sell advice.
- YouTube remains pedagogy/reference by default; factual grounding requires explicit transcript/source evidence.

## Remaining Notes

True backend blockers: none in the activated IDE/Piston path.

Backend follow-up:

- Live Wolfram/news/weather/crypto providers can be implemented behind provider interfaces and fake-provider tests.
- Current state is safe fallback/provider gate, not live-provider proof.
- Finance/crypto UI must present "not investment advice" copy when surfaced later.

## Final Core Intelligence Update

The live-provider adapter layer has now been implemented behind provider gates:

- `IWolframProvider` / `WolframProvider`
- `INewsProvider` / `NewsProvider`
- `IWeatherProvider` / `WeatherProvider`
- `IMarketDataProvider` / `MarketDataProvider`

The previous follow-up "live providers can be implemented" is now closed at backend adapter level. Real external calls remain configuration-gated, while fake-provider tests prove success, malformed, missing-key and telemetry paths without requiring real keys.

New learning intelligence:

- `IMistakeClassifierService` classifies wrong quiz and IDE/code errors.
- `MistakeClassified` learning signals are persisted.
- Wrong quiz classification feeds durable review pressure through the existing ReviewItem path.
- IDE compile/runtime/timeout failures feed classification and learning signals when topic/session context exists.

New YouTube pedagogy intelligence:

- `IYouTubeTranscriptProvider` normalizes and chunks transcript text.
- `IYouTubeTeachingReferenceService` retrieves relevant chunks and extracts teaching flow, examples, analogies, common mistakes and practice ideas.
- `YouTubeTranscriptPlugin.BuildTeachingReference` exposes this path to the Tutor/Semantic Kernel runtime.
- `TeachingMoveApplied` is persisted when user/topic context exists.

Remaining product gate:

- Real Wolfram/news/weather/crypto/YouTube provider use still requires configuration.
- Tests intentionally use fake providers and safe missing-key fallbacks.
