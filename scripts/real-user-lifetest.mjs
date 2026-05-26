#!/usr/bin/env node
/*
 * Orka real-user Learning OS lifetest.
 *
 * This script talks to the running API like real browser clients:
 * register/login, create topics, upload sources, record learning signals,
 * start Study Room, exercise exam/code/notebook/review contracts, then run
 * safety and cross-user isolation checks.
 *
 * It is provider-free by default. Use --include-ai-provider only when a human
 * explicitly wants live provider-backed Tutor/Classroom checks.
 */

import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(__dirname, "..");

const args = parseArgs(process.argv.slice(2));
const BASE_URL = trimSlash(args["api-url"] ?? process.env.ORKA_API_URL ?? "http://localhost:5065");
const REPORT_DIR = path.resolve(ROOT, args["report-dir"] ?? "scripts/reports");
const INCLUDE_AI_PROVIDER = boolArg("include-ai-provider");
const ALLOW_UNREADY = boolArg("allow-unready");
const SKIP_CODE_RUN = boolArg("skip-code-run");
const PERSONA_FILTER = (args.personas ?? "new,repair,evidence-code")
  .split(",")
  .map((x) => x.trim())
  .filter(Boolean);

const RUN_ID = String(args["run-id"] ?? new Date().toISOString().replace(/[-:.TZ]/g, "").slice(0, 14));
const PASSWORD = `OrkaLive${RUN_ID}!`;

const BLOCKED_FIELD_MARKERS = [
  "rawPrompt",
  "hiddenPrompt",
  "systemPrompt",
  "developerPrompt",
  "rawProviderPayload",
  "rawSourceChunk",
  "rawToolPayload",
  "debugTrace",
  "localPath",
  "apiKey",
  "secret",
  "answerKey",
  "correctAnswer",
  "stackTrace",
  "ownerId",
  "rawTranscript",
];

const TOKEN_FIELD_NAMES = new Set([
  "token",
  "accessToken",
  "refreshToken",
  "idToken",
  "bearerToken",
].map(normalizeKey));

const USER_ID_FIELD_NAMES = new Set([
  "userId",
  "ownerId",
  "ownerUserId",
  "unsafeUserId",
  "deletedByUserId",
  "importedByUserId",
].map(normalizeKey));

const CLAIM_MARKERS = [
  "success guarantee",
  "kesin basari",
  "kesin başarı",
  "tam uyumlu",
  "percentile",
  "placement",
  "%100 basari",
  "%100 başarı",
].map(normalizeText);

const results = [];
const created = {
  users: [],
  topics: [],
  sources: [],
  studyRooms: [],
  flashcards: [],
};

function parseArgs(argv) {
  const parsed = {};
  for (const raw of argv) {
    const match = raw.match(/^--([^=]+)(?:=(.*))?$/);
    if (!match) continue;
    parsed[match[1]] = match[2] ?? "true";
  }
  return parsed;
}

function boolArg(name) {
  const value = args[name];
  return value === true || value === "true" || value === "1" || value === "yes";
}

function trimSlash(value) {
  return String(value).replace(/\/+$/, "");
}

function qs(params = {}) {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== "") {
      search.set(key, String(value));
    }
  }
  const text = search.toString();
  return text ? `?${text}` : "";
}

function nowIso() {
  return new Date().toISOString();
}

function shortId(id) {
  if (!id) return null;
  const text = String(id);
  return text.length <= 12 ? text : `${text.slice(0, 8)}...${text.slice(-4)}`;
}

function addResult({ area, name, status, detail = "", evidence = undefined, severity = "required" }) {
  results.push({
    area,
    name,
    status,
    detail,
    evidence,
    severity,
    at: nowIso(),
  });

  const prefix = status === "pass" ? "OK" : status === "warn" ? "WARN" : status === "skip" ? "SKIP" : "FAIL";
  const suffix = detail ? ` - ${detail}` : "";
  console.log(`${prefix} [${area}] ${name}${suffix}`);
}

function pass(area, name, detail = "", evidence) {
  addResult({ area, name, status: "pass", detail, evidence });
}

function warn(area, name, detail = "", evidence) {
  addResult({ area, name, status: "warn", detail, evidence, severity: "optional" });
}

function fail(area, name, detail = "", evidence) {
  addResult({ area, name, status: "fail", detail, evidence });
}

function skip(area, name, detail = "") {
  addResult({ area, name, status: "skip", detail, severity: "optional" });
}

