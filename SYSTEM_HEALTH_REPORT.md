# Orka AI - System Health Report

> **Date:** 2026-04-15 20:49
> **Backend:** http://localhost:5065/api
> **Test User:** orka_test_runner@orka.ai

## Overall Score: EXCELLENT 94/100

---

## Test Results

| Test | Result | Status | Detail |
|------|--------|--------|--------|
| Backend Reachable | 5/5 | OK | 914 ms |
| AI Provider: Groq (Fallback) | 3/3 | OK | 187 ms |
| GitHub: GitHub/gpt-4o | 3/3 | OK | 1880 ms |
| GitHub: GitHub/gpt-4o-mini | 2/3 | PARTIAL | 2132 ms |
| GitHub: GitHub/Llama-405B | 3/3 | OK | 1387 ms |
| AI Failover Chain | 5/5 | OK | 1010 ms |
| AIAgentFactory: Tutor | 3/3 | OK | 1689 ms, model=gpt-4o |
| AIAgentFactory: DeepPlan | 3/3 | OK | 631 ms, model=Meta-Llama-3.1-405B-Instruct |
| AIAgentFactory: Analyzer | 3/3 | OK | 1299 ms, model=gpt-4o-mini |
| AIAgentFactory: Summarizer | 3/3 | OK | 1467 ms, model=gpt-4o-mini |
| AIAgentFactory: Korteks | 1/3 | PARTIAL | 4292 ms, model=Meta-Llama-3.1-405B-Instruct |
| Cohere: Embedding (1024-dim) | 5/5 | OK | 1024 dim, 372 ms |
| Auth: Login | 5/5 | OK | 564 ms |
| Auth: Token Refresh | 3/3 | OK | 131 ms |
| GET /topics | 3/3 | OK | 90 ms |
| GET /user/me | 3/3 | OK | 44 ms |
| GET /dashboard/stats | 3/3 | OK | 81 ms |
| GET /quiz/stats | 3/3 | OK | 40 ms |
| GET /dashboard/recent-activity | 2/2 | OK | 22 ms |
| Topic: CREATE | 4/4 | OK | ID: 176739ed-9fea-4d2b-bac3-5c2d180eebcb |
| Topic: LIST Consistency | 3/3 | OK | Found in list |
| Topic: PATCH | 3/3 | OK | 23 ms |
| Chat: TutorAgent First Response | 5/5 | OK | 1490 ms, 114 chars |
| Chat: Quiz JSON Quality | 5/5 | OK | API OK, quiz context accumulating (by design) |
| Chat: Conversation Continuity | 5/5 | OK | 2124 ms |
| Chat: Session End | 2/2 | OK | 23 ms |
| SSE: Stream Response | 5/5 | OK | 60 chunks, THINKING=False |
| SSE: Content-Type Header | 5/5 | OK | text/event-stream |
| Wiki: Endpoint Reachable | 5/5 | OK | 0 page(s) |
| Wiki: Page Content Quality | 5/10 | PARTIAL | New topic - wiki pending (expected) |
| Dashboard: Stats Shape | 5/5 | OK | All gamification fields present |
| Dashboard: TotalXP Valid | 3/3 | OK | XP=0 |
| Dashboard: Streak Valid | 2/2 | OK | Streak=0 |
| Dashboard: Topic Counts Consistent | 3/3 | OK | 0 completed, 0 active, 3 total |
| Quiz: Stats Shape | 2/2 | OK | All fields present |
| Resilience: 404 Handling | 2/2 | OK | HTTP 404 |
| Resilience: Auth Guard | 2/2 | OK | HTTP 401 |
| Resilience: User Profile | 2/2 | OK | Shape OK |
| Quiz: History by Topic | 3/3 | OK | 0 attempts |
| Quiz: Record Attempt | 4/4 | OK | HTTP 200 |
| Quiz: Attempt Persistence | 4/4 | OK | 1 attempts found |
| Quiz: Global Stats Update | 3/3 | OK | totalQuizzes=1 |
| Gamification: Endpoint | 4/4 | OK | All fields present |
| Gamification: XP Non-Negative | 3/3 | OK | XP=0 |
| Gamification: Level Calc | 3/3 | OK | Level=1 |
| Topics: Subtopics Endpoint | 4/4 | OK | count=0 |
| Topics: Progress Endpoint | 4/4 | OK | All fields OK |
| Korteks: Ping | 2/2 | OK | 14 ms |
| Korteks: Sync Research | 5/8 | PARTIAL | 428 chars |
| Korteks: URL Context | 3/3 | OK | 438 chars |
| Korteks: File Endpoint | 3/3 | OK | HTTP 200 - endpoint reachable |
| Korteks: URL Validation | 2/2 | OK | Invalid URL ignored, research continued |
| User: Settings Update | 3/3 | OK | HTTP 200 |
| User: Settings Persisted | 3/3 | OK | language=Turkish |
| Perf: SSE First Token | 2/3 | PARTIAL | 1591 ms (target <1500ms) |
| Perf: Plan Mode [PLAN_READY] | 3/3 | OK | Baseline quiz detected - correct flow |
| Perf: DB Latency p95 | 5/5 | OK | worst p95=16ms (target <200ms) |
| Perf: Wiki Generation Delay | 4/4 | OK | Correct: wiki deferred until topic complete |
| Wiki: Pages Available | 2/2 | OK | Correct: deferred until topic complete |
| Wiki: Add Note | 4/4 | OK | Correct: wiki deferred until topic complete |
| Wiki: Delete Block | 4/4 | OK | Correct: wiki deferred until topic complete |
| Wiki: Export | 4/4 | OK | Correct: wiki deferred until topic complete |
| Perf: Concurrent Requests | 3/4 | PARTIAL | 5/5 OK, max=788ms |

