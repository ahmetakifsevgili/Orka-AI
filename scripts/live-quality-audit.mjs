#!/usr/bin/env node
/*
 * Orka live pedagogical quality audit.
 *
 * This runner is intentionally additive: it does not change backend behavior.
 * It talks to a running API and scores whether the learning product is truly
 * useful, not merely HTTP-green.
 */

import crypto from "node:crypto";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(__dirname, "..");
const args = parseArgs(process.argv.slice(2));

const BASE_URL = trimSlash(args["base-url"] ?? args["api-url"] ?? process.env.ORKA_API_URL ?? "http://localhost:5065");
const REPORT_PATH = path.resolve(ROOT, args["report"] ?? "life_tests/reports/live-quality-audit-report.md");
const INCLUDE_AI_PROVIDER = boolArg("include-ai-provider");
const RUN_ID = String(args["run-id"] ?? new Date().toISOString().replace(/[-:.TZ]/g, "").slice(0, 14));
const PASSWORD = `OrkaQuality${RUN_ID}!`;
const PERSONA_FILTER = String(args.personas ?? "")
  .split(",")
  .map((x) => x.trim())
  .filter(Boolean);

const AREA_MAX = {
  plan: 20,
  diagnostic: 20,
  tutor: 20,
  remediation: 15,
  wiki: 15,
  coherence: 10,
};

const CRITICAL_CODES = new Set([
  "answer_key_leak",
  "cross_user_data_leak",
  "raw_internal_payload_leak",
  "generic_plan_ready",
  "blank_marked_mastery",
  "tutor_mastery_guarantee",
  "tool_policy_bypass",
  "hallucinated_source_citation",
]);

const LEAK_PATTERNS = [
  /thoughtSignature|thought_signature/i,
  /(?:"apiKey"\s*:|"api_key"\s*:|"api-key"\s*:|api[_-]?key\s*=)/i,
  /Authorization|Bearer\s+[A-Za-z0-9._-]+/i,
  /rawProvider|rawProviderPayload|rawToolPayload|rawSourceChunk/i,
  /stackTrace|System\.[A-Za-z]+Exception|Traceback \(most recent call last\)/i,
  /(?<![A-Za-z])[A-Z]:\\\\(?:Users|Windows|Program Files|ProgramData|Orka|repo|workspace|[A-Za-z0-9_. -]+\\\\)/i,
  /"answerKey"\s*:/i,
  /"correctAnswer"\s*:/i,
  /"isCorrect"\s*:\s*true/i,
];

const OVERCLAIM_PATTERNS = [
  /100%\s*(guarantee|guaranteed|master|success)/i,
  /guarantee(?:d|ing)?\s+(?:perfect|mastery|success|exam)/i,
  /kesin\s+ba[sş]ar/i,
  /tam\s+garanti/i,
];

const PERSONAS = [
  {
    slug: "SQL_Index_NewLearner",
    title: "SQL Indexes and Query Optimization",
    category: "Database Engineering",
    behavior: "choose_first",
    prompt: "I want to learn SQL indexes and query optimization professionally.",
    tutorPrompt: "I don't understand how an index changes a query plan. Explain simply and check me.",
    requiredTerms: ["sql", "index", "indeks", "query", "sorgu"],
    weakTerms: ["index", "query", "selectivity", "cardinality", "execution"],
    expectRemediation: true,
  },
  {
    slug: "SQL_Cardinality_Gap",
    title: "SQL Cardinality and Query Plans",
    category: "Database Engineering",
    behavior: "mixed_blank",
    prompt: "I understand basic indexes, but I struggle with cardinality, selectivity, and execution plans.",
    tutorPrompt: "I keep mixing up selectivity and cardinality. Can you repair that with one example?",
    requiredTerms: ["sql", "cardinality", "selectivity", "secicilik"],
    weakTerms: ["cardinality", "selectivity", "plan", "optimizer"],
    expectRemediation: true,
  },
  {
    slug: "Async_Misconception",
    title: "Python Async Programming",
    category: "Software Engineering",
    behavior: "choose_first",
    prompt: "I think Task.Result is the safest way to make async code finish before continuing. Test me and fix the misconception.",
    tutorPrompt: "I still think blocking on Task.Result is safe. Why is that wrong? Give me a micro-check.",
    requiredTerms: ["async", "task", "blocking", "gorev", "engelleme"],
    weakTerms: ["async", "blocking", "deadlock", "await", "task"],
    expectRemediation: true,
  },
  {
    slug: "Blank_Skip_Learner",
    title: "SQL Query Optimization Foundations",
    category: "Database Engineering",
    behavior: "skip_all",
    prompt: "I do not know where to start with SQL query optimization. I may leave diagnostic questions blank.",
    tutorPrompt: "I skipped most of the questions because I don't know the basics. Start from prerequisites.",
    requiredTerms: ["sql", "query", "sorgu", "optimization", "optimizasyon"],
    weakTerms: ["basic", "prerequisite", "foundation", "index"],
    expectRemediation: true,
    blankLearner: true,
  },
  {
    slug: "History_Source_Learner",
    title: "Industrial Revolution Global History",
    category: "Humanities",
    behavior: "mixed",
    prompt: "I am studying the global impact of the Industrial Revolution and need source-aware explanations without invented citations.",
    tutorPrompt: "Can you explain the global impact using only evidence you actually have? If evidence is weak, say so.",
    requiredTerms: ["industrial", "revolution", "global"],
    weakTerms: ["industrial", "revolution", "global", "source"],
    expectRemediation: false,
    sourceSensitive: true,
  },
  {
    slug: "Calculus_Substitution_Gap",
    title: "Integral Substitution and Accumulation",
    category: "Mathematics",
    behavior: "mixed_blank",
    prompt: "I can do basic derivatives, but I get lost when integrals require substitution or interpreting accumulation.",
    tutorPrompt: "I do not understand when to use u-substitution. Repair it with one worked example and a micro-check.",
    requiredTerms: ["integral", "substitution", "accumulation", "u-substitution", "area"],
    weakTerms: ["substitution", "accumulation", "antiderivative", "chain", "area"],
    expectRemediation: true,
  },
  {
    slug: "Biology_CellResp_Source",
    title: "Cellular Respiration and Energy Transfer",
    category: "Biology",
    behavior: "mixed",
    prompt: "I want to learn cellular respiration with source-aware explanations and no invented claims about biology.",
    tutorPrompt: "Explain how glycolysis and oxidative phosphorylation connect. If evidence is limited, say so and check my understanding.",
    requiredTerms: ["cellular", "respiration", "glycolysis", "atp", "energy"],
    weakTerms: ["glycolysis", "oxidative", "phosphorylation", "electron", "atp"],
    expectRemediation: true,
    sourceSensitive: true,
  },
  {
    slug: "English_B2_Writing_Coherence",
    title: "English B2 Essay Writing Coherence",
    category: "Language Learning",
    behavior: "choose_first",
    prompt: "I want to improve B2 English essay writing, especially coherence, cohesion, and argument structure.",
    tutorPrompt: "My essays feel disconnected. Teach me how to improve coherence with a short example and a micro-check.",
    requiredTerms: ["english", "essay", "coherence", "cohesion", "argument"],
    weakTerms: ["coherence", "cohesion", "thesis", "paragraph", "transition"],
    expectRemediation: true,
  },
  {
    slug: "YKS_Physics_Circuits",
    title: "YKS Physics Electric Circuits",
    category: "Exam Prep",
    behavior: "mixed_blank",
    prompt: "I am preparing for YKS physics and struggle with electric circuits, current, voltage, and resistance problems.",
    tutorPrompt: "I confuse current and voltage in circuit questions. Repair the misconception with one exam-style example.",
    requiredTerms: ["physics", "circuit", "current", "voltage", "resistance"],
    weakTerms: ["current", "voltage", "resistance", "ohm", "series"],
    expectRemediation: true,
  },
  {
    slug: "Product_Metrics_Cohort_Gap",
    title: "Product Analytics Cohorts and Retention",
    category: "Product Management",
    behavior: "mixed",
    prompt: "I want to learn product analytics professionally, especially cohorts, retention, activation, and metric tradeoffs.",
    tutorPrompt: "I keep mixing activation and retention metrics. Repair that with a product example and one check question.",
    requiredTerms: ["product", "cohort", "retention", "activation", "metrics"],
    weakTerms: ["cohort", "retention", "activation", "funnel", "north"],
    expectRemediation: true,
  },
];

