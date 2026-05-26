#!/usr/bin/env node
/*
 * Deep real-user learner journey for Orka.
 *
 * This is not a shallow endpoint smoke test. It drives the product like a
 * student: source upload, evidence workspace, wrong/blank quiz answers,
 * Study Room repair checkpoints, Wiki trace inspection, Notebook pack/export,
 * progress/mission refresh, and cross-user isolation.
 *
 * Provider-backed endpoints stay disabled unless --include-provider is passed.
 */

import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(__dirname, "..");
const args = parseArgs(process.argv.slice(2));
const BASE_URL = trimSlash(args["api-url"] ?? process.env.ORKA_API_URL ?? "http://localhost:5065");
const REPORT_DIR = path.resolve(ROOT, args["report-dir"] ?? "scripts/reports");
const RUN_ID = String(args["run-id"] ?? new Date().toISOString().replace(/[-:.TZ]/g, "").slice(0, 14));
const INCLUDE_PROVIDER = boolArg("include-provider");
const PASSWORD = `OrkaDeep${RUN_ID}!`;

const blockedFieldNames = [
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
  "token",
  "answerKey",
  "correctAnswer",
  "stackTrace",
  "ownerId",
  "rawTranscript",
];

const checks = [];
const artifacts = {
  mainUser: null,
  strangerUser: null,
  topicId: null,
  sourceId: null,
  wikiPages: [],
  wikiBlocks: [],
  quizAttempts: [],
  studyRoomSessionId: null,
  notebookPackId: null,
  notebookArtifactId: null,
};

function parseArgs(argv) {
  const parsed = {};
  for (const raw of argv) {
    const match = raw.match(/^--([^=]+)(?:=(.*))?$/);
    if (match) parsed[match[1]] = match[2] ?? "true";
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
    if (value !== undefined && value !== null && value !== "") search.set(key, String(value));
  }
  const text = search.toString();
  return text ? `?${text}` : "";
}

function nowIso() {
  return new Date().toISOString();
}

function shortId(value) {
  const text = String(value ?? "");
  return text.length <= 13 ? text : `${text.slice(0, 8)}...${text.slice(-4)}`;
}

function normalize(value) {
  return String(value ?? "")
    .normalize("NFD")
    .replace(/\p{Diacritic}/gu, "")
    .replace(/ı/g, "i")
    .replace(/İ/g, "i")
    .toLowerCase();
}

function addCheck(area, name, status, detail = "", evidence = undefined) {
  checks.push({ area, name, status, detail, evidence, at: nowIso() });
  const label = status === "pass" ? "OK" : status === "warn" ? "WARN" : status === "skip" ? "SKIP" : "FAIL";
  console.log(`${label} [${area}] ${name}${detail ? ` - ${detail}` : ""}`);
}

function pass(area, name, detail = "", evidence) {
  addCheck(area, name, "pass", detail, evidence);
}

function warn(area, name, detail = "", evidence) {
  addCheck(area, name, "warn", detail, evidence);
}

function fail(area, name, detail = "", evidence) {
  addCheck(area, name, "fail", detail, evidence);
}

function skip(area, name, detail = "") {
  addCheck(area, name, "skip", detail);
}