async function request(method, url, { token, body, formData, timeoutMs = 30000, safety = true, area = "HTTP", name = url } = {}) {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), timeoutMs);
  const headers = { Accept: "application/json" };
  if (token) headers.Authorization = `Bearer ${token}`;
  if (body !== undefined && !formData) headers["Content-Type"] = "application/json";

  const started = performance.now();
  try {
    const response = await fetch(`${BASE_URL}${url}`, {
      method,
      headers,
      body: formData ? formData : body !== undefined ? JSON.stringify(body) : undefined,
      signal: controller.signal,
    });
    const text = await response.text();
    const data = parseJson(text);
    const durationMs = Math.round(performance.now() - started);
    if (safety) {
      runSafetySweep(`${method} ${url}`, data ?? text, { allowAuthEnvelope: url.includes("/api/auth/") });
    }
    return { ok: response.ok, status: response.status, data, text, durationMs, area, name, url, method };
  } catch (error) {
    const durationMs = Math.round(performance.now() - started);
    return {
      ok: false,
      status: 0,
      data: null,
      text: "",
      durationMs,
      error: error instanceof Error ? error.message : String(error),
      area,
      name,
      url,
      method,
    };
  } finally {
    clearTimeout(timeout);
  }
}

function parseJson(text) {
  if (!text) return null;
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

function assertOk(response, area, name, { optional = false, statusCodes = [200], detail = "" } = {}) {
  const expected = statusCodes.includes(response.status);
  if (response.ok && expected) {
    pass(area, name, detail || `status=${response.status}, ${response.durationMs}ms`);
    return true;
  }

  const message = `status=${response.status}${response.error ? `, error=${response.error}` : ""}`;
  if (optional) warn(area, name, message);
  else fail(area, name, message);
  return false;
}

function runSafetySweep(label, value, options = {}) {
  if (value == null) return;

  const blockedHits = [];
  collectBlockedPayloadHits(value, [], blockedHits, options);
  const blocked = unique(blockedHits.map((hit) => hit.marker));

  if (blocked.length > 0) {
    const paths = blockedHits.slice(0, 4).map((hit) => hit.path).join(", ");
    fail("Safety", `${label} public payload marker sweep`, `blocked markers: ${blocked.join(", ")}${paths ? ` at ${paths}` : ""}`);
  }

  const claimHits = [];
  collectClaimHits(value, [], claimHits);
  const claimMarkers = unique(claimHits.map((hit) => hit.marker));
  if (claimMarkers.length > 0) {
    const paths = claimHits.slice(0, 4).map((hit) => hit.path).join(", ");
    fail("Safety", `${label} overclaim sweep`, `claim markers: ${claimMarkers.join(", ")}${paths ? ` at ${paths}` : ""}`);
  }
}

function collectBlockedPayloadHits(value, pathParts, hits, options) {
  if (value == null) return;

  if (Array.isArray(value)) {
    value.forEach((item, index) => collectBlockedPayloadHits(item, pathParts.concat(String(index)), hits, options));
    return;
  }

  if (typeof value === "object") {
    for (const [key, child] of Object.entries(value)) {
      const normalizedKey = normalizeKey(key);
      const childPath = pathParts.concat(key);

      for (const marker of BLOCKED_FIELD_MARKERS) {
        if (normalizedKey === normalizeKey(marker)) {
          hits.push({ marker, path: childPath.join(".") });
        }
      }

      if (!options.allowAuthEnvelope && TOKEN_FIELD_NAMES.has(normalizedKey)) {
        hits.push({ marker: "token", path: childPath.join(".") });
      }

      if (!options.allowAuthEnvelope && USER_ID_FIELD_NAMES.has(normalizedKey) && containsUnsafeUserId(child)) {
        hits.push({ marker: "userId", path: childPath.join(".") });
      }

      collectBlockedPayloadHits(child, childPath, hits, options);
    }
    return;
  }

  if (typeof value !== "string") return;
  const text = normalizeText(value);
  for (const marker of BLOCKED_FIELD_MARKERS) {
    if (text.includes(normalizeText(marker))) {
      hits.push({ marker, path: pathParts.join(".") || "$" });
    }
  }

  if (!options.allowAuthEnvelope && looksLikeJwt(value)) {
    hits.push({ marker: "token", path: pathParts.join(".") || "$" });
  }
}

function collectClaimHits(value, pathParts, hits) {
  if (value == null) return;
  if (Array.isArray(value)) {
    value.forEach((item, index) => collectClaimHits(item, pathParts.concat(String(index)), hits));
    return;
  }
  if (typeof value === "object") {
    for (const [key, child] of Object.entries(value)) {
      collectClaimHits(child, pathParts.concat(key), hits);
    }
    return;
  }
  if (typeof value !== "string") return;

  const text = normalizeText(value);
  for (const marker of CLAIM_MARKERS) {
    if (text.includes(marker) && !isNegatedClaim(text, marker)) {
      hits.push({ marker, path: pathParts.join(".") || "$" });
    }
  }
}

function containsUnsafeUserId(value) {
  if (typeof value === "string") return looksLikeGuid(value);
  if (Array.isArray(value)) return value.some(containsUnsafeUserId);
  if (value && typeof value === "object") return Object.values(value).some(containsUnsafeUserId);
  return value !== null && value !== undefined;
}

function looksLikeGuid(value) {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(String(value));
}

function looksLikeJwt(value) {
  return /\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b/.test(String(value));
}

function isNegatedClaim(text, marker) {
  const index = text.indexOf(marker);
  const start = Math.max(0, index - 80);
  const end = Math.min(text.length, index + marker.length + 120);
  const context = text.slice(start, end);
  return /\b(degil|degildir|yok|kurulmaz|kurulmadi|engellendi|blocked|block|guard|without|not|no)\b/.test(context);
}

function normalizeKey(value) {
  return String(value).replace(/[^a-zA-Z0-9]/g, "").toLowerCase();
}

function normalizeText(value) {
  return String(value)
    .normalize("NFD")
    .replace(/\p{Diacritic}/gu, "")
    .replace(/ı/g, "i")
    .replace(/İ/g, "i")
    .toLowerCase();
}

function unique(values) {
  return [...new Set(values)];
}

function firstArray(value) {
  return Array.isArray(value) ? value : [];
}

function get(obj, pathText) {
  return pathText.split(".").reduce((acc, key) => {
    if (acc == null) return undefined;
    return acc[key];
  }, obj);
}

function extractAction(payload) {
  return (
    get(payload, "primaryMission.actionType") ??
    get(payload, "todayExamMission.actionType") ??
    get(payload, "nextAction.view") ??
    get(payload, "mode") ??
    get(payload, "studyRoomMode") ??
    get(payload, "readinessStatus") ??
    "unknown"
  );
}

async function preflight() {
  console.log(`\nOrka real-user lifetest`);
  console.log(`Base URL: ${BASE_URL}`);
  console.log(`Run id: ${RUN_ID}`);
  console.log(`Provider calls: ${INCLUDE_AI_PROVIDER ? "enabled by explicit flag" : "disabled"}`);

  const live = await request("GET", "/health/live", { safety: true, area: "Preflight" });
  assertOk(live, "Preflight", "/health/live");

  const ready = await request("GET", "/health/ready", { safety: true, area: "Preflight" });
  const readyOk = assertOk(ready, "Preflight", "/health/ready SQL/Redis readiness", { optional: ALLOW_UNREADY });
  if (!readyOk && !ALLOW_UNREADY) {
    fail("Preflight", "hard stop", "API dependencies are not ready. Start SQL/Redis/API, or pass --allow-unready for investigation only.");
    await writeReport();
    process.exit(1);
  }

  const aggregate = await request("GET", "/health", { safety: true, area: "Preflight" });
  assertOk(aggregate, "Preflight", "/health aggregate", { optional: true, statusCodes: [200, 503] });
}

async function registerPersona(slug, firstName, topicTitle, category) {
  const email = `orka-lifetest-${slug}-${RUN_ID}@orka.local`;
  const register = await request("POST", "/api/auth/register", {
    body: { firstName, lastName: "Lifetest", name: `${firstName} Lifetest`, email, password: PASSWORD },
    safety: true,
    area: "Auth",
    name: `${slug} register`,
  });

  const canReuseExisting = register.status === 409 || register.status === 429;
  if (!register.ok && canReuseExisting) {
    warn("Auth", `${slug} register reused existing test identity`, `status=${register.status}; trying login`);
  } else if (!assertOk(register, "Auth", `${slug} register`, { statusCodes: [201] })) {
    return null;
  }

  const login = await request("POST", "/api/auth/login", {
    body: { email, password: PASSWORD },
    safety: true,
    area: "Auth",
    name: `${slug} login`,
  });
  if (!assertOk(login, "Auth", `${slug} login`)) {
    return null;
  }

  const token = login.data?.token ?? register.data?.token;
  if (!token) {
    fail("Auth", `${slug} token`, "login/register did not return a token");
    return null;
  }

  const me = await request("GET", "/api/user/me", {
    token,
    safety: false,
    area: "Auth",
    name: `${slug} me`,
  });
  assertOk(me, "Auth", `${slug} /api/user/me`);

  const topic = await request("POST", "/api/topics", {
    token,
    body: { title: `${topicTitle} ${RUN_ID}`, emoji: "O", category },
    area: "Topic",
    name: `${slug} create topic`,
  });
  if (!assertOk(topic, "Topic", `${slug} create topic`)) return null;

  const topicId = topic.data?.id;
  if (!topicId) {
    fail("Topic", `${slug} topic id`, "topic create returned no id");
    return null;
  }

  const persona = {
    slug,
    firstName,
    email,
    token,
    userRef: shortId(login.data?.user?.id ?? register.data?.userId),
    topicId,
    topicTitle: topic.data?.title ?? topicTitle,
    snapshots: {},
  };
  created.users.push({ slug, email, userRef: persona.userRef });
  created.topics.push({ slug, topicRef: shortId(topicId) });
  return persona;
}

async function getContract(persona, key, method, url, options = {}) {
  const response = await request(method, url, {
    token: persona.token,
    body: options.body,
    formData: options.formData,
    timeoutMs: options.timeoutMs ?? 30000,
    area: options.area ?? "Contract",
    name: `${persona.slug} ${key}`,
    safety: options.safety !== false,
  });
  const ok = assertOk(response, options.area ?? "Contract", `${persona.slug} ${key}`, {
    optional: options.optional,
    statusCodes: options.statusCodes ?? [200],
  });
  if (ok && options.snapshot !== false) {
    persona.snapshots[key] = response.data;
  }
  return response;
}

async function baselineLearningOs(persona) {
  const topicQuery = qs({ topicId: persona.topicId });
  await getContract(persona, "topics list", "GET", "/api/topics", { area: "Topic" });
  await getContract(persona, "topic detail", "GET", `/api/topics/${persona.topicId}`, { area: "Topic" });
  await getContract(persona, "dashboard today", "GET", "/api/dashboard/today", { area: "Dashboard" });
  await getContract(persona, "dashboard stats", "GET", "/api/dashboard/stats", { area: "Dashboard" });
  await getContract(persona, "learning state", "GET", `/api/learning/orka-state${topicQuery}`, { area: "LearningOS" });
  await getContract(persona, "mission control", "GET", `/api/learning/mission-control${topicQuery}`, { area: "LearningOS" });
  await getContract(persona, "study coach", "GET", `/api/learning/study-coach${topicQuery}`, { area: "LearningOS" });
  await getContract(persona, "review due", "GET", `/api/review/due${topicQuery}`, { area: "Review" });
  await getContract(persona, "wiki pages", "GET", `/api/wiki/${persona.topicId}`, { area: "Wiki" });
  await getContract(persona, "wiki graph", "GET", `/api/wiki/${persona.topicId}/graph`, { area: "Wiki", optional: true });
  await getContract(persona, "source wiki pro", "GET", `/api/sources/wiki-pro${topicQuery}`, { area: "SourceWikiPro" });
  await getContract(persona, "study room", "GET", `/api/classroom/study-room${topicQuery}`, { area: "StudyRoom" });
  await getContract(persona, "notebook studio pro", "GET", `/api/notebook-studio/pro${topicQuery}`, { area: "Notebook" });
  await getContract(persona, "code learning ide", "GET", `/api/code/learning-ide${qs({ topicId: persona.topicId, language: "python" })}`, { area: "CodeIDE" });
  await getContract(persona, "central exams", "GET", "/api/central-exams", { area: "Exam" });
  await getContract(persona, "exam war room", "GET", "/api/central-exams/kpss/war-room", { area: "Exam" });
  await getContract(persona, "production readiness", "GET", "/api/production-readiness/v1", { area: "Production", optional: true });
}

async function recordLearningSignal(persona, signalType, index, overrides = {}) {
  const payloadJson = JSON.stringify({
    lifetest: true,
    runId: RUN_ID,
    persona: persona.slug,
    index,
    evidence: "real_api_learning_signal",
    ...overrides.payload,
  });
  return getContract(persona, `${signalType} signal ${index}`, "POST", "/api/learning/signal", {
    area: "LearningSignal",
    snapshot: false,
    body: {
      topicId: persona.topicId,
      signalType,
      skillTag: overrides.skillTag ?? "fractions-common-denominator",
      topicPath: overrides.topicPath ?? "Mathematics > Fractions > Common denominator",
      score: overrides.score ?? 0,
      isPositive: overrides.isPositive ?? false,
      payloadJson,
    },
  });
}

async function runNewLearner(persona) {
  await baselineLearningOs(persona);
  const state = persona.snapshots["learning state"];
  const mission = persona.snapshots["mission control"];
  const dashboard = persona.snapshots["dashboard today"];
  const action = extractAction(mission);

  if (JSON.stringify({ state, mission, dashboard }).match(/thin_evidence|diagnostic|quick_start|start/i)) {
    pass("Persona:new", "new learner degrades to diagnostic/thin-evidence states", action);
  } else {
    warn("Persona:new", "new learner diagnostic signal not obvious", `primary=${action}`);
  }
}

async function runRepairLearner(persona) {
  await baselineLearningOs(persona);

  for (let i = 1; i <= 3; i++) {
    await recordLearningSignal(persona, "WeaknessDetected", i, {
      skillTag: "fractions-common-denominator",
      score: 0,
      isPositive: false,
      payload: { result: "repeated_wrong", observedFrom: "student_work" },
    });
  }

  await getContract(persona, "quiz attempt answer-key guard", "POST", "/api/quiz/attempt", {
    area: "Quiz",
    snapshot: false,
    body: {
      topicId: persona.topicId,
      questionId: `lifetest-guard-${RUN_ID}`,
      question: "1/2 + 1/4 islemini coz.",
      selectedOptionId: "wrong-option",
      isCorrect: false,
      explanation: "This client value must be ignored by the server.",
      skillTag: "fractions-common-denominator",
      conceptTag: "fractions-common-denominator",
      learningObjective: "Add fractions with unlike denominators",
      topicPath: "Mathematics > Fractions > Common denominator",
      questionHash: `lifetest-guard-${RUN_ID}`,
      responseTimeMs: 22000,
      confidenceSelfRating: 0.25,
    },
  });

  const flashcard = await getContract(persona, "create flashcard", "POST", "/api/flashcards", {
    area: "Review",
    body: {
      topicId: persona.topicId,
      front: "Kesirlerde ortak payda ne zaman gerekir?",
      back: "Paydalari farkli kesirleri toplarken veya cikarirken ortak payda gerekir.",
      hint: "Paydalari karsilastir.",
      skillTag: "fractions-common-denominator",
      conceptTag: "fractions-common-denominator",
      learningObjective: "Common denominator repair",
      difficulty: "medium",
      createdFrom: "real-user-lifetest",
    },
  });
  const flashcardId = flashcard.data?.id;
  if (flashcardId) {
    created.flashcards.push({ slug: persona.slug, flashcardRef: shortId(flashcardId) });
    await getContract(persona, "review flashcard", "POST", `/api/flashcards/${flashcardId}/review`, {
      area: "Review",
      snapshot: false,
      body: { quality: 2, notes: "Still needs repair." },
    });
  }

  const started = await getContract(persona, "start study room", "POST", "/api/classroom/study-room/start", {
    area: "StudyRoom",
    body: { topicId: persona.topicId, mode: "repair_lesson", examCode: "KPSS" },
  });
  const classroomSessionId = started.data?.classroomSessionId;
  if (classroomSessionId) {
    created.studyRooms.push({ slug: persona.slug, classroomSessionRef: shortId(classroomSessionId) });
    await getContract(persona, "study room wrong checkpoint", "POST", "/api/classroom/study-room/checkpoint", {
      area: "StudyRoom",
      body: {
        classroomSessionId,
        responseSignal: "wrong",
        answerText: "Ortak payda yerine paylari topladim.",
        skipped: false,
        conceptKey: "fractions-common-denominator",
      },
    });
    await getContract(persona, "study room blank checkpoint", "POST", "/api/classroom/study-room/checkpoint", {
      area: "StudyRoom",
      body: {
        classroomSessionId,
        responseSignal: "blank",
        answerText: "",
        skipped: true,
        conceptKey: "fractions-common-denominator",
      },
    });
  } else {
    warn("StudyRoom", "repair learner study room did not return classroomSessionId");
  }

  await refreshCoreContracts(persona);
  const combined = JSON.stringify({
    dashboard: persona.snapshots["dashboard today refreshed"],
    mission: persona.snapshots["mission control refreshed"],
    studyRoom: persona.snapshots["study room refreshed"],
  });
  if (/repair|review|weak|checkpoint|diagnostic|prerequisite/i.test(combined)) {
    pass("Persona:repair", "repair learner produced repair/review/diagnostic language");
  } else {
    warn("Persona:repair", "repair learner did not visibly shift mission text", "inspect report output and backend scoring thresholds");
  }
}

async function runEvidenceCodeLearner(persona) {
  await baselineLearningOs(persona);
  await uploadSource(persona);
  await sourceWikiNotebookFlow(persona);
  await examFlow(persona);
  await codeFlow(persona);
  await refreshCoreContracts(persona);
}

async function uploadSource(persona) {
  const sourceText = [
    "# Fractions source evidence",
    "",
    "Common denominators are used when adding fractions with unlike denominators.",
    "A careful learner first finds an equivalent denominator, then combines numerators.",
    "This test file intentionally contains no raw chunks, prompts, or credentials.",
  ].join("\n");
  const form = new FormData();
  form.append("TopicId", persona.topicId);
  form.append("File", new Blob([sourceText], { type: "text/markdown" }), `orka-lifetest-source-${RUN_ID}.md`);

  const upload = await getContract(persona, "upload markdown source", "POST", "/api/sources/upload", {
    area: "Source",
    formData: form,
    timeoutMs: 45000,
  });
  const sourceId = upload.data?.id;
  if (sourceId) {
    persona.sourceId = sourceId;
    created.sources.push({ slug: persona.slug, sourceRef: shortId(sourceId) });
  }

  await getContract(persona, "topic sources", "GET", `/api/sources/topic/${persona.topicId}`, { area: "Source" });
  await getContract(persona, "source quality", "GET", `/api/sources/topic/${persona.topicId}/quality`, { area: "Source", optional: true });
  await getContract(persona, "source lifecycle", "GET", `/api/sources/topic/${persona.topicId}/lifecycle-summary`, { area: "Source", optional: true });
  await getContract(persona, "source evidence bundle", "GET", `/api/sources/topic/${persona.topicId}/evidence-bundle`, { area: "Source" });
  if (sourceId) {
    await getContract(persona, "source concept links", "GET", `/api/sources/${sourceId}/concept-links`, { area: "Source", optional: true });
    await getContract(persona, "source wiki pro focused", "GET", `/api/sources/wiki-pro${qs({ topicId: persona.topicId, sourceId })}`, { area: "SourceWikiPro" });
  }
}

async function sourceWikiNotebookFlow(persona) {
  const wiki = await getContract(persona, "wiki pages after source", "GET", `/api/wiki/${persona.topicId}`, { area: "Wiki" });
  const firstPageId = firstArray(wiki.data)[0]?.id;
  if (firstPageId) {
    persona.wikiPageId = firstPageId;
    await getContract(persona, "add wiki manual note", "POST", `/api/wiki/page/${firstPageId}/note`, {
      area: "Wiki",
      body: { content: "Lifetest note: ortak payda adimini once yap." },
    });
    await getContract(persona, "wiki page source links", "GET", `/api/wiki/pages/${firstPageId}/source-links`, { area: "Wiki", optional: true });
  } else {
    warn("Wiki", "no wiki page found for note/source-link flow");
  }

  const pack = await getContract(persona, "build topic source pack", "POST", `/api/notebook-studio/topic/${persona.topicId}/source-pack`, {
    area: "Notebook",
    optional: true,
    body: { packType: "source_study_pack", includeArtifacts: false },
  });
  const packId = pack.data?.id ?? pack.data?.packId;
  if (packId) {
    persona.packId = packId;
    await getContract(persona, "pack detail", "GET", `/api/notebook-studio/packs/${packId}`, { area: "Notebook", optional: true });
    await getContract(persona, "pack export preview", "GET", `/api/notebook-studio/packs/${packId}/export/preview`, { area: "Notebook", optional: true });
  }
  await getContract(persona, "notebook studio pro focused", "GET", `/api/notebook-studio/pro${qs({ topicId: persona.topicId, sourceId: persona.sourceId, wikiPageId: persona.wikiPageId })}`, { area: "Notebook" });
}

async function examFlow(persona) {
  await getContract(persona, "kpss study home", "GET", "/api/central-exams/kpss", { area: "Exam" });
  const practice = await getContract(persona, "start kpss practice", "POST", "/api/central-exams/kpss/turkce-paragraf/start", {
    area: "Exam",
    optional: true,
    body: { limit: 3 },
  });
  const questions = firstArray(practice.data?.questions);
  if (questions.length > 0) {
    runSafetySweep("pre-submit practice session", practice.data);
    await getContract(persona, "submit kpss practice", "POST", "/api/central-exams/kpss/turkce-paragraf/submit", {
      area: "Exam",
      body: {
        practiceSetId: practice.data?.practiceSetId,
        answers: questions.map((q, index) => ({
          questionId: q.questionId,
          selectedOptionKey: index === 0 ? null : q.options?.[0]?.optionKey ?? null,
        })),
      },
    });
  } else {
    warn("Exam", "practice has no questions", practice.data?.emptyState ?? "question coverage limited");
  }

  const denemeler = await getContract(persona, "list kpss denemeler", "GET", "/api/central-exams/kpss/denemeler", {
    area: "Exam",
    optional: true,
  });
  const blueprintCode = firstArray(denemeler.data)[0]?.code ?? "KPSS_MINI_TURKCE_PARAGRAF";
  const deneme = await getContract(persona, "start kpss deneme", "POST", `/api/central-exams/kpss/denemeler/${encodeURIComponent(blueprintCode)}/start`, {
    area: "Exam",
    optional: true,
    body: {},
  });
  const denemeQuestions = firstArray(deneme.data?.questions);
  if (denemeQuestions.length > 0 && deneme.data?.denemeAttemptId) {
    runSafetySweep("pre-submit deneme session", deneme.data);
    await getContract(persona, "submit kpss deneme", "POST", "/api/central-exams/kpss/denemeler/submit", {
      area: "Exam",
      body: {
        denemeAttemptId: deneme.data.denemeAttemptId,
        answers: denemeQuestions.map((q, index) => ({
          questionId: q.questionId,
          selectedOptionKey: index < 2 ? q.options?.[0]?.optionKey ?? null : null,
        })),
      },
    });
  } else {
    warn("Exam", "deneme has no runnable questions", deneme.data?.emptyState ?? "question coverage limited");
  }

  await getContract(persona, "exam war room after practice", "GET", "/api/central-exams/kpss/war-room", { area: "Exam" });
}

async function codeFlow(persona) {
  if (SKIP_CODE_RUN) {
    skip("CodeIDE", "code execution skipped by --skip-code-run");
    return;
  }

  await getContract(persona, "code runtime blocked language", "GET", `/api/code/learning-ide${qs({ topicId: persona.topicId, language: "powershell" })}`, {
    area: "CodeIDE",
    optional: true,
  });

  const code = [
    "print('orka lifetest code run')",
    "print('apiKey=dummy token=dummy C:\\\\Users\\\\ahmet\\\\secret.txt rawPrompt stackTrace')",
  ].join("\n");
  await getContract(persona, "code run redaction probe", "POST", "/api/code/run", {
    area: "CodeIDE",
    optional: true,
    timeoutMs: 60000,
    body: {
      topicId: persona.topicId,
      code,
      language: "python",
    },
  });

  for (let i = 1; i <= 2; i++) {
    await recordLearningSignal(persona, "IdeCompileError", i, {
      skillTag: "python",
      topicPath: "Code > Python > syntax",
      payload: { phase: "compile", result: "syntax_error" },
    });
  }

  await getContract(persona, "code learning ide after errors", "GET", `/api/code/learning-ide${qs({ topicId: persona.topicId, language: "python" })}`, {
    area: "CodeIDE",
  });
}

async function refreshCoreContracts(persona) {
  const topicQuery = qs({ topicId: persona.topicId });
  await getContract(persona, "dashboard today refreshed", "GET", "/api/dashboard/today", { area: "Dashboard" });
  await getContract(persona, "mission control refreshed", "GET", `/api/learning/mission-control${topicQuery}`, { area: "LearningOS" });
  await getContract(persona, "study coach refreshed", "GET", `/api/learning/study-coach${topicQuery}`, { area: "LearningOS" });
  await getContract(persona, "study room refreshed", "GET", `/api/classroom/study-room${topicQuery}`, { area: "StudyRoom" });
  await getContract(persona, "source wiki pro refreshed", "GET", `/api/sources/wiki-pro${topicQuery}`, { area: "SourceWikiPro" });
  await getContract(persona, "notebook studio pro refreshed", "GET", `/api/notebook-studio/pro${topicQuery}`, { area: "Notebook" });
  await getContract(persona, "code learning ide refreshed", "GET", `/api/code/learning-ide${qs({ topicId: persona.topicId, language: "python" })}`, { area: "CodeIDE" });
}

async function crossUserIsolation(owner, other) {
  if (!owner || !other) {
    warn("Isolation", "cross-user skipped", "owner or other persona missing");
    return;
  }

  const checks = [
    ["topic detail", "GET", `/api/topics/${owner.topicId}`],
    ["topic sources", "GET", `/api/sources/topic/${owner.topicId}`],
    ["study room owner topic", "GET", `/api/classroom/study-room${qs({ topicId: owner.topicId })}`],
    ["code ide owner topic", "GET", `/api/code/learning-ide${qs({ topicId: owner.topicId, language: "python" })}`],
    ["notebook pro owner topic", "GET", `/api/notebook-studio/pro${qs({ topicId: owner.topicId })}`],
  ];
  if (owner.sourceId) {
    checks.push(["source notebook", "GET", `/api/sources/${owner.sourceId}/notebook`]);
  }

  for (const [label, method, url] of checks) {
    const response = await request(method, url, { token: other.token, safety: true, area: "Isolation", name: label });
    if (response.status === 404 || response.status === 403) {
      pass("Isolation", `${other.slug} blocked from ${owner.slug} ${label}`, `status=${response.status}`);
    } else {
      fail("Isolation", `${other.slug} can access ${owner.slug} ${label}`, `status=${response.status}`);
    }
  }
}

async function comparePersonaBehavior(personas) {
  const active = personas.filter(Boolean);
  if (active.length < 2) {
    warn("Coherence", "persona comparison skipped", "need at least two successful personas");
    return;
  }

  const rows = active.map((p) => ({
    slug: p.slug,
    mission: extractAction(p.snapshots["mission control refreshed"] ?? p.snapshots["mission control"]),
    studyRoom: extractAction(p.snapshots["study room refreshed"] ?? p.snapshots["study room"]),
    sourceWiki: extractAction(p.snapshots["source wiki pro refreshed"] ?? p.snapshots["source wiki pro"]),
    codeIde: extractAction(p.snapshots["code learning ide refreshed"] ?? p.snapshots["code learning ide"]),
  }));

  const serialized = rows.map((r) => JSON.stringify(r));
  const unique = new Set(serialized);
  if (unique.size > 1) {
    pass("Coherence", "personas produce different module states", rows.map((r) => `${r.slug}:${r.mission}/${r.studyRoom}/${r.codeIde}`).join(" | "));
  } else {
    warn("Coherence", "personas look identical after seeding", "module arbitration may be too generic");
  }
}

async function optionalProviderChecks(persona) {
  if (!INCLUDE_AI_PROVIDER) {
    skip("Provider", "Tutor/Classroom live provider calls disabled", "pass --include-ai-provider only when paid/live calls are approved");
    return;
  }
  if (!persona) {
    warn("Provider", "provider checks skipped", "no successful persona was available");
    return;
  }

  await getContract(persona, "provider chat message", "POST", "/api/chat/message", {
    area: "Provider",
    timeoutMs: 120000,
    body: {
      topicId: persona.topicId,
      content: "Kisa bir telafi aciklamasi yap: ortak payda neden gerekir?",
      isPlanMode: false,
    },
  });

  const classroom = await getContract(persona, "legacy classroom session", "POST", "/api/classroom/session", {
    area: "Provider",
    body: {
      topicId: persona.topicId,
      transcript: "[AI teacher]: Common denominator repair lesson.\n[AI assistant]: Ask for one checkpoint.",
    },
  });
  const id = classroom.data?.id;
  if (id) {
    await getContract(persona, "legacy classroom ask", "POST", `/api/classroom/${id}/ask`, {
      area: "Provider",
      timeoutMs: 120000,
      body: { question: "Bunu anlamadim, daha basit anlat.", activeSegment: "Common denominator repair lesson." },
    });
  }
}

async function writeReport() {
  await fs.mkdir(REPORT_DIR, { recursive: true });
  const startedAt = results[0]?.at ?? nowIso();
  const finishedAt = nowIso();
  const failCount = results.filter((r) => r.status === "fail").length;
  const warnCount = results.filter((r) => r.status === "warn").length;
  const passCount = results.filter((r) => r.status === "pass").length;
  const skipCount = results.filter((r) => r.status === "skip").length;
  const status = failCount > 0 ? "fail" : warnCount > 0 ? "warning" : "pass";
  const report = {
    runId: RUN_ID,
    baseUrl: BASE_URL,
    startedAt,
    finishedAt,
    status,
    includeAiProvider: INCLUDE_AI_PROVIDER,
    allowUnready: ALLOW_UNREADY,
    skipCodeRun: SKIP_CODE_RUN,
    summary: { pass: passCount, warn: warnCount, fail: failCount, skip: skipCount },
    created,
    results,
  };

  const jsonPath = path.join(REPORT_DIR, `real-user-lifetest-${RUN_ID}.json`);
  const mdPath = path.join(REPORT_DIR, `real-user-lifetest-${RUN_ID}.md`);
  await fs.writeFile(jsonPath, JSON.stringify(report, null, 2), "utf8");
  await fs.writeFile(mdPath, renderMarkdown(report), "utf8");
  console.log(`\nReport JSON: ${path.relative(ROOT, jsonPath)}`);
  console.log(`Report MD:   ${path.relative(ROOT, mdPath)}`);
  return report;
}

function renderMarkdown(report) {
  const lines = [
    `# Orka Real-User Lifetest ${report.runId}`,
    "",
    `- Base URL: ${report.baseUrl}`,
    `- Status: ${report.status}`,
    `- Provider calls: ${report.includeAiProvider ? "enabled" : "disabled"}`,
    `- SQL/Redis readiness was ${report.allowUnready ? "allowed to be warning" : "required"}`,
    `- Summary: ${report.summary.pass} pass, ${report.summary.warn} warn, ${report.summary.fail} fail, ${report.summary.skip} skip`,
    "",
    "## Created Test Data",
    "",
    `- Users: ${report.created.users.map((u) => `${u.slug} (${u.email}, ${u.userRef})`).join(", ") || "none"}`,
    `- Topics: ${report.created.topics.map((t) => `${t.slug}:${t.topicRef}`).join(", ") || "none"}`,
    `- Sources: ${report.created.sources.map((s) => `${s.slug}:${s.sourceRef}`).join(", ") || "none"}`,
    `- Study Rooms: ${report.created.studyRooms.map((s) => `${s.slug}:${s.classroomSessionRef}`).join(", ") || "none"}`,
    "",
    "## Results",
    "",
    "| Area | Check | Status | Detail |",
    "|---|---|---:|---|",
    ...report.results.map((r) => `| ${escapeMd(r.area)} | ${escapeMd(r.name)} | ${r.status} | ${escapeMd(r.detail)} |`),
    "",
    "## Notes",
    "",
    "- The report intentionally stores only metadata and short object refs, not bearer tokens or full response bodies.",
    "- Provider-backed Tutor/Classroom qualitative answer checks run only with --include-ai-provider.",
  ];
  return `${lines.join("\n")}\n`;
}

function escapeMd(value) {
  return String(value ?? "").replace(/\|/g, "\\|").replace(/\n/g, " ");
}

async function main() {
  await preflight();

  const personas = {};
  if (PERSONA_FILTER.includes("new")) {
    personas.new = await registerPersona("new", "New", "New learner diagnostic", "Lifetest");
    if (personas.new) await runNewLearner(personas.new);
  }
  if (PERSONA_FILTER.includes("repair")) {
    personas.repair = await registerPersona("repair", "Repair", "Repeated wrong repair learner", "Lifetest");
    if (personas.repair) await runRepairLearner(personas.repair);
  }
  if (PERSONA_FILTER.includes("evidence-code")) {
    personas.evidenceCode = await registerPersona("evidence-code", "Evidence", "Source exam code learner", "Lifetest");
    if (personas.evidenceCode) await runEvidenceCodeLearner(personas.evidenceCode);
  }

  await comparePersonaBehavior(Object.values(personas));
  await crossUserIsolation(personas.evidenceCode ?? personas.repair, personas.new ?? personas.repair);
  await optionalProviderChecks(personas.repair ?? personas.new ?? personas.evidenceCode);

  const report = await writeReport();
  const failCount = report.summary.fail;
  if (failCount > 0) {
    console.error(`\nReal-user lifetest failed with ${failCount} hard failure(s).`);
    process.exit(1);
  }
  console.log("\nReal-user lifetest completed without hard failures.");
}

main().catch(async (error) => {
  fail("Fatal", "unhandled lifetest error", error instanceof Error ? error.message : String(error));
  await writeReport();
  process.exit(2);
});