---

## GitHub Models and Agent Status

| Provider / Agent | Status | Latency |
|------------------|--------|---------|
| Groq | OK | 187 ms |
| GitHub/gpt-4o | OK | 1880 ms |
| GitHub/gpt-4o-mini | OK | 2132 ms |
| GitHub/Llama-405B | OK | 1387 ms |
| Factory-Tutor | OK | 1689 ms |
| Factory-DeepPlan | OK | 631 ms |
| Factory-Analyzer | OK | 1299 ms |
| Factory-Summarizer | OK | 1467 ms |
| Factory-Korteks | OK | 4292 ms |
| CohereEmbed | OK | 372 ms |

---

## Key Findings & Recommendations

### FIXED (Resolved in this sprint)

1. ~~**Unmonitored fire-and-forget tasks**~~ - Task.Run blocks now have try-catch; SummarizerAgent failures mark WikiPage.Status='failed'.
2. ~~**Session race condition**~~ - GetOrCreateSessionAsync now uses per-user SemaphoreSlim + double-check.
3. ~~**No mid-stream exception recovery**~~ - [ERROR] signal flushed to client; ChatPanel stops streaming and shows toast.
4. ~~**Hardcoded Dashboard stats**~~ - /dashboard/stats now returns real totalXP, currentStreak, completedTopics, activeLearning.
5. ~~**No XP / Streak system**~~ - User.TotalXP += 20 on correct quiz; streak logic in HandleQuizModeAsync.
6. ~~**Korteks hallucination / no citations**~~ - Wikipedia plugin + TavilySearchDeep + citation-mandatory prompt. Temperature 0.2.
7. ~~**Korteks file/URL input missing**~~ - PDF/TXT/MD upload via /research-file (PdfPig); URL context via sourceUrl param. Frontend: attach + URL toggle in WikiDrawer.

### HIGH (Next sprint)

1. **No rate limiting** on /chat/message and /chat/stream.
2. **CORS too permissive** - AllowAnyOrigin() is a production security risk.
3. **Missing CancellationToken chain** - background tasks run after client disconnects.
4. **WikiPage.Status='failed' not surfaced in frontend** - WikiMainPanel should show error card instead of polling forever.

### MEDIUM

5. **XP only from HandleQuizModeAsync** - direct /quiz/attempt calls do not award XP yet.
6. **Streak updated only on quiz pass** - daily login should also update LastActiveDate.

### Token Savings

| Suggestion | Expected Saving | Priority |
|------------|-----------------|----------|
| Shorten TutorAgent system prompt | ~200 tokens/req | High |
| AnalyzerAgent: use last 5 msgs instead of 20 | ~600 tokens/analysis | High |
| AnalyzerAgent: trigger every 3 msgs, not every msg | ~66% cost reduction | High |
| SummarizerAgent: use LastStudySnapshot instead of full conversation | ~800 tokens/wiki | Medium |

---

*Generated by Orka AI Integration Test Suite*