const selectedPersonas = PERSONA_FILTER.length
  ? PERSONAS.filter((p) => PERSONA_FILTER.some((needle) => p.slug.toLowerCase().includes(needle.toLowerCase())))
  : PERSONAS;

const audit = {
  runId: RUN_ID,
  baseUrl: BASE_URL,
  includeAiProvider: INCLUDE_AI_PROVIDER,
  startedAt: new Date().toISOString(),
  personas: [],
  criticalFailures: [],
};

let currentClientIp = null;

main().catch(async (error) => {
  console.error(`FATAL live-quality-audit error: ${error?.stack ?? error}`);
  audit.fatalError = String(error?.message ?? error);
  await writeReport(false);
  process.exit(2);
});

async function main() {
  console.log(`Orka Live Pedagogical Quality Audit`);
  console.log(`Run: ${RUN_ID}`);
  console.log(`Target: ${BASE_URL}`);
  console.log(`Personas: ${selectedPersonas.map((p) => p.slug).join(", ")}`);

  if (selectedPersonas.length === 0) {
    throw new Error(`No personas matched --personas=${PERSONA_FILTER.join(",")}`);
  }

  await preflight();

  for (const [index, persona] of selectedPersonas.entries()) {
    currentClientIp = `127.30.${Number(RUN_ID.slice(-4, -2) || 0) % 240}.${index + 20}`;
    audit.personas.push(await runPersona(persona));
  }

  const releasePass = computeReleasePass();
  await writeReport(releasePass);
  printSummary(releasePass);
  process.exit(releasePass ? 0 : 1);
}

async function preflight() {
  const health = await request("GET", "/health", { safety: true, optionalStatus: [200, 404] });
  if (!health.ok) {
    const apiHealth = await request("GET", "/api/health", { safety: true, optionalStatus: [200] });
    if (!apiHealth.ok) {
      throw new Error(`API health check failed: /health=${health.status}, /api/health=${apiHealth.status}`);
    }
  }
}

async function runPersona(persona) {
  console.log(`\n== ${persona.slug} ==`);
  const ctx = {
    persona,
    email: `orka-quality-${slugify(persona.slug)}-${RUN_ID}@orka.local`,
    token: null,
    topicId: null,
    planRequestId: null,
    quizRunId: null,
    questions: [],
    evidence: {},
    issues: [],
    scores: {
      plan: 0,
      diagnostic: 0,
      tutor: 0,
      remediation: 0,
      wiki: 0,
      coherence: 0,
    },
  };

  await registerLoginAndTopic(ctx);
  await analyzeIntent(ctx);
  await startDiagnostic(ctx);
  await submitDiagnostic(ctx);
  await finalizePlan(ctx);
  await inspectPlan(ctx);
  await inspectTutor(ctx);
  await inspectRemediation(ctx);
  await inspectWikiAndQuestionBank(ctx);
  inspectCoherence(ctx);

  ctx.totalScore = sum(Object.values(ctx.scores));
  ctx.releasePass = ctx.totalScore >= 85 &&
    ctx.issues.every((i) => !i.critical) &&
    Object.entries(AREA_MAX).every(([area, max]) => ctx.scores[area] >= max * 0.75);

  console.log(`${persona.slug}: score=${ctx.totalScore}/100, releasePass=${ctx.releasePass}`);
  return {
    slug: persona.slug,
    title: persona.title,
    scores: ctx.scores,
    totalScore: ctx.totalScore,
    releasePass: ctx.releasePass,
    evidence: ctx.evidence,
    issues: ctx.issues,
  };
}

async function registerLoginAndTopic(ctx) {
  const register = await request("POST", "/api/auth/register", {
    body: {
      firstName: ctx.persona.slug.slice(0, 32),
      lastName: "QualityAudit",
      name: `${ctx.persona.slug} QualityAudit`,
      email: ctx.email,
      password: PASSWORD,
    },
    safety: false,
    timeoutMs: 30000,
  });
  recordEvidence(ctx, "auth.register", register, false);
  if (!register.ok && register.status !== 409 && register.status !== 429) {
    addIssue(ctx, "coherence", "auth_register_failed", `register status ${register.status}`, 0, true);
    return;
  }

  const login = await request("POST", "/api/auth/login", {
    body: { email: ctx.email, password: PASSWORD },
    safety: false,
    timeoutMs: 30000,
  });
  recordEvidence(ctx, "auth.login", login, false);
  if (!login.ok || !login.data?.token) {
    addIssue(ctx, "coherence", "auth_login_failed", `login status ${login.status}`, 0, true);
    return;
  }
  ctx.token = login.data.token;

  const topic = await authed(ctx, "POST", "/api/topics", {
    title: `${ctx.persona.title} ${RUN_ID}`,
    emoji: "O",
    category: ctx.persona.category,
  });
  recordEvidence(ctx, "topic.create", topic);
  if (!topic.ok || !topic.data?.id) {
    addIssue(ctx, "coherence", "topic_create_failed", `topic status ${topic.status}`, 0, true);
    return;
  }
  ctx.topicId = topic.data.id;
  addPoints(ctx, "coherence", 1, "auth/topic created");
}

