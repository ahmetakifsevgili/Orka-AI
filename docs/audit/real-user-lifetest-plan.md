# Orka Real-User Lifetest Plan

This is the production-like manual lifetest for Orka's full Learning OS. It is not a unit test and not a random endpoint ping. It uses the running API exactly like real authenticated users.

Script:

```powershell
node scripts/real-user-lifetest.mjs --api-url=http://localhost:5065
```

Provider-backed Tutor/Classroom calls are disabled by default:

```powershell
node scripts/real-user-lifetest.mjs --api-url=http://localhost:5065 --include-ai-provider
```

Use the provider flag only when live provider calls and possible cost are explicitly approved.

## What Must Be Running

- SQL Server / LocalDB with Orka migrations applied.
- Redis if the selected environment requires Redis readiness.
- Orka API running and reachable at `ORKA_API_URL` or `--api-url`.
- No frontend server is required for this script.
- No AI provider key is required by default.

The script requires `/health/ready` by default. If you only want to inspect a partially ready environment, pass `--allow-unready`, but do not count that as beta proof.

## Personas

Default personas:

- `new`: a brand-new learner with thin evidence.
- `repair`: a learner with repeated weakness signals, a guarded quiz attempt, flashcard/review work, Study Room start, wrong checkpoint, and blank checkpoint.
- `evidence-code`: a learner with source upload, source/wiki evidence checks, Notebook pack, exam practice/deneme checks, Code IDE readiness, and code runtime/redaction probe.

Run a subset:

```powershell
node scripts/real-user-lifetest.mjs --personas=new,repair --api-url=http://localhost:5065
```

The default creates three real test users. This intentionally stays inside the default register rate-limit shape. If Redis has recent auth-attempt counters, wait for the auth window or clear only the dedicated development rate-limit data intentionally.

## Actual API Coverage

The script calls real endpoints, including:

- `POST /api/auth/register`
- `POST /api/auth/login`
- `GET /api/user/me`
- `POST /api/topics`
- `GET /api/topics`
- `GET /api/topics/{id}`
- `GET /api/dashboard/today`
- `GET /api/dashboard/stats`
- `GET /api/learning/orka-state`
- `GET /api/learning/mission-control`
- `GET /api/learning/study-coach`
- `POST /api/learning/signal`
- `POST /api/quiz/attempt`
- `GET /api/review/due`
- `POST /api/flashcards`
- `POST /api/flashcards/{id}/review`
- `GET /api/wiki/{topicId}`
- `GET /api/wiki/{topicId}/graph`
- `POST /api/wiki/page/{pageId}/note` when a page exists
- `POST /api/sources/upload`
- `GET /api/sources/topic/{topicId}`
- `GET /api/sources/topic/{topicId}/quality`
- `GET /api/sources/topic/{topicId}/lifecycle-summary`
- `GET /api/sources/topic/{topicId}/evidence-bundle`
- `GET /api/sources/wiki-pro`
- `GET /api/classroom/study-room`
- `POST /api/classroom/study-room/start`
- `POST /api/classroom/study-room/checkpoint`
- `GET /api/notebook-studio/pro`
- `POST /api/notebook-studio/topic/{topicId}/source-pack`
- `GET /api/notebook-studio/packs/{packId}`
- `GET /api/notebook-studio/packs/{packId}/export/preview`
- `GET /api/central-exams`
- `GET /api/central-exams/kpss`
- `GET /api/central-exams/kpss/war-room`
- `POST /api/central-exams/kpss/turkce-paragraf/start`
- `POST /api/central-exams/kpss/turkce-paragraf/submit` when questions exist
- `GET /api/central-exams/kpss/denemeler`
- `POST /api/central-exams/kpss/denemeler/{blueprintCode}/start`
- `POST /api/central-exams/kpss/denemeler/submit` when questions exist
- `GET /api/code/learning-ide`
- `POST /api/code/run` unless `--skip-code-run`
- `GET /api/production-readiness/v1`

It also verifies cross-user isolation by trying to access another persona's topic/source/study-room/code/notebook state.

## What It Checks

- SQL/Redis/API readiness through health endpoints.
- Real auth registration/login/user context.
- Real topic creation and user-scoped reads.
- New learner thin-evidence behavior.
- Repeated weakness behavior through real learning-signal ingestion.
- Quiz answer-key guard behavior through `POST /api/quiz/attempt`.
- Review/flashcard write/read path.
- Study Room start/checkpoint with wrong and blank responses.
- Source upload and evidence lifecycle.
- Source/Wiki Pro readiness and warnings.
- Notebook Studio pack and preview flow when possible.
- Exam War Room plus practice/deneme safety when questions exist.
- Code IDE readiness and runtime redaction behavior.
- Persona-specific module outputs should differ after different evidence.
- Public payload safety sweep.
- Cross-user access blocking.

## Safety Sweep

Every public response body is scanned for blocked markers:

- raw prompt / hidden prompt / system prompt / developer prompt
- raw provider payload
- raw source chunk
- raw tool payload
- debug trace
- local path
- API key / secret / token
- answer key / correct answer before submit
- stack trace
- owner id / unsafe user id
- raw transcript
- success guarantee / official-overclaim markers

Auth token responses are special-cased so the auth envelope can return a bearer token while other public module DTOs cannot leak unsafe token fields.

The JSON/Markdown reports store only step metadata and short object references, not bearer tokens or full response bodies.

## Expected Output

Reports are written to:

```text
scripts/reports/real-user-lifetest-YYYYMMDDHHMMSS.json
scripts/reports/real-user-lifetest-YYYYMMDDHHMMSS.md
```

Exit code:

- `0`: no hard failures.
- `1`: at least one hard failure.
- `2`: unhandled/fatal script error.

Warnings are still important. They usually mean a feature is present but the current data did not prove the richer behavior, such as no exam questions available for a deneme.

## What This Does Not Prove

- Live qualitative Tutor answer quality unless `--include-ai-provider` is used.
- Browser layout or frontend visual quality.
- Load/stress capacity.
- Real production observability over days.
- Payment, mobile, teacher/classroom management, or provider migrations. Those are intentionally out of scope.

## Recommended Run Order

1. Start SQL and Redis.
2. Start the API.
3. Confirm the API manually if needed:

```powershell
Invoke-RestMethod http://localhost:5065/health/ready
```

4. Run the provider-free lifetest:

```powershell
node scripts/real-user-lifetest.mjs --api-url=http://localhost:5065
```

5. Read the Markdown report first, then the JSON if a step needs engineering detail.

6. Only after provider calls are approved, run:

```powershell
node scripts/real-user-lifetest.mjs --api-url=http://localhost:5065 --include-ai-provider
```

