# Orka AI - System Health Report

> **Date:** 2026-04-15 06:17
> **Backend:** http://localhost:5065/api
> **Test User:** orka_test_runner@orka.ai

## Overall Score: AVERAGE 68/100

---

## Test Results

| Test | Result | Status | Detail |
|------|--------|--------|--------|
| Backend Reachable | 5/5 | OK | 663 ms |
| AI Provider: Groq (Fallback) | 3/3 | OK | 158 ms |
| GitHub: GitHub/gpt-4o | 0/3 | FAIL | HTTP 0 |
| GitHub: GitHub/gpt-4o-mini | 0/3 | FAIL | HTTP 0 |
| GitHub: GitHub/Llama-405B | 0/3 | FAIL | HTTP 0 |
| AI Failover Chain | 5/5 | OK | 256 ms |
| AIAgentFactory: Tutor | 0/3 | FAIL | HTTP 0 |
| AIAgentFactory: DeepPlan | 3/3 | OK | 629 ms, model=Meta-Llama-3.1-405B-Instruct |
| AIAgentFactory: Analyzer | 3/3 | OK | 1411 ms, model=gpt-4o-mini |
| AIAgentFactory: Summarizer | 3/3 | OK | 1221 ms, model=gpt-4o-mini |
| AIAgentFactory: Korteks | 3/3 | OK | 731 ms, model=Meta-Llama-3.1-405B-Instruct |
| Cohere: Embedding (1024-dim) | 5/5 | OK | 1024 dim, 605 ms |
| Auth: Login | 5/5 | OK | 322 ms |
| Auth: Token Refresh | 3/3 | OK | 135 ms |
| GET /topics | 3/3 | OK | 16 ms |
| GET /user/me | 3/3 | OK | 14 ms |
| GET /dashboard/stats | 3/3 | OK | 20 ms |
| GET /quiz/stats | 3/3 | OK | 20 ms |
| GET /dashboard/recent-activity | 2/2 | OK | 332 ms |
| Topic: CREATE | 4/4 | OK | ID: 686ac03a-09bc-407d-bb4d-d4ca5bca2301 |
| Topic: LIST Consistency | 3/3 | OK | Found in list |
| Topic: PATCH | 3/3 | OK | 114 ms |
| Chat: TutorAgent First Response | 0/5 | FAIL | SSE error |
| Chat: Quiz JSON Quality | 0/5 | FAIL | No session |
| Chat: Conversation Continuity | 0/5 | FAIL | No session |
| Chat: Session End | 0/2 | FAIL | No session |
| SSE: Stream Response | 3/5 | PARTIAL | 0 chunks, THINKING=False |
| SSE: Content-Type Header | 3/5 | PARTIAL | Partial test |
| Wiki: Endpoint Reachable | 5/5 | OK | 0 page(s) |
| Wiki: Page Content Quality | 5/10 | PARTIAL | New topic - wiki pending (expected) |
| Dashboard: Stats Shape | 5/5 | OK | All gamification fields present |
| Dashboard: TotalXP Valid | 3/3 | OK | XP=0 |
| Dashboard: Streak Valid | 2/2 | OK | Streak=0 |
| Dashboard: Topic Counts Consistent | 3/3 | OK | 0 completed, 0 active, 1 total |
| Quiz: Stats Shape | 2/2 | OK | All fields present |
| Resilience: 404 Handling | 2/2 | OK | HTTP 404 |
| Resilience: Auth Guard | 2/2 | OK | HTTP 401 |
| Resilience: User Profile | 2/2 | OK | Shape OK |
| Quiz: History by Topic | 3/3 | OK | 0 attempts |
| Quiz: Record Attempt | 0/4 | FAIL | HTTP 500 |
| Quiz: Attempt Persistence | 0/4 | FAIL | Empty history after attempt |
| Quiz: Global Stats Update | 1/3 | PARTIAL | Zero count |
| Gamification: Endpoint | 4/4 | OK | All fields present |
| Gamification: XP Non-Negative | 3/3 | OK | XP=0 |
| Gamification: Level Calc | 3/3 | OK | Level=1 |
| Topics: Subtopics Endpoint | 4/4 | OK | count=0 |
| Topics: Progress Endpoint | 4/4 | OK | All fields OK |
| Korteks: Ping | 2/2 | OK | 12 ms |
| Korteks: Sync Research | 5/8 | PARTIAL | 421 chars |
| Wiki: Pages Available | 1/2 | PARTIAL | Empty |
| Wiki: Add Note | 0/4 | FAIL | No page |
| Wiki: Delete Block | 0/4 | FAIL | No block |
| Wiki: Export | 2/4 | PARTIAL | 404 - no wiki yet |
| User: Settings Update | 3/3 | OK | HTTP 200 |
| User: Settings Persisted | 3/3 | OK | language=Turkish |
| Perf: SSE First Token | 1/3 | PARTIAL | Stream error (partial score) |
| Perf: Plan Mode [PLAN_READY] | 1/3 | PARTIAL | Signal not detected in 6.3s |
| Perf: DB Latency p95 | 5/5 | OK | worst p95=18ms (target <200ms) |
| Perf: Wiki Generation Delay | 0/4 | FAIL | >60s timeout |
| Perf: Concurrent Requests | 4/4 | OK | 5/5 OK, max=122ms |

---

## GitHub Models and Agent Status

| Provider / Agent | Status | Latency |
|------------------|--------|---------|
| Groq | OK | 158 ms |
| Factory-Tutor | UNREACHABLE | N/A |
| Factory-DeepPlan | OK | 629 ms |
| Factory-Analyzer | OK | 1411 ms |
| Factory-Summarizer | OK | 1221 ms |
| Factory-Korteks | OK | 731 ms |
| CohereEmbed | OK | 605 ms |

---

## Key Findings & Recommendations

### FIXED (Resolved in this sprint)

1. ~~**Unmonitored fire-and-forget tasks**~~ - Task.Run blocks now have try-catch; SummarizerAgent failures mark WikiPage.Status='failed'.
2. ~~**Session race condition**~~ - GetOrCreateSessionAsync now uses per-user SemaphoreSlim + double-check.
3. ~~**No mid-stream exception recovery**~~ - [ERROR] signal flushed to client; ChatPanel stops streaming and shows toast.
4. ~~**Hardcoded Dashboard stats**~~ - /dashboard/stats now returns real totalXP, currentStreak, completedTopics, activeLearning.
5. ~~**No XP / Streak system**~~ - User.TotalXP += 20 on correct quiz; streak logic in HandleQuizModeAsync.

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