async function analyzeIntent(ctx) {
  if (!ctx.topicId) return;
  const res = await authed(ctx, "POST", "/api/quiz/plan-diagnostic/intent", {
    rawRequest: ctx.persona.prompt,
    topicId: ctx.topicId,
    existingTopicTitle: ctx.persona.title,
  }, 60000);
  recordEvidence(ctx, "intent.analysis", res);
  if (!res.ok || !res.data) {
    addIssue(ctx, "plan", "intent_endpoint_failed", `status ${res.status}`, 0, true);
    return;
  }

  ctx.intent = res.data;
  const haystack = normalizeText([
    res.data.mainTopic,
    res.data.focusArea,
    res.data.studyGoal,
    res.data.researchIntent,
  ].join(" "));
  const termHits = ctx.persona.requiredTerms.filter((term) => haystack.includes(normalizeText(term))).length;
  if (res.data.intentRequestId) addPoints(ctx, "plan", 1, "intent id");
  if (res.data.mainTopic && res.data.focusArea && res.data.studyGoal) addPoints(ctx, "plan", 2, "intent schema");
  if (termHits >= Math.min(2, ctx.persona.requiredTerms.length)) {
    addPoints(ctx, "plan", 2, "intent domain terms");
  } else {
    addIssue(ctx, "plan", "intent_topic_drift", `missing domain terms in intent: ${ctx.persona.requiredTerms.join(", ")}`, 0);
  }
}

async function startDiagnostic(ctx) {
  if (!ctx.topicId) return;
  const intent = ctx.intent ?? {};
  const startRes = await authed(ctx, "POST", "/api/quiz/plan-diagnostic/start-async", {
    topicId: ctx.topicId,
    topicTitle: ctx.persona.title,
    intentRequestId: intent.intentRequestId,
    approvedMainTopic: intent.mainTopic ?? ctx.persona.title,
    approvedFocusArea: intent.focusArea ?? ctx.persona.title,
    approvedStudyGoal: intent.studyGoal ?? "professional learning",
    approvedResearchIntent: intent.researchIntent ?? ctx.persona.prompt,
    rawStudyRequest: intent.rawRequest ?? ctx.persona.prompt,
  }, 30000);
  if (!startRes.ok || !startRes.data?.planRequestId) {
    recordEvidence(ctx, "diagnostic.start", startRes);
    addIssue(ctx, "diagnostic", "diagnostic_start_failed", `status ${startRes.status}`, 0, true);
    return;
  }

  let res = startRes;
  for (let i = 0; i < 80; i += 1) {
    if (res.data?.isReady || String(res.data?.status ?? "") === "QuizPending" || String(res.data?.status ?? "") === "Failed") {
      break;
    }

    await sleep(3000);
    res = await authed(ctx, "GET", `/api/quiz/plan-diagnostic/${startRes.data.planRequestId}/status`, null, 30000);
    if (!res.ok || !res.data) {
      break;
    }
  }

  recordEvidence(ctx, "diagnostic.start", res);
  if (!res.ok || !res.data) {
    addIssue(ctx, "diagnostic", "diagnostic_start_failed", `status ${res.status}`, 0, true);
    return;
  }

  if (String(res.data.status ?? "") === "Failed") {
    addIssue(ctx, "diagnostic", "diagnostic_start_failed", "status Failed", 0, true);
    return;
  }

  if (!res.data.isReady && String(res.data.status ?? "") !== "QuizPending") {
    addIssue(ctx, "diagnostic", "diagnostic_start_not_ready", `status ${res.data.status ?? "unknown"}`, 0, true);
    return;
  }

  ctx.planRequestId = res.data.planRequestId;
  ctx.quizRunId = res.data.quizRunId;
  ctx.korteks = {
    sourceCount: Number(res.data.sourceCount ?? 0),
    synthesisStatus: res.data.korteksSynthesisStatus,
    sourceConfidence: res.data.korteksSourceConfidence,
  };

  const rawQuestionsJson = String(res.data.questionsJson ?? "[]");
  if (LEAK_PATTERNS.some((pattern) => pattern.test(rawQuestionsJson))) {
    addIssue(ctx, "diagnostic", "answer_key_leak", "diagnostic questions leaked answers/internal markers", 0, true);
  } else {
    addPoints(ctx, "diagnostic", 3, "no answer key leak");
  }

  try {
    ctx.questions = JSON.parse(rawQuestionsJson);
  } catch {
    ctx.questions = [];
  }

  scoreDiagnosticQuestions(ctx);
  scoreKorteksFromDiagnostic(ctx);
}

function scoreDiagnosticQuestions(ctx) {
  const questions = Array.isArray(ctx.questions) ? ctx.questions : [];
  if (questions.length >= 15 && questions.length <= 25) addPoints(ctx, "diagnostic", 3, `question count ${questions.length}`);
  else addIssue(ctx, "diagnostic", "diagnostic_count_out_of_range", `question count ${questions.length}`, 0);

  const ids = questions.map(questionId).filter(Boolean);
  if (ids.length === questions.length && new Set(ids).size === ids.length) addPoints(ctx, "diagnostic", 2, "unique ids");
  else addIssue(ctx, "diagnostic", "diagnostic_duplicate_or_missing_ids", "question ids missing/duplicate", 0);

  const complete = questions.filter((q) => questionStem(q).length >= 12 && questionOptions(q).length >= 2).length;
  if (questions.length > 0 && complete / questions.length >= 0.9) addPoints(ctx, "diagnostic", 3, "complete stems/options");
  else addIssue(ctx, "diagnostic", "diagnostic_incomplete_items", `${complete}/${questions.length} complete`, 0);

  const conceptTags = questions.map(conceptTag).filter(Boolean);
  if (conceptTags.length >= Math.max(5, Math.ceil(questions.length * 0.6)) && new Set(conceptTags.map(normalizeText)).size >= 3) {
    addPoints(ctx, "diagnostic", 3, "concept diversity");
  } else {
    addIssue(ctx, "diagnostic", "diagnostic_weak_concept_tags", `${conceptTags.length} concept tags`, 0);
  }

  const difficulties = new Set(questions.map((q) => normalizeText(q.difficulty ?? q.level ?? q.bloomLevel)).filter(Boolean));
  if (difficulties.size >= 2) addPoints(ctx, "diagnostic", 2, "difficulty spread");
  else addIssue(ctx, "diagnostic", "diagnostic_flat_difficulty", `difficulty buckets ${difficulties.size}`, 0);

  const text = normalizeText(JSON.stringify(questions));
  if (["misconception", "mistake", "wrong", "confus", "why"].some((x) => text.includes(x))) {
    addPoints(ctx, "diagnostic", 2, "misconception/gap probes");
  } else {
    addIssue(ctx, "diagnostic", "diagnostic_missing_misconception_probe", "no clear misconception/gap probe language", 0);
  }
}