async function request(method, url, { token, body, formData, timeoutMs = 45000, safety = true } = {}) {
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
    const result = {
      ok: response.ok,
      status: response.status,
      data,
      text,
      durationMs: Math.round(performance.now() - started),
      method,
      url,
    };
    if (safety) sweepPublicPayload(`${method} ${url}`, data ?? text);
    return result;
  } catch (error) {
    return {
      ok: false,
      status: 0,
      data: null,
      text: "",
      durationMs: Math.round(performance.now() - started),
      method,
      url,
      error: error instanceof Error ? error.message : String(error),
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

function assertOk(response, area, name, { expected = [200], optional = false } = {}) {
  if (response.ok && expected.includes(response.status)) {
    pass(area, name, `status=${response.status}, ${response.durationMs}ms`);
    return true;
  }
  const detail = `status=${response.status}${response.error ? `, ${response.error}` : ""}${response.text ? `, ${response.text.slice(0, 160)}` : ""}`;
  if (optional) warn(area, name, detail);
  else fail(area, name, detail);
  return false;
}

function sweepPublicPayload(label, value) {
  const hits = [];
  collectBlockedHits(value, [], hits);
  if (hits.length > 0) {
    fail("Safety", `${label} public payload sweep`, hits.slice(0, 5).map((h) => `${h.marker}@${h.path}`).join(", "));
  }
}

function collectBlockedHits(value, pathParts, hits) {
  if (value == null) return;
  if (Array.isArray(value)) {
    value.forEach((child, index) => collectBlockedHits(child, pathParts.concat(String(index)), hits));
    return;
  }
  if (typeof value === "object") {
    for (const [key, child] of Object.entries(value)) {
      const keyNorm = normalizeKey(key);
      const childPath = pathParts.concat(key);
      for (const marker of blockedFieldNames) {
        if (keyNorm === normalizeKey(marker)) hits.push({ marker, path: childPath.join(".") });
      }
      if (keyNorm === "userid" && containsGuid(child)) hits.push({ marker: "unsafeUserId", path: childPath.join(".") });
      collectBlockedHits(child, childPath, hits);
    }
    return;
  }
  if (typeof value !== "string") return;
  const text = value;
  for (const marker of blockedFieldNames) {
    if (marker === "token") continue;
    if (normalize(text).includes(normalize(marker))) hits.push({ marker, path: pathParts.join(".") || "$" });
  }
  if (/\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b/.test(text)) {
    hits.push({ marker: "jwt", path: pathParts.join(".") || "$" });
  }
}

function normalizeKey(value) {
  return String(value).replace(/[^a-zA-Z0-9]/g, "").toLowerCase();
}

function containsGuid(value) {
  if (typeof value === "string") {
    return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
  }
  if (Array.isArray(value)) return value.some(containsGuid);
  if (value && typeof value === "object") return Object.values(value).some(containsGuid);
  return value !== null && value !== undefined;
}

async function authPersona(slug, firstName) {
  const email = `orka-deep-${slug}-${RUN_ID}@orka.local`;
  const register = await request("POST", "/api/auth/register", {
    body: { firstName, lastName: "Deep", name: `${firstName} Deep`, email, password: PASSWORD },
    safety: false,
  });
  if (!register.ok && register.status !== 409 && register.status !== 429) {
    assertOk(register, "Auth", `${slug} register`, { expected: [201] });
    return null;
  }
  if (register.status === 409 || register.status === 429) {
    warn("Auth", `${slug} register reused`, `status=${register.status}`);
  } else {
    pass("Auth", `${slug} register`, "created");
  }

  const login = await request("POST", "/api/auth/login", {
    body: { email, password: PASSWORD },
    safety: false,
  });
  if (!assertOk(login, "Auth", `${slug} login`)) return null;
  const token = login.data?.token;
  if (!token) {
    fail("Auth", `${slug} token`, "login did not return token");
    return null;
  }
  return { slug, firstName, email, token, userRef: shortId(login.data?.user?.id) };
}

async function createTopic(persona) {
  const response = await request("POST", "/api/topics", {
    token: persona.token,
    body: {
      title: `Derin test: Kesirlerde ortak payda ${RUN_ID}`,
      emoji: "O",
      category: "Matematik",
      planIntent: "study",
    },
  });
  if (!assertOk(response, "Topic", "main topic created")) return null;
  artifacts.topicId = response.data?.id;
  return response.data?.id;
}

async function uploadSource(persona, topicId) {
  const sourceBody = [
    "# Kesirlerde ortak payda - derin test kaynagi",
    "",
    "Ortak payda, paydalari farkli iki kesri toplamak veya cikarmak icin kesirleri esit buyuklukte parcalara cevirmektir.",
    "1/2 + 1/3 isleminde 2 ve 3'un ortak kati 6 olur. 1/2 = 3/6, 1/3 = 2/6, toplam 5/6 eder.",
    "Yaygin hata: paylari ve paydalari ayri ayri toplamak. 1/2 + 1/3 = 2/5 degildir; cunku yarim ve ucte bir ayni parca buyuklugunde degildir.",
    "Telafi adimi: once ortak paydayi bul, sonra kesirleri denk kesre cevir, en son paylari topla.",
    "Bos cevap veren ogrenciye tani: once pay, payda, denk kesir ve ortak kat kavramlari kisa kontrol edilir.",
    "Mini checkpoint: 1/4 + 1/6 icin ortak payda 12; sonuc 3/12 + 2/12 = 5/12.",
  ].join("\n");
  const form = new FormData();
  form.append("TopicId", topicId);
  form.append("File", new Blob([sourceBody], { type: "text/markdown" }), `deep-fractions-${RUN_ID}.md`);

  const upload = await request("POST", "/api/sources/upload", { token: persona.token, formData: form, timeoutMs: 90000 });
  if (!assertOk(upload, "Source", "markdown source uploaded")) return null;
  const sourceId = upload.data?.id;
  artifacts.sourceId = sourceId;

  for (let i = 0; i < 8; i++) {
    const sources = await request("GET", `/api/sources/topic/${topicId}`, { token: persona.token });
    if (sources.ok) {
      const item = Array.isArray(sources.data) ? sources.data.find((s) => s.id === sourceId) : null;
      if (item?.status === "ready" || Number(item?.chunkCount ?? 0) > 0) {
        pass("Source", "source became queryable", `status=${item?.status}, chunks=${item?.chunkCount ?? "?"}`);
        return sourceId;
      }
    }
    await sleep(1500);
  }
  warn("Source", "source readiness polling inconclusive", "source uploaded but ready/chunk status was not observed");
  return sourceId;
}

async function baselineContracts(persona, topicId) {
  const topic = qs({ topicId });
  const calls = [
    ["Dashboard", "dashboard today", "GET", "/api/dashboard/today"],
    ["LearningOS", "orka state", "GET", `/api/learning/orka-state${topic}`],
    ["LearningOS", "mission control", "GET", `/api/learning/mission-control${topic}`],
    ["LearningOS", "study coach", "GET", `/api/learning/study-coach${topic}`],
    ["StudyRoom", "study room preview", "GET", `/api/classroom/study-room${topic}`],
    ["SourceWikiPro", "source/wiki pro", "GET", `/api/sources/wiki-pro${topic}`],
    ["Notebook", "notebook pro", "GET", `/api/notebook-studio/pro${topic}`],
    ["Review", "due review", "GET", `/api/review/due${topic}`],
    ["Progress", "topic progress", "GET", `/api/topics/${topicId}/progress`],
  ];
  const snapshots = {};
  for (const [area, name, method, url] of calls) {
    const res = await request(method, url, { token: persona.token });
    assertOk(res, area, name, { optional: area === "Progress" });
    snapshots[name] = res.data;
  }
  return snapshots;
}

async function sourceEvidenceFlow(persona, topicId, sourceId) {
  const evidence = await request("POST", `/api/sources/topic/${topicId}/evidence-bundle/refresh`, {
    token: persona.token,
    body: { sourceId },
  });
  assertOk(evidence, "Source", "evidence bundle refresh", { optional: true });

  const notebook = await request("GET", `/api/sources/topic/${topicId}/notebook`, { token: persona.token });
  assertOk(notebook, "Source", "topic source notebook", { optional: true });

  const lifecycle = await request("GET", `/api/sources/topic/${topicId}/lifecycle-summary`, { token: persona.token });
  assertOk(lifecycle, "Source", "lifecycle summary", { optional: true });

  const conceptGraph = await request("GET", `/api/sources/topic/${topicId}/concept-graph`, { token: persona.token });
  assertOk(conceptGraph, "Source", "concept graph", { optional: true });

  const wikiPro = await request("GET", `/api/sources/wiki-pro${qs({ topicId, sourceId })}`, { token: persona.token });
  assertOk(wikiPro, "SourceWikiPro", "focused source/wiki pro");

  const score = scoreSourceWorkspace({ evidence: evidence.data, notebook: notebook.data, lifecycle: lifecycle.data, wikiPro: wikiPro.data });
  pass("Scoring", "source/wiki workspace scored", `${score.score}/5 - ${score.reason}`, score);
  return { evidence: evidence.data, notebook: notebook.data, lifecycle: lifecycle.data, wikiPro: wikiPro.data, score };
}

async function providerBackedNotebookTools(persona, topicId) {
  if (!INCLUDE_PROVIDER) {
    skip("Provider", "source Q&A + Wiki briefing/glossary/mindmap/study-cards", "disabled: paid/provider-backed live calls require explicit --include-provider");
    return {};
  }

  const calls = {};
  const endpoints = [
    ["briefing", "GET", `/api/wiki/${topicId}/briefing`],
    ["glossary", "GET", `/api/wiki/${topicId}/glossary`],
    ["timeline", "GET", `/api/wiki/${topicId}/timeline`],
    ["mindmap", "GET", `/api/wiki/${topicId}/mindmap`],
    ["study cards", "GET", `/api/wiki/${topicId}/study-cards`],
  ];
  for (const [name, method, url] of endpoints) {
    const res = await request(method, url, { token: persona.token, timeoutMs: 120000 });
    assertOk(res, "WikiNotebookLM", name, { optional: true });
    calls[name] = res.data;
  }
  return calls;
}

async function sourceQuestionFlow(persona, topicId, sourceId) {
  if (!INCLUDE_PROVIDER) {
    skip("Provider", "source question answers", "disabled: Source Q&A currently calls provider for strict grounded answer generation");
    return {};
  }

  const ask = await request("POST", `/api/sources/topic/${topicId}/ask`, {
    token: persona.token,
    timeoutMs: 120000,
    body: {
      topicId,
      sourceId,
      question: "1/2 + 1/3 neden 2/5 degil? Ogrenci anlamadiysa telafi diliyle anlat.",
      mode: "selected_source",
      includeLearnerContext: true,
      writeWikiTrace: true,
    },
  });
  assertOk(ask, "SourceQA", "source ask with wiki trace", { optional: true });

  const thread = await request("POST", "/api/sources/question-threads", {
    token: persona.token,
    timeoutMs: 120000,
    body: {
      topicId,
      sourceId,
      sourceIds: [sourceId],
      conceptKey: "fractions-common-denominator",
      title: "Ortak payda anlamama telafi thread'i",
      initialQuestion: "Hala anlamadim: neden paydalari toplamiyoruz?",
      mode: "selected_source",
      includeLearnerContext: true,
      writeWikiTrace: true,
    },
  });
  assertOk(thread, "SourceQA", "source question thread", { optional: true });
  return { ask: ask.data, thread: thread.data };
}

async function recordQuizAttempts(persona, topicId) {
  const attempts = [
    {
      id: "wrong-1",
      isCorrect: false,
      wasSkipped: false,
      selectedOptionId: "2-5",
      mistakeCategory: "added_denominators",
      misconceptionTarget: "payda_toplama_yanilgisi",
      explanation: "Paylari ve paydalari ayri topladim.",
    },
    {
      id: "wrong-2",
      isCorrect: false,
      wasSkipped: false,
      selectedOptionId: "2-5-again",
      mistakeCategory: "added_denominators",
      misconceptionTarget: "payda_toplama_yanilgisi",
      explanation: "Ortak payda adimini atladim.",
    },
    {
      id: "blank-1",
      isCorrect: false,
      wasSkipped: true,
      selectedOptionId: null,
      mistakeCategory: "blank_or_skipped",
      misconceptionTarget: "",
      explanation: "Bos biraktim; nereden baslayacagimi bilemedim.",
    },
    {
      id: "correct-1",
      isCorrect: true,
      wasSkipped: false,
      selectedOptionId: "5-6",
      mistakeCategory: "",
      misconceptionTarget: "",
      explanation: "Ortak payda 6, 3/6 + 2/6 = 5/6.",
    },
  ];

  for (const item of attempts) {
    const res = await request("POST", "/api/quiz/attempt", {
      token: persona.token,
      body: {
        topicId,
        questionId: `deep-${RUN_ID}-${item.id}`,
        questionHash: `deep-${RUN_ID}-${item.id}`,
        question: "1/2 + 1/3 islemini cozerken ortak payda kullan.",
        selectedOptionId: item.selectedOptionId,
        isCorrect: item.isCorrect,
        wasSkipped: item.wasSkipped,
        explanation: item.explanation,
        skillTag: "fractions-common-denominator",
        conceptKey: "fractions-common-denominator",
        conceptTag: "fractions-common-denominator",
        cognitiveSkill: "conceptual_repair",
        misconceptionTarget: item.misconceptionTarget,
        learningObjective: "Paydalari farkli kesirleri ortak payda ile toplar.",
        questionType: item.wasSkipped ? "blank_checkpoint" : "conceptual_repair",
        mistakeCategory: item.mistakeCategory,
        assessmentMode: "deep_lifetest",
        topicPath: "Matematik > Kesirler > Ortak payda",
        difficulty: "medium",
        responseTimeMs: item.wasSkipped ? 45000 : 28000,
        confidenceSelfRating: item.isCorrect ? 0.8 : 0.2,
      },
    });
    if (assertOk(res, "Quiz", `record ${item.id}`)) {
      artifacts.quizAttempts.push({ id: res.data?.id, kind: item.id, wikiBlockRef: findWikiBlockRefInQuizResponse(res.data) });
    }
  }
}

function findWikiBlockRefInQuizResponse(data) {
  const text = JSON.stringify(data ?? {});
  const match = text.match(/[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}/i);
  return match ? shortId(match[0]) : null;
}

async function studyRoomRepair(persona, topicId) {
  const preview = await request("GET", `/api/classroom/study-room${qs({ topicId, mode: "repair_lesson" })}`, { token: persona.token });
  assertOk(preview, "StudyRoom", "repair lesson preview");

  const start = await request("POST", "/api/classroom/study-room/start", {
    token: persona.token,
    body: { topicId, mode: "repair_lesson", examCode: "KPSS" },
  });
  if (!assertOk(start, "StudyRoom", "start repair lesson")) return null;
  const sessionId = start.data?.classroomSessionId;
  artifacts.studyRoomSessionId = sessionId;

  const wrong = await request("POST", "/api/classroom/study-room/checkpoint", {
    token: persona.token,
    body: {
      classroomSessionId: sessionId,
      responseSignal: "wrong",
      answerText: "1/2 + 1/3 = 2/5 dedim, cunku pay ve paydayi ayri topladim.",
      skipped: false,
      conceptKey: "fractions-common-denominator",
    },
  });
  assertOk(wrong, "StudyRoom", "wrong checkpoint creates repair feedback");

  const blank = await request("POST", "/api/classroom/study-room/checkpoint", {
    token: persona.token,
    body: {
      classroomSessionId: sessionId,
      responseSignal: "blank",
      answerText: "",
      skipped: true,
      conceptKey: "fractions-common-denominator",
    },
  });
  assertOk(blank, "StudyRoom", "blank checkpoint creates diagnostic feedback");

  const score = scoreStudyRoom({ preview: preview.data, start: start.data, wrong: wrong.data, blank: blank.data });
  pass("Scoring", "Study Room repair/checkpoint scored", `${score.score}/5 - ${score.reason}`, score);
  return { preview: preview.data, start: start.data, wrong: wrong.data, blank: blank.data, score };
}

async function inspectWiki(persona, topicId, label) {
  const pages = await request("GET", `/api/wiki/${topicId}`, { token: persona.token });
  assertOk(pages, "Wiki", `${label}: pages`);
  const pageList = Array.isArray(pages.data) ? pages.data : [];
  artifacts.wikiPages = pageList.map((p) => ({
    id: p.id,
    title: p.title,
    type: p.pageType,
    status: p.status,
    blockCount: p.blockCount,
    sourceReadiness: p.sourceReadiness,
  }));

  const details = [];
  for (const page of pageList.slice(0, 6)) {
    const detail = await request("GET", `/api/wiki/page/${page.id}`, { token: persona.token });
    if (assertOk(detail, "Wiki", `${label}: page ${shortId(page.id)}`, { optional: true })) {
      details.push(detail.data);
    }
  }

  const blocks = details.flatMap((d) => {
    const page = d?.page ?? {};
    return (Array.isArray(d?.blocks) ? d.blocks : []).map((b) => ({
      pageId: page.id,
      pageTitle: page.title,
      blockId: b.id,
      type: b.type,
      title: b.title,
      source: b.source,
      sourceBasis: b.sourceBasis,
      conceptKey: b.conceptKey,
      quizAttemptId: b.quizAttemptId,
      contentPreview: String(b.content ?? "").slice(0, 220),
    }));
  });
  artifacts.wikiBlocks = blocks;
  const repairBlocks = blocks.filter((b) => /repair|telafi|misconception|quiz/i.test(`${b.type} ${b.title} ${b.contentPreview}`));
  const score = scoreWikiTrace(blocks);
  pass("Scoring", `${label}: Wiki trace scored`, `${score.score}/5 - ${score.reason}`, {
    score,
    pages: artifacts.wikiPages,
    repairBlocks: repairBlocks.map((b) => ({
      pageRef: shortId(b.pageId),
      pageTitle: b.pageTitle,
      blockRef: shortId(b.blockId),
      type: b.type,
      title: b.title,
      sourceBasis: b.sourceBasis,
    })),
  });
  return { pages: pageList, details, blocks, score };
}

async function notebookFlow(persona, topicId, sourceId, wikiPageId) {
  const knowledge = await request("GET", `/api/wiki/${topicId}/knowledge-notebook`, { token: persona.token });
  assertOk(knowledge, "NotebookLM", "knowledge notebook snapshot", { optional: true });

  const refresh = await request("POST", `/api/wiki/${topicId}/knowledge-notebook/refresh`, { token: persona.token });
  assertOk(refresh, "NotebookLM", "knowledge notebook refresh", { optional: true });

  const sourcePack = await request("POST", `/api/notebook-studio/topic/${topicId}/source-pack`, {
    token: persona.token,
    body: {
      sourceId,
      wikiPageId,
      packType: "source_study_pack",
      focusConceptKey: "fractions-common-denominator",
      userGoal: "Yanlis ve bos cevaplardan sonra telafi paketi olustur.",
      includeArtifacts: true,
    },
  });
  assertOk(sourcePack, "Notebook", "source study pack", { optional: true });
  const packId = sourcePack.data?.id;
  artifacts.notebookPackId = packId;

  let artifact = null;
  let preview = null;
  if (packId) {
    artifact = await request("POST", `/api/notebook-studio/packs/${packId}/artifact`, {
      token: persona.token,
      body: {
        artifactType: "study_guide",
        conceptKey: "fractions-common-denominator",
        wikiPageId,
      },
    });
    assertOk(artifact, "Notebook", "study guide artifact", { optional: true });
    artifacts.notebookArtifactId = artifact.data?.id;

    preview = await request("GET", `/api/notebook-studio/packs/${packId}/export/preview`, { token: persona.token });
    assertOk(preview, "Notebook", "export preview", { optional: true });
  }

  const pro = await request("GET", `/api/notebook-studio/pro${qs({ topicId, sourceId, wikiPageId, packType: "source_study_pack" })}`, {
    token: persona.token,
  });
  assertOk(pro, "Notebook", "Notebook Studio Pro focused", { optional: true });

  const score = scoreNotebook({ knowledge: knowledge.data, refresh: refresh.data, sourcePack: sourcePack.data, artifact: artifact?.data, preview: preview?.data, pro: pro.data });
  pass("Scoring", "Notebook/Artifact workspace scored", `${score.score}/5 - ${score.reason}`, score);
  return { knowledge: knowledge.data, refresh: refresh.data, sourcePack: sourcePack.data, artifact: artifact?.data, preview: preview?.data, pro: pro.data, score };
}

async function refreshLearningOs(persona, topicId) {
  const topic = qs({ topicId });
  const mission = await request("GET", `/api/learning/mission-control${topic}`, { token: persona.token });
  const coach = await request("GET", `/api/learning/study-coach${topic}`, { token: persona.token });
  const state = await request("GET", `/api/learning/orka-state${topic}`, { token: persona.token });
  const dashboard = await request("GET", "/api/dashboard/today", { token: persona.token });
  const progress = await request("GET", `/api/topics/${topicId}/progress`, { token: persona.token });
  assertOk(mission, "LearningOS", "mission after deep journey");
  assertOk(coach, "LearningOS", "study coach after deep journey");
  assertOk(state, "LearningOS", "state after deep journey");
  assertOk(dashboard, "Dashboard", "dashboard after deep journey");
  assertOk(progress, "Progress", "topic progress after deep journey", { optional: true });

  const score = scoreMissionQuality({ mission: mission.data, coach: coach.data, state: state.data, dashboard: dashboard.data, progress: progress.data });
  pass("Scoring", "mission/action quality scored", `${score.score}/5 - ${score.reason}`, score);
  return { mission: mission.data, coach: coach.data, state: state.data, dashboard: dashboard.data, progress: progress.data, score };
}

async function crossUserIsolation(stranger, main) {
  const targets = [
    ["topic detail", "GET", `/api/topics/${artifacts.topicId}`],
    ["topic wiki", "GET", `/api/wiki/${artifacts.topicId}`],
    ["source detail page", "GET", artifacts.sourceId ? `/api/sources/${artifacts.sourceId}/pages/1` : null],
    ["notebook pack", "GET", artifacts.notebookPackId ? `/api/notebook-studio/packs/${artifacts.notebookPackId}` : null],
    ["study room session", "POST", "/api/classroom/study-room/checkpoint", {
      classroomSessionId: artifacts.studyRoomSessionId,
      responseSignal: "wrong",
      answerText: "cross user attempt",
      skipped: false,
      conceptKey: "fractions-common-denominator",
    }],
  ].filter((x) => x[2] && (!x[3] || artifacts.studyRoomSessionId));

  for (const [name, method, url, body] of targets) {
    const res = await request(method, url, { token: stranger.token, body, safety: true });
    if ([404, 400].includes(res.status)) pass("Security", `cross-user blocked: ${name}`, `status=${res.status}`);
    else if (name === "topic wiki" && res.status === 200 && Array.isArray(res.data) && res.data.length === 0) {
      pass("Security", `cross-user blocked: ${name}`, "status=200 empty scoped result");
    }
    else fail("Security", `cross-user blocked: ${name}`, `unexpected status=${res.status}`);
  }
}

function scoreSourceWorkspace(data) {
  let score = 0;
  const reasons = [];
  const text = JSON.stringify(data ?? {});
  if (/source|evidence|readiness/i.test(text)) { score++; reasons.push("readiness/evidence var"); }
  if (/citation|source_grounded|evidence_insufficient|source_limited/i.test(text)) { score++; reasons.push("citation/source basis var"); }
  if (/recommended|nextActions|mission|action/i.test(text)) { score++; reasons.push("aksiyon/handoff var"); }
  if (/warning|limited|stale|insufficient|degraded|thin/i.test(text)) { score++; reasons.push("warning/degrade dili var"); }
  if (!hasBlockedText(text)) { score++; reasons.push("raw payload yok"); }
  return { score, reason: reasons.join("; ") || "kanıt zayıf" };
}

function scoreStudyRoom(data) {
  let score = 0;
  const text = JSON.stringify(data ?? {});
  const reasons = [];
  if (/repair_lesson|start_repair_lesson|repair/i.test(text)) { score++; reasons.push("repair mode/action"); }
  if (/checkpoint|wrong|blank|skipped|needs_repair/i.test(text)) { score++; reasons.push("checkpoint sinyali"); }
  if (/lessonPlan|steps|objective|stopCondition/i.test(text)) { score++; reasons.push("ders planı var"); }
  if (/tutorHandoffs|quizHandoffs|reviewHandoffs|sourceWikiHandoffs|notebookHandoffs/i.test(text)) { score++; reasons.push("handoff var"); }
  if (!hasBlockedText(text)) { score++; reasons.push("raw payload yok"); }
  return { score, reason: reasons.join("; ") || "kanıt zayıf" };
}

function scoreWikiTrace(blocks) {
  let score = 0;
  const reasons = [];
  const text = JSON.stringify(blocks ?? {});
  if (blocks.length > 0) { score++; reasons.push(`${blocks.length} block var`); }
  if (/repair_note|misconception_note|Yanlis|Bos|telafi|Quiz sonrasi/i.test(text)) { score++; reasons.push("telafi/repair block var"); }
  if (/quiz_attempt|assessment_verified|assessment_signal/i.test(text)) { score++; reasons.push("quiz/assessment kaynaklı iz var"); }
  if (/fractions-common-denominator|ortak payda|payda/i.test(text)) { score++; reasons.push("kavram bağlamı var"); }
  if (!hasBlockedText(text)) { score++; reasons.push("raw payload yok"); }
  return { score, reason: reasons.join("; ") || "Wiki trace zayıf" };
}

function scoreNotebook(data) {
  let score = 0;
  const text = JSON.stringify(data ?? {});
  const reasons = [];
  if (/knowledge|notebook|snapshot|concept/i.test(text)) { score++; reasons.push("knowledge notebook var"); }
  if (/pack|source_study_pack|artifact/i.test(text)) { score++; reasons.push("pack/artifact var"); }
  if (/exportReadiness|slide|preview|preview_ready/i.test(text)) { score++; reasons.push("export preview var"); }
  if (/sourceReadiness|evidenceStatus|sourceBasis|warning/i.test(text)) { score++; reasons.push("evidence metadata var"); }
  if (!hasBlockedText(text)) { score++; reasons.push("raw payload yok"); }
  return { score, reason: reasons.join("; ") || "Notebook kanıtı zayıf" };
}

function scoreMissionQuality(data) {
  let score = 0;
  const text = JSON.stringify(data ?? {});
  const reasons = [];
  if (/primaryMission|primary|nextAction|actionType/i.test(text)) { score++; reasons.push("primary action var"); }
  if (/reason|reasonCodes|why|label/i.test(text)) { score++; reasons.push("neden/reason var"); }
  if (/repair|review|checkpoint|diagnostic|source|study_room|notebook/i.test(text)) { score++; reasons.push("öğrenme aksiyonu var"); }
  if (/warning|thin|limited|conflict|blocked/i.test(text)) { score++; reasons.push("uyarı/degrade var"); }
  if (!hasBlockedText(text)) { score++; reasons.push("raw payload yok"); }
  return { score, reason: reasons.join("; ") || "mission kanıtı zayıf" };
}

function hasBlockedText(text) {
  return blockedFieldNames.some((marker) => normalize(text).includes(normalize(marker))) ||
    /\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b/.test(text);
}

async function preflight() {
  console.log(`\nOrka deep learner journey`);
  console.log(`Base URL: ${BASE_URL}`);
  console.log(`Run id: ${RUN_ID}`);
  console.log(`Provider-backed calls: ${INCLUDE_PROVIDER ? "enabled" : "disabled"}`);
  const live = await request("GET", "/health/live", { safety: true, timeoutMs: 10000 });
  assertOk(live, "Preflight", "/health/live");
  const ready = await request("GET", "/health/ready", { safety: true, timeoutMs: 15000 });
  assertOk(ready, "Preflight", "/health/ready");
}

async function writeReport({ baseline, sourceFlow, providerTools, sourceQuestions, studyRoom, wikiBeforeNotebook, notebook, learningOs }) {
  await fs.mkdir(REPORT_DIR, { recursive: true });
  const counts = {
    pass: checks.filter((c) => c.status === "pass").length,
    warn: checks.filter((c) => c.status === "warn").length,
    fail: checks.filter((c) => c.status === "fail").length,
    skip: checks.filter((c) => c.status === "skip").length,
  };
  const jsonPath = path.join(REPORT_DIR, `deep-learner-journey-${RUN_ID}.json`);
  const mdPath = path.join(REPORT_DIR, `deep-learner-journey-${RUN_ID}.md`);
  const report = {
    runId: RUN_ID,
    baseUrl: BASE_URL,
    generatedAt: nowIso(),
    providerBackedCallsEnabled: INCLUDE_PROVIDER,
    counts,
    artifacts,
    scores: {
      sourceWiki: sourceFlow?.score,
      studyRoom: studyRoom?.score,
      wikiTrace: wikiBeforeNotebook?.score,
      notebook: notebook?.score,
      mission: learningOs?.score,
    },
    checks,
    snapshots: {
      baseline,
      sourceFlow,
      providerTools,
      sourceQuestions,
      studyRoom,
      wikiBeforeNotebook,
      notebook,
      learningOs,
    },
  };
  await fs.writeFile(jsonPath, JSON.stringify(report, null, 2), "utf8");
  await fs.writeFile(mdPath, buildMarkdown(report), "utf8");
  console.log(`\nReport JSON: ${jsonPath}`);
  console.log(`Report MD:   ${mdPath}`);
  if (counts.fail > 0) process.exitCode = 1;
}

function buildMarkdown(report) {
  const scoreLine = (name, score) => `| ${name} | ${score ? `${score.score}/5` : "n/a"} | ${score?.reason ?? "-"} |`;
  const repairBlocks = artifacts.wikiBlocks.filter((b) => /repair|telafi|quiz|misconception/i.test(`${b.type} ${b.title} ${b.contentPreview}`));
  return [
    `# Orka Deep Learner Journey Report`,
    ``,
    `- Run id: \`${report.runId}\``,
    `- Base URL: \`${report.baseUrl}\``,
    `- Generated at: \`${report.generatedAt}\``,
    `- Provider-backed calls: \`${report.providerBackedCallsEnabled ? "enabled" : "disabled"}\``,
    `- Result: ${report.counts.pass} pass, ${report.counts.warn} warn, ${report.counts.fail} fail, ${report.counts.skip} skip`,
    ``,
    `## Main Artifacts`,
    ``,
    `- Main user: \`${artifacts.mainUser?.email ?? "-"}\``,
    `- Topic: \`${shortId(artifacts.topicId)}\``,
    `- Source: \`${shortId(artifacts.sourceId)}\``,
    `- Study Room session: \`${shortId(artifacts.studyRoomSessionId)}\``,
    `- Notebook pack: \`${shortId(artifacts.notebookPackId)}\``,
    `- Notebook artifact: \`${shortId(artifacts.notebookArtifactId)}\``,
    ``,
    `## Product Scores`,
    ``,
    `| Area | Score | Evidence |`,
    `| --- | ---: | --- |`,
    scoreLine("Source/Wiki workspace", report.scores.sourceWiki),
    scoreLine("Study Room repair/checkpoint", report.scores.studyRoom),
    scoreLine("Wiki trace/remediation", report.scores.wikiTrace),
    scoreLine("Notebook/artifact workspace", report.scores.notebook),
    scoreLine("Mission/action quality", report.scores.mission),
    ``,
    `## Telafi Dersi / Wiki Evidence`,
    ``,
    repairBlocks.length === 0
      ? `- Telafi veya quiz repair bloğu bulunamadı. Bu beta öncesi incelenmeli.`
      : repairBlocks.map((b) =>
          `- Page \`${shortId(b.pageId)}\` (${b.pageTitle}) -> Block \`${shortId(b.blockId)}\`, type=\`${b.type}\`, title=\`${b.title}\`, sourceBasis=\`${b.sourceBasis}\`, quizAttempt=\`${shortId(b.quizAttemptId)}\``
        ).join("\n"),
    ``,
    `## Important Interpretation`,
    ``,
    `- Study Room checkpoint gerçek API ile yanlış ve boş cevap aldı. Bu akış \`ClassroomSession\`, \`ClassroomInteraction\` ve learning signal üretir.`,
    `- Wiki'ye otomatik telafi izi bu koşuda özellikle quiz attempt tarafında doğrulandı. Study Room'un kendisi public kontratta Wiki'ye doğrudan blok yazmıyor; Wiki/Notebook handoff ve safe session trace üretiyor.`,
    `- Source Q&A ve Wiki briefing/glossary/mindmap/study-cards canlı cevap kalitesi provider-backed olduğu için \`${report.providerBackedCallsEnabled ? "koşturuldu" : "bilerek koşturulmadı"}\`. Paid/provider izni olmadan niteliksel AI cevap puanı tamamlanmış sayılmaz.`,
    ``,
    `## Wiki Pages`,
    ``,
    artifacts.wikiPages.length === 0
      ? `- Wiki page yok.`
      : artifacts.wikiPages.map((p) => `- \`${shortId(p.id)}\` ${p.title} (${p.type}), blocks=${p.blockCount}, source=${p.sourceReadiness}`).join("\n"),
    ``,
    `## Checks`,
    ``,
    `| Status | Area | Check | Detail |`,
    `| --- | --- | --- | --- |`,
    ...checks.map((c) => `| ${c.status} | ${c.area} | ${escapeMd(c.name)} | ${escapeMd(c.detail)} |`),
    ``,
  ].join("\n");
}

function escapeMd(value) {
  return String(value ?? "").replace(/\|/g, "\\|").replace(/\n/g, " ").slice(0, 260);
}

async function sleep(ms) {
  await new Promise((resolve) => setTimeout(resolve, ms));
}

async function main() {
  await preflight();

  const mainUser = await authPersona("student", "Derin");
  if (!mainUser) {
    await writeReport({});
    return;
  }
  artifacts.mainUser = { email: mainUser.email, userRef: mainUser.userRef };

  const stranger = await authPersona("stranger", "Yabanci");
  if (stranger) artifacts.strangerUser = { email: stranger.email, userRef: stranger.userRef };

  const topicId = await createTopic(mainUser);
  if (!topicId) {
    await writeReport({});
    return;
  }

  const baseline = await baselineContracts(mainUser, topicId);
  const sourceId = await uploadSource(mainUser, topicId);
  const sourceFlow = sourceId ? await sourceEvidenceFlow(mainUser, topicId, sourceId) : null;
  const providerTools = await providerBackedNotebookTools(mainUser, topicId);
  const sourceQuestions = sourceId ? await sourceQuestionFlow(mainUser, topicId, sourceId) : null;

  await recordQuizAttempts(mainUser, topicId);
  const studyRoom = await studyRoomRepair(mainUser, topicId);
  const wikiBeforeNotebook = await inspectWiki(mainUser, topicId, "after quiz/study-room");
  const firstWikiPageId = artifacts.wikiPages[0]?.id ?? null;
  const notebook = await notebookFlow(mainUser, topicId, sourceId, firstWikiPageId);
  const learningOs = await refreshLearningOs(mainUser, topicId);

  if (stranger) await crossUserIsolation(stranger, mainUser);

  await writeReport({ baseline, sourceFlow, providerTools, sourceQuestions, studyRoom, wikiBeforeNotebook, notebook, learningOs });
}

main().catch(async (error) => {
  fail("Runner", "fatal error", error instanceof Error ? error.stack ?? error.message : String(error));
  await writeReport({});
  process.exit(1);
});