function scoreKorteksFromDiagnostic(ctx) {
  const status = normalizeText(ctx.korteks?.synthesisStatus);
  if (ctx.korteks?.sourceCount > 0) addPoints(ctx, "coherence", 1, `sourceCount ${ctx.korteks.sourceCount}`);
  if (status && !["failed", "fallback", "not_available"].includes(status)) {
    addPoints(ctx, "coherence", 1, `korteks ${ctx.korteks.synthesisStatus}`);
  } else {
    addIssue(ctx, "coherence", "korteks_degraded_or_unproven", `status=${ctx.korteks?.synthesisStatus ?? "unknown"}, sources=${ctx.korteks?.sourceCount ?? 0}`, 0);
  }
}

async function submitDiagnostic(ctx) {
  if (!ctx.planRequestId || !ctx.quizRunId || ctx.questions.length === 0) return;

  let answered = 0;
  let skipped = 0;
  let lastImpact = null;
  for (const [index, q] of ctx.questions.entries()) {
    const shouldSkip = ctx.persona.behavior === "skip_all" ||
      (ctx.persona.behavior === "mixed_blank" && index >= Math.ceil(ctx.questions.length / 2)) ||
      (ctx.persona.behavior === "mixed" && index % 3 === 2);
    const options = questionOptions(q);
    const selectedOption = shouldSkip ? null : optionId(options[index % Math.max(options.length, 1)] ?? options[0]);
    const payload = {
      quizRunId: ctx.quizRunId,
      topicId: ctx.topicId,
      questionId: questionId(q) ?? `audit-question-${index + 1}`,
      assessmentItemId: q.assessmentItemId ?? questionId(q) ?? `audit-assessment-${index + 1}`,
      conceptTag: conceptTag(q),
      selectedOptionId: selectedOption,
      wasSkipped: shouldSkip,
      responseTimeMs: shouldSkip ? 1200 : 2400,
    };
    const res = await authed(ctx, "POST", `/api/quiz/plan-diagnostic/${ctx.planRequestId}/attempt`, payload, 60000);
    recordEvidence(ctx, `diagnostic.attempt.${index + 1}`, res, false);
    if (res.ok) {
      answered++;
      if (shouldSkip) skipped++;
      lastImpact = res.data?.learningImpact ?? lastImpact;
    } else {
      addIssue(ctx, "diagnostic", "diagnostic_attempt_failed", `question ${index + 1} status ${res.status}`, 0);
    }
  }

  ctx.learningImpact = lastImpact;
  if (answered === ctx.questions.length) addPoints(ctx, "diagnostic", 2, "attempt recording");
  if (ctx.persona.blankLearner) {
    const impactText = normalizeText(JSON.stringify(lastImpact ?? {}));
    if (skipped > 0 && !/(mastered|advanced|high_confidence_mastery)/i.test(impactText)) {
      addPoints(ctx, "diagnostic", 2, "blank learner not fake mastery");
    } else {
      addIssue(ctx, "diagnostic", "blank_marked_mastery", "blank/skipped learner appears mastered or untracked", 0, true);
    }
  }
}

async function finalizePlan(ctx) {
  if (!ctx.planRequestId) return;
  const res = await authed(ctx, "POST", "/api/quiz/plan-diagnostic/finalize", {
    planRequestId: ctx.planRequestId,
  }, 240000);
  recordEvidence(ctx, "diagnostic.finalize", res);
  if (!res.ok || !res.data?.planGenerated) {
    addIssue(ctx, "plan", "plan_finalize_failed", `status ${res.status}, generated=${res.data?.planGenerated}`, 0, true);
    return;
  }
  ctx.finalize = res.data;
  addPoints(ctx, "plan", 3, "plan generated");
  if (res.data.planQuality) {
    ctx.planQuality = res.data.planQuality;
  }
}

async function inspectPlan(ctx) {
  if (!ctx.topicId) return;
  const curriculum = await authed(ctx, "GET", `/api/topics/${ctx.topicId}/curriculum`, null, 90000);
  const latest = await authed(ctx, "GET", `/api/plan-quality/topic/${ctx.topicId}/latest`, null, 90000);
  const readiness = await authed(ctx, "GET", `/api/plan-quality/topic/${ctx.topicId}/readiness`, null, 90000);
  recordEvidence(ctx, "plan.curriculum", curriculum);
  recordEvidence(ctx, "plan.quality.latest", latest);
  recordEvidence(ctx, "plan.readiness", readiness);

  ctx.curriculum = curriculum.data;
  ctx.planQuality = latest.ok ? latest.data : ctx.planQuality;
  ctx.planReadiness = readiness.data;

  const chapters = Array.isArray(curriculum.data?.chapters) ? curriculum.data.chapters : [];
  const lessons = chapters.flatMap((chapter) => Array.isArray(chapter.lessons) ? chapter.lessons : []);
  if (curriculum.ok && chapters.length >= 6 && lessons.length >= 24 && chapters.every((c) => Array.isArray(c.lessons) && c.lessons.length > 0)) {
    addPoints(ctx, "plan", 4, `${chapters.length} chapters/${lessons.length} lessons`);
  } else {
    addIssue(ctx, "plan", "plan_hierarchy_missing", `${chapters.length} chapters/${lessons.length} lessons`, 0, true);
  }

  const planText = normalizeText(JSON.stringify({ chapters }));
  const hitCount = ctx.persona.requiredTerms.filter((term) => planText.includes(normalizeText(term))).length;
  if (hitCount >= Math.min(2, ctx.persona.requiredTerms.length)) addPoints(ctx, "plan", 3, "topic-specific lesson titles");
  else addIssue(ctx, "plan", "generic_plan_ready", "curriculum does not contain enough persona topic terms", 0, true);

  if (!hasGenericPlanSmell(planText)) addPoints(ctx, "plan", 2, "generic scaffold filtered");
  else addIssue(ctx, "plan", "generic_plan_smell", "generic scaffold phrases found", 0);

  if (looksSequenced(chapters, lessons)) addPoints(ctx, "plan", 2, "sequence has fundamentals before advanced material");
  else addIssue(ctx, "plan", "plan_prerequisite_order_unproven", "could not prove prerequisite ordering", 0);

  const weakHitCount = ctx.persona.weakTerms.filter((term) => planText.includes(normalizeText(term))).length;
  if (weakHitCount >= 1) addPoints(ctx, "plan", 2, "weak concepts appear in plan");
  else addIssue(ctx, "plan", "weak_concepts_missing_from_plan", "weak target terms absent from curriculum", 0);

  if (!hasPlanBlockingIssue(ctx.planQuality, ctx.planReadiness)) addPoints(ctx, "plan", 1, "no blocking plan-quality issue exposed");
  else addIssue(ctx, "plan", "plan_quality_blocking_issue", "quality/readiness exposed blocking marker", 0);
}

async function inspectTutor(ctx) {
  if (!ctx.topicId) return;
  const tutor = await authed(ctx, "POST", "/api/chat/message", {
    content: ctx.persona.tutorPrompt,
    topicId: ctx.topicId,
    isPlanMode: false,
  }, 180000);
  recordEvidence(ctx, "tutor.chat", tutor);
  if (!tutor.ok || !tutor.data) {
    addIssue(ctx, "tutor", "tutor_chat_failed", `status ${tutor.status}`, 0, true);
    return;
  }

  const content = String(tutor.data.content ?? tutor.data.message ?? "");
  ctx.tutorContent = content;
  ctx.tutorTraceId =
    tutor.data.metadata?.tutorActionTraceId ||
    tutor.data.Metadata?.TutorActionTraceId ||
    tutor.data.tutorActionTraceId;

  if (content.length >= 120) addPoints(ctx, "tutor", 2, "non-trivial tutor answer");
  else addIssue(ctx, "tutor", "tutor_answer_too_short", `length ${content.length}`, 0);

  if (OVERCLAIM_PATTERNS.some((pattern) => pattern.test(content))) {
    addIssue(ctx, "tutor", "tutor_mastery_guarantee", "mastery/success guarantee detected", 0, true);
  } else {
    addPoints(ctx, "tutor", 3, "no mastery guarantee");
  }

  const answerText = normalizeText(content);
  ctx.tutorAnswerTopicAware = ctx.persona.requiredTerms.some((term) => answerText.includes(normalizeText(term)));

  if (/(simple|basit|step|ad[ıi]m|first|önce|micro|check|soru|example|örnek)/i.test(content)) {
    addPoints(ctx, "tutor", 3, "adaptation markers");
  } else {
    addIssue(ctx, "tutor", "tutor_adaptation_unproven", "no simple/exemplar/checkpoint markers", 0);
  }

  const trace = ctx.tutorTraceId ? await authed(ctx, "GET", `/api/tutor/trace/${ctx.tutorTraceId}`, null, 90000) : null;
  if (trace) {
    recordEvidence(ctx, "tutor.trace", trace);
  }
  const policy = await authed(ctx, "GET", `/api/tutor/policy/topic/${ctx.topicId}`, null, 90000);
  const runtime = await authed(ctx, "GET", `/api/tools/runtime/traces?topicId=${encodeURIComponent(ctx.topicId)}&take=20`, null, 90000);
  const pedagogy = await authed(ctx, "POST", `/api/tutor/pedagogy/evaluate/recent?topicId=${encodeURIComponent(ctx.topicId)}`, null, 120000);
  recordEvidence(ctx, "tutor.policy", policy);
  recordEvidence(ctx, "tutor.runtime", runtime);
  recordEvidence(ctx, "tutor.pedagogy", pedagogy);

  const structuredTutorContext = normalizeText(JSON.stringify({
    metadata: tutor.data?.metadata ?? tutor.data?.Metadata,
    trace: trace?.data,
    policy: policy.data,
  }));
  const structuredTopicAware =
    structuredTutorContext.includes(normalizeText(ctx.topicId)) ||
    ctx.persona.requiredTerms.some((term) => structuredTutorContext.includes(normalizeText(term)));
  if (ctx.tutorAnswerTopicAware || structuredTopicAware) addPoints(ctx, "tutor", 3, "topic-aware answer or trace");
  else addIssue(ctx, "tutor", "tutor_topic_drift", "answer/trace does not reference expected topic terms", 0);

  if (trace?.ok || policy.ok) addPoints(ctx, "tutor", 3, "trace/policy available");
  else addIssue(ctx, "tutor", "tutor_trace_policy_missing", "trace and policy unavailable", 0);

  const runtimeText = JSON.stringify(runtime.data ?? {});
  const traces = Array.isArray(runtime.data?.traces) ? runtime.data.traces : [];
  const governed = traces.find((t) => ["tutor", "tutor_gemini_advisory"].includes(String(t.caller)) || /wiki|source|research/i.test(String(t.toolId)));
  if (runtime.ok && !hasLeak(runtimeText) && governed) {
    addPoints(ctx, "tutor", 4, `governed tool telemetry ${governed.toolId ?? "tool"}`);
  } else if (runtime.ok && hasLeak(runtimeText)) {
    addIssue(ctx, "tutor", "raw_internal_payload_leak", "runtime trace leaked internal marker", 0, true);
  } else {
    addIssue(ctx, "tutor", "tool_governance_unproven", `trace count ${traces.length}`, 0);
  }

  const pedagogyText = normalizeText(JSON.stringify(pedagogy.data ?? trace?.data ?? policy.data ?? {}));
  if (!/(criticalviolationcount[^0-9]*[1-9]|hascriticalviolation[^a-z]*true)/i.test(pedagogyText)) {
    addPoints(ctx, "tutor", 2, "no critical pedagogy violation");
  } else {
    addIssue(ctx, "tutor", "tutor_pedagogy_critical_violation", "pedagogy endpoint exposed critical violation", 0, true);
  }
}

async function inspectRemediation(ctx) {
  if (!ctx.topicId) return;
  const mission = await authed(ctx, "GET", `/api/learning/mission-control?topicId=${encodeURIComponent(ctx.topicId)}`, null, 90000);
  const coach = await authed(ctx, "GET", `/api/learning/study-coach?topicId=${encodeURIComponent(ctx.topicId)}`, null, 90000);
  const state = await authed(ctx, "GET", `/api/tutor/state/topic/${ctx.topicId}`, null, 90000);
  recordEvidence(ctx, "remediation.mission", mission);
  recordEvidence(ctx, "remediation.coach", coach);
  recordEvidence(ctx, "remediation.tutorState", state);
  ctx.missionData = mission.data;
  ctx.coachData = coach.data;
  ctx.tutorStateData = state.data;

  const combined = normalizeText(JSON.stringify({
    mission: mission.data,
    coach: coach.data,
    state: state.data,
    tutor: ctx.tutorContent,
  }));
  const hasRepair = /(repair|remed|telafi|prerequisite|guided|misconception|weak|checkpoint|micro|worked|örnek|example)/.test(combined);
  const hasConcreteLoop = /(micro|checkpoint|worked|example|örnek|practice|pratik|step|adim|adım)/.test(combined);

  if (ctx.persona.expectRemediation) {
    if (hasRepair) addPoints(ctx, "remediation", 6, "repair signal present");
    else addIssue(ctx, "remediation", "remediation_expected_missing", "expected learner did not expose repair signal", 0);

    if (hasConcreteLoop) addPoints(ctx, "remediation", 5, "concrete repair loop markers");
    else addIssue(ctx, "remediation", "remediation_label_only", "repair signal lacks micro/example/checkpoint markers", 0);
  } else {
    if (!hasHeavyRepairSignal({ mission: mission.data, coach: coach.data, state: state.data })) addPoints(ctx, "remediation", 10, "no heavy repair for non-struggling persona");
    else addIssue(ctx, "remediation", "over_remediation", "non-struggling persona received heavy repair marker", 0);
    if (hasConcreteLoop || ctx.tutorContent) addPoints(ctx, "remediation", 4, "available next learning action");
  }

  if (ctx.persona.blankLearner) {
    if (!/(mastered|advanced|high mastery|strong mastery)/.test(combined)) addPoints(ctx, "remediation", 4, "blank learner not mastered");
    else addIssue(ctx, "remediation", "blank_marked_mastery", "blank learner received mastery marker", 0, true);
  } else {
    addPoints(ctx, "remediation", 1, "blank-specific critical not applicable");
  }
}

async function inspectWikiAndQuestionBank(ctx) {
  if (!ctx.topicId) return;
  const wiki = await authed(ctx, "GET", `/api/wiki/${ctx.topicId}`, null, 120000);
  recordEvidence(ctx, "wiki.pages", wiki);
  const pages = Array.isArray(wiki.data) ? wiki.data : [];
  const readyPages = pages.filter((p) => p.contentReadiness === "ready" && p.hasLearningContent && Number(p.visibleBlockCount ?? 0) > 0);
  if (wiki.ok && pages.length > 0) addPoints(ctx, "wiki", 2, `${pages.length} wiki pages`);
  else addIssue(ctx, "wiki", "wiki_pages_missing", `status ${wiki.status}, pages ${pages.length}`, 0);

  if (readyPages.length > 0) addPoints(ctx, "wiki", 3, `${readyPages.length} ready pages`);
  else addIssue(ctx, "wiki", "wiki_no_ready_content", "no page with ready learning content", 0);

  const page = readyPages[0] ?? pages[0];
  let detail = null;
  if (page?.id) {
    detail = await authed(ctx, "GET", `/api/wiki/page/${page.id}`, null, 90000);
    recordEvidence(ctx, "wiki.pageDetail", detail);
  }
  const blocks = Array.isArray(detail?.data?.blocks) ? detail.data.blocks : [];
  const blockTypes = blocks.map((b) => normalizeText(b.blockType ?? b.type ?? b.kind)).filter(Boolean);
  const safeSummary = page?.safeSummary ?? detail?.data?.page?.safeSummary;
  if (safeSummary && String(safeSummary).length >= 20) addPoints(ctx, "wiki", 2, "safe summary present");
  else addIssue(ctx, "wiki", "wiki_safe_summary_missing", "safeSummary empty or too short", 0);
  if (blocks.length > 0) addPoints(ctx, "wiki", 2, `${blocks.length} visible blocks`);
  else addIssue(ctx, "wiki", "wiki_blocks_missing", "page detail has no blocks", 0);
  if (blockTypes.some((t) => /summary|concept|checkpoint|practice|question|repair|misconception/.test(t)) || /summary|checkpoint|question|practice|repair|misconception/i.test(JSON.stringify(blocks))) {
    addPoints(ctx, "wiki", 2, "summary/reinforcement block present");
  } else {
    addIssue(ctx, "wiki", "wiki_reinforcement_unproven", "no summary/concept/checkpoint/question block evidence", 0);
  }

  const cards = await authed(ctx, "GET", `/api/wiki/${ctx.topicId}/study-cards`, null, 90000);
  const recs = await authed(ctx, "GET", `/api/wiki/${ctx.topicId}/recommendations`, null, 90000);
  recordEvidence(ctx, "wiki.studyCards", cards);
  recordEvidence(ctx, "wiki.recommendations", recs);
  if (cards.ok || recs.ok) addPoints(ctx, "wiki", 1, "study cards/recommendations endpoint available");

  const bank = await authed(ctx, "GET", "/api/questions?take=5&difficulty=Medium&questionType=MultipleChoice", null, 90000);
  recordEvidence(ctx, "questionBank.filter", bank);
  const questions = Array.isArray(bank.data) ? bank.data : [];
  if (bank.ok) {
    const allRespectFilter = questions.length === 0 || questions.every((q) =>
      normalizeText(q.difficulty) === "medium" && /multiple/.test(normalizeText(q.questionType ?? q.type)));
    if (allRespectFilter) addPoints(ctx, "wiki", 3, `question bank filter respected (${questions.length} rows)`);
    else addIssue(ctx, "wiki", "question_bank_filter_mismatch", "returned rows violate difficulty/questionType", 0);
  } else {
    addIssue(ctx, "wiki", "question_bank_filter_failed", `status ${bank.status}`, 0);
  }

  ctx.wikiPages = pages;
}

function inspectCoherence(ctx) {
  const planText = normalizeText(JSON.stringify(ctx.curriculum ?? {}));
  const tutorText = normalizeText(ctx.tutorContent ?? "");
  const wikiText = normalizeText(JSON.stringify(ctx.wikiPages ?? {}));
  const missionText = normalizeText(JSON.stringify({
    mission: ctx.missionData,
    coach: ctx.coachData,
    tutorState: ctx.tutorStateData,
  }));
  const required = ctx.persona.requiredTerms.map(normalizeText);
  const sharedTerms = required.filter((term) =>
    planText.includes(term) &&
    (tutorText.includes(term) || missionText.includes(term)) &&
    wikiText.includes(term));

  if (sharedTerms.length >= 1) addPoints(ctx, "coherence", 3, `shared term ${sharedTerms[0]}`);
  else addIssue(ctx, "coherence", "cross_surface_drift", "plan/tutor/wiki/mission did not share expected topic terms", 0);

  const combined = JSON.stringify({
    evidence: ctx.evidence,
    plan: ctx.curriculum,
    quality: ctx.planQuality,
    readiness: ctx.planReadiness,
  });
  if (hasLeak(combined)) {
    addIssue(ctx, "coherence", "raw_internal_payload_leak", "public evidence contained blocked marker", 0, true);
  } else {
    addPoints(ctx, "coherence", 2, "public DTO leak sweep clean");
  }

  if (ctx.persona.sourceSensitive) {
    const text = normalizeText(`${ctx.tutorContent ?? ""} ${JSON.stringify(ctx.wikiPages ?? {})}`);
    const inventsCitation = /\[[0-9]+\]|doi:|arxiv|http/.test(text) && !/(evidence|source|citation|ground|insufficient|available|readiness)/.test(text);
    if (!inventsCitation) addPoints(ctx, "coherence", 2, "source-sensitive overclaim clean");
    else addIssue(ctx, "coherence", "hallucinated_source_citation", "citation marker without evidence caveat", 0, true);
  } else {
    addPoints(ctx, "coherence", 1, "source-sensitive critical not applicable");
  }
}

function hasPlanBlockingIssue(...payloads) {
  const blockingStatuses = new Set(["failed", "not_ready", "missing_hierarchy", "generic_plan_ready", "degraded"]);
  const stack = payloads.filter(Boolean);
  while (stack.length) {
    const value = stack.pop();
    if (!value || typeof value !== "object") continue;
    if (Array.isArray(value)) {
      stack.push(...value);
      continue;
    }
    for (const [key, child] of Object.entries(value)) {
      const normalizedKey = normalizeText(key);
      if (normalizedKey === "blockingissues" && Array.isArray(child) && child.length > 0) return true;
      if (normalizedKey === "hascriticalviolation" && child === true) return true;
      if (/(status|readiness|quality)/i.test(key) && typeof child === "string" && blockingStatuses.has(normalizeText(child))) return true;
      if (child && typeof child === "object") stack.push(child);
    }
  }
  return false;
}

function hasHeavyRepairSignal(payload) {
  const stack = [payload].filter(Boolean);
  while (stack.length) {
    const value = stack.pop();
    if (!value) continue;
    if (typeof value === "string") {
      const normalized = normalizeText(value);
      if (/(urgent_remediation|heavy_repair|major_repair|critical_repair)/.test(normalized)) return true;
      continue;
    }
    if (Array.isArray(value)) {
      stack.push(...value);
      continue;
    }
    if (typeof value === "object") {
      for (const [key, child] of Object.entries(value)) {
        const normalizedKey = normalizeText(key);
        if (/(urgentremediation|heavyrepair|majorrepair|criticalrepair)/.test(normalizedKey)) {
          if (child === true) return true;
          if (typeof child === "string" && /(true|urgent|heavy|major|critical|high)/.test(normalizeText(child))) return true;
        }
        stack.push(child);
      }
    }
  }
  return false;
}

async function authed(ctx, method, url, body = null, timeoutMs = 30000) {
  if (!ctx.token) {
    return { ok: false, status: 0, data: null, text: "", error: "missing token", method, url, durationMs: 0 };
  }
  return request(method, url, {
    token: ctx.token,
    body,
    timeoutMs,
    safety: true,
  });
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function request(method, url, options = {}) {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), options.timeoutMs ?? 30000);
  const headers = { Accept: "application/json" };
  if (options.token) headers.Authorization = `Bearer ${options.token}`;
  if (options.body !== null && options.body !== undefined) headers["Content-Type"] = "application/json";
  if (currentClientIp) headers["X-Forwarded-For"] = currentClientIp;
  const started = performance.now();

  try {
    const response = await fetch(`${BASE_URL}${url}`, {
      method,
      headers,
      body: options.body !== null && options.body !== undefined ? JSON.stringify(options.body) : undefined,
      signal: controller.signal,
    });
    const text = await response.text();
    const data = parseJson(text);
    const result = { ok: response.ok, status: response.status, data, text, method, url, durationMs: Math.round(performance.now() - started) };
  if (options.safety && hasLeak(JSON.stringify(data ?? text ?? ""))) {
    result.safetyLeak = true;
    result.safetyLeakMarkers = leakMarkers(JSON.stringify(data ?? text ?? ""));
  }
    return result;
  } catch (error) {
    return {
      ok: false,
      status: 0,
      data: null,
      text: "",
      method,
      url,
      durationMs: Math.round(performance.now() - started),
      error: error instanceof Error ? error.message : String(error),
    };
  } finally {
    clearTimeout(timeout);
  }
}

function parseArgs(argv) {
  const parsed = {};
  for (let i = 0; i < argv.length; i += 1) {
    const raw = argv[i];
    const match = raw.match(/^--([^=]+)(?:=(.*))?$/);
    if (!match) continue;
    const next = argv[i + 1];
    if (match[2] !== undefined) {
      parsed[match[1]] = match[2];
    } else if (next && !next.startsWith("--")) {
      parsed[match[1]] = next;
      i += 1;
    } else {
      parsed[match[1]] = "true";
    }
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

function parseJson(text) {
  if (!text) return null;
  try { return JSON.parse(text); } catch { return null; }
}

function recordEvidence(ctx, key, response, publicPayload = true) {
  const safe = {
    status: response?.status ?? 0,
    ok: Boolean(response?.ok),
    ms: response?.durationMs ?? 0,
    ref: hashShort(redact(JSON.stringify(response?.data ?? response?.text ?? response?.error ?? ""))),
  };
  ctx.evidence[key] = safe;
  if (publicPayload && response?.safetyLeak) {
    addIssue(ctx, "coherence", "raw_internal_payload_leak", `${key} leaked blocked public marker: ${(response.safetyLeakMarkers ?? []).join(", ")}`, 0, true);
  }
}

function addPoints(ctx, area, points, reason) {
  ctx.scores[area] = Math.min(AREA_MAX[area], (ctx.scores[area] ?? 0) + points);
  console.log(`  +${points} ${area}: ${reason}`);
}

function addIssue(ctx, area, code, message, points = 0, critical = false) {
  const issue = {
    area,
    code,
    message,
    critical: critical || CRITICAL_CODES.has(code),
  };
  ctx.issues.push(issue);
  if (issue.critical) audit.criticalFailures.push({ persona: ctx.persona.slug, ...issue });
  if (points > 0) addPoints(ctx, area, points, message);
  console.log(`  ! ${area}/${code}: ${message}${issue.critical ? " [critical]" : ""}`);
}

function questionId(q) {
  return q?.id ?? q?.questionId ?? q?.assessmentItemId ?? null;
}

function questionStem(q) {
  return String(q?.stem ?? q?.question ?? q?.prompt ?? q?.text ?? "");
}

function questionOptions(q) {
  return Array.isArray(q?.options) ? q.options : [];
}

function optionId(option) {
  if (!option) return null;
  return option.id ?? option.optionId ?? option.value ?? option.text ?? option.label ?? null;
}

function conceptTag(q) {
  return q?.conceptTag ?? q?.conceptKey ?? q?.skillTag ?? q?.learningObjective ?? "";
}

function normalizeText(value) {
  return String(value ?? "")
    .toLowerCase()
    .normalize("NFKD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/ı/g, "i")
    .replace(/ş/g, "s")
    .replace(/ğ/g, "g")
    .replace(/ü/g, "u")
    .replace(/ö/g, "o")
    .replace(/ç/g, "c");
}

function hasGenericPlanSmell(text) {
  return /(let'?s learn|learning path|start with basics|module 1|topic overview)/i.test(text) &&
    !/(sql|async|industrial|index|cardinality|revolution)/i.test(text);
}

function looksSequenced(chapters, lessons) {
  const firstThird = normalizeText(JSON.stringify(chapters.slice(0, Math.max(1, Math.ceil(chapters.length / 3)))));
  const lastThird = normalizeText(JSON.stringify(chapters.slice(Math.floor(chapters.length * 2 / 3))));
  if (/(foundation|intro|basic|prerequisite|fundamental|temel)/.test(firstThird)) return true;
  if (/(advanced|optimization|performance|synthesis|capstone|professional)/.test(lastThird)) return true;
  return lessons.length >= 24 && chapters.every((c, index) => Number(c.order ?? index) >= 0);
}

function hasLeak(text) {
  return LEAK_PATTERNS.some((pattern) => pattern.test(String(text ?? "")));
}

function leakMarkers(text) {
  const value = String(text ?? "");
  return LEAK_PATTERNS
    .filter((pattern) => pattern.test(value))
    .map((pattern) => pattern.source)
    .slice(0, 6);
}

function hashShort(text) {
  return crypto.createHash("sha256").update(String(text ?? "")).digest("hex").slice(0, 12);
}

function redact(text) {
  return String(text ?? "")
    .replace(/Bearer\s+[A-Za-z0-9._-]+/g, "Bearer [redacted]")
    .replace(/"token"\s*:\s*"[^"]+"/gi, "\"token\":\"[redacted]\"")
    .replace(/"accessToken"\s*:\s*"[^"]+"/gi, "\"accessToken\":\"[redacted]\"");
}

function slugify(value) {
  return normalizeText(value).replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
}

function sum(values) {
  return values.reduce((total, value) => total + Number(value ?? 0), 0);
}

function computeReleasePass() {
  if (audit.personas.length === 0) return false;
  return audit.personas.every((p) => p.releasePass) && audit.criticalFailures.length === 0;
}

async function writeReport(releasePass) {
  audit.finishedAt = new Date().toISOString();
  audit.releasePass = releasePass;
  const lines = [];
  lines.push(`# Orka Live Pedagogical Quality Audit`);
  lines.push("");
  lines.push(`**Run ID:** \`${RUN_ID}\``);
  lines.push(`**Target API:** \`${BASE_URL}\``);
  lines.push(`**Include AI Provider Flag:** \`${INCLUDE_AI_PROVIDER}\``);
  lines.push(`**Verdict:** ${releasePass ? "RELEASE PASS" : "RELEASE FAIL"}`);
  lines.push("");
  lines.push(`This report is evidence-based. It does not include tokens, raw provider payloads, stack traces, local paths, thought signatures, or answer keys.`);
  lines.push("");
  lines.push(`## Scorecard`);
  lines.push("");
  lines.push(`| Persona | Plan | Diagnostic | Tutor | Remediation | Wiki/QBank | Coherence | Total | Verdict |`);
  lines.push(`|---|---:|---:|---:|---:|---:|---:|---:|---|`);
  for (const p of audit.personas) {
    lines.push(`| \`${p.slug}\` | ${p.scores.plan}/20 | ${p.scores.diagnostic}/20 | ${p.scores.tutor}/20 | ${p.scores.remediation}/15 | ${p.scores.wiki}/15 | ${p.scores.coherence}/10 | ${p.totalScore}/100 | ${p.releasePass ? "PASS" : "FAIL"} |`);
  }
  lines.push("");
  lines.push(`## Issues`);
  lines.push("");
  const allIssues = audit.personas.flatMap((p) => p.issues.map((issue) => ({ persona: p.slug, ...issue })));
  if (allIssues.length === 0) {
    lines.push(`No issues found.`);
  } else {
    lines.push(`| Persona | Area | Code | Critical | Message |`);
    lines.push(`|---|---|---|---|---|`);
    for (const issue of allIssues) {
      lines.push(`| \`${issue.persona}\` | ${issue.area} | \`${issue.code}\` | ${issue.critical ? "yes" : "no"} | ${escapeMd(issue.message)} |`);
    }
  }
  lines.push("");
  lines.push(`## Evidence Refs`);
  lines.push("");
  for (const p of audit.personas) {
    lines.push(`### ${p.slug}`);
    lines.push("");
    lines.push(`| Endpoint Step | OK | Status | Duration | Payload Ref |`);
    lines.push(`|---|---:|---:|---:|---|`);
    for (const [key, value] of Object.entries(p.evidence)) {
      lines.push(`| \`${key}\` | ${value.ok ? "yes" : "no"} | ${value.status} | ${value.ms}ms | \`${value.ref}\` |`);
    }
    lines.push("");
  }
  lines.push(`## Pass Rules`);
  lines.push("");
  lines.push(`Release pass requires score >= 85/100 per persona, no critical failures, and every major area at least 75% of its maximum.`);
  lines.push(`Professional pass target remains >= 90/100 with Plan/Diagnostic/Tutor each near professional quality.`);
  lines.push("");
  lines.push(releasePass
    ? `Final result: release quality gate passed.`
    : `Final result: release quality gate failed. Inspect the issues above before treating the system as pedagogically ready.`);
  lines.push("");

  await fs.mkdir(path.dirname(REPORT_PATH), { recursive: true });
  await fs.writeFile(REPORT_PATH, lines.join("\n"), "utf8");
}

function escapeMd(value) {
  return String(value ?? "").replace(/\|/g, "\\|").replace(/\n/g, " ");
}

function printSummary(releasePass) {
  console.log(`\nReport: ${REPORT_PATH}`);
  console.log(`Verdict: ${releasePass ? "RELEASE PASS" : "RELEASE FAIL"}`);
  for (const p of audit.personas) {
    console.log(`- ${p.slug}: ${p.totalScore}/100 (${p.releasePass ? "PASS" : "FAIL"}), issues=${p.issues.length}`);
  }
}
