#!/usr/bin/env node

import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { ApiClient, boolArg, parseArgs, trimSlash } from "./api-client.mjs";
import { extractFacts } from "./extractors/facts.mjs";
import { evaluateBundle } from "./invariants/evaluate.mjs";
import { runOptionalJudge } from "./judge.mjs";
import { createPrivacy } from "./privacy.mjs";
import { SCORE_MAX, REQUIRED_ENDPOINTS, selectScenarios } from "./scenarios.mjs";
import { writeJsonReport } from "./reporters/json.mjs";
import { writeJsonlReport } from "./reporters/jsonl.mjs";
import { writeMarkdownReport } from "./reporters/markdown.mjs";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(__dirname, "../..");
const args = parseArgs(process.argv.slice(2));
const runId = String(args["run-id"] ?? new Date().toISOString().replace(/[-:.TZ]/g, "").slice(0, 14));
const baseUrl = trimSlash(args["base-url"] ?? args["api-url"] ?? process.env.ORKA_API_URL ?? "http://localhost:5065");
const includeAiProvider = boolArg(args, "include-ai-provider");
const reportDir = path.resolve(ROOT, args["report-dir"] ?? "life_tests/reports");
const selectedScenarios = selectScenarios(args.personas);
const password = `OrkaPedAudit${runId}!`;
const privacy = createPrivacy(runId);
const api = new ApiClient({ baseUrl, privacy, timeoutMs: 30000 });
let checkpointScenario = null;

main().catch(async (error) => {
  console.error(`FATAL pedagogy-audit error: ${error?.stack ?? error}`);
  const fatalReport = baseReport();
  fatalReport.fatalError = String(error?.message ?? error);
  fatalReport.releasePass = false;
  await writeAllReports(fatalReport);
  process.exit(2);
});

async function main() {
  console.log("Orka Pedagogical Quality Audit V2");
  console.log(`Run: ${runId}`);
  console.log(`Target: ${baseUrl}`);
  console.log(`Scenarios: ${selectedScenarios.map((s) => s.id).join(", ")}`);

  if (selectedScenarios.length === 0) {
    throw new Error("No scenarios selected.");
  }

  await preflight();

  const report = baseReport();
  for (const [index, scenario] of selectedScenarios.entries()) {
    api.setClientIp(`127.40.${Number(runId.slice(-4, -2) || 0) % 240}.${index + 30}`);
    checkpointScenario = scenario;
    console.log(`\n== ${scenario.id} ==`);
    const evidence = await collectEvidence(scenario, index);
    const bundle = extractFacts({ scenario, steps: evidence.steps, privacy });
    const judge = await runOptionalJudge({ includeAiProvider, bundle });
    const evaluation = evaluateBundle(bundle, { includeJudge: includeAiProvider, judgeResult: judge });
    if (evidence.collectionError) {
      evaluation.issues.push({
        area: "contract",
        code: "scenario_collection_error",
        severity: "critical",
        message: evidence.collectionError,
        evidenceRefs: [],
        deterministic: true,
      });
      evaluation.invariants.push({
        area: "contract",
        code: "scenario_collection_error",
        status: "fail",
        severity: "critical",
        message: evidence.collectionError,
        evidenceRefs: [],
      });
      evaluation.criticalFailureCount = evaluation.issues.filter((issue) => issue.severity === "critical").length;
      evaluation.releasePass = false;
    }
    report.personas.push({
      personaId: scenario.id,
      bundle,
      evaluation,
    });
    console.log(`${scenario.id}: ${evaluation.totalScore}/100 ${evaluation.releasePass ? "PASS" : "FAIL"}`);
    report.finishedAt = new Date().toISOString();
    report.releasePass = false;
    await writeAllReports(report);
  }

  report.finishedAt = new Date().toISOString();
  report.releasePass = report.personas.length > 0 && report.personas.every((persona) => persona.evaluation.releasePass);
  checkpointScenario = null;
  await writeAllReports(report);
  console.log(`\nReports:`);
  console.log(`- ${path.join(reportDir, "pedagogical-quality-audit.md")}`);
  console.log(`- ${path.join(reportDir, "pedagogical-quality-audit.json")}`);
  console.log(`- ${path.join(reportDir, "pedagogical-quality-audit.jsonl")}`);
  console.log(`Verdict: ${report.releasePass ? "RELEASE PASS" : "RELEASE FAIL"}`);
  process.exit(report.releasePass ? 0 : 1);
}

function baseReport() {
  return {
    runId,
    baseUrl,
    includeAiProvider,
    startedAt: new Date().toISOString(),
    maxScores: SCORE_MAX,
    requiredEndpoints: REQUIRED_ENDPOINTS,
    personas: [],
  };
}

async function writeAllReports(report) {
  await writeMarkdownReport(path.join(reportDir, "pedagogical-quality-audit.md"), report);
  await writeJsonReport(path.join(reportDir, "pedagogical-quality-audit.json"), report);
  await writeJsonlReport(path.join(reportDir, "pedagogical-quality-audit.jsonl"), report);
}

async function preflight() {
  const health = await api.request("GET", "/health", { evidenceKey: "preflight.health", timeoutMs: 10000 });
  if (health.ok) return;
  const apiHealth = await api.request("GET", "/api/health", { evidenceKey: "preflight.apiHealth", timeoutMs: 10000 });
  if (!apiHealth.ok) {
    throw new Error(`API health failed: /health=${health.status}, /api/health=${apiHealth.status}`);
  }
}

async function collectEvidence(scenario, index) {
  const steps = [];
  const email = `orka-ped-audit-${slugify(scenario.id)}-${runId}@orka.local`;
  const firstName = scenario.id.slice(0, 32);

  try {
  const register = await step(steps, "auth.register", "POST", "/api/auth/register", {
    body: { firstName, lastName: "PedAudit", name: `${firstName} PedAudit`, email, password },
    required: true,
  });
  const login = await step(steps, "auth.login", "POST", "/api/auth/login", {
    body: { email, password },
    required: true,
  });
  const token = login.data?.token ?? register.data?.token;
  if (!token) return { steps };

  const topic = await step(steps, "topic.create", "POST", "/api/topics", {
    token,
    body: { title: `${scenario.title} ${runId}`, emoji: "O", category: scenario.category },
    required: true,
  });
  const topicId = topic.data?.id;
  if (!topicId) return { steps };

  const intent = await step(steps, "intent.analysis", "POST", "/api/quiz/plan-diagnostic/intent", {
    token,
    body: { rawRequest: scenario.prompt, topicId, existingTopicTitle: scenario.title },
    required: true,
    timeoutMs: 90000,
  });

  const diagnosticAccepted = await step(steps, "diagnostic.start.accepted", "POST", "/api/quiz/plan-diagnostic/start-async", {
    token,
    body: {
      topicId,
      topicTitle: scenario.title,
      intentRequestId: intent.data?.intentRequestId,
      approvedMainTopic: intent.data?.mainTopic ?? scenario.title,
      approvedFocusArea: intent.data?.focusArea ?? scenario.title,
      approvedStudyGoal: intent.data?.studyGoal ?? "professional learning",
      approvedResearchIntent: intent.data?.researchIntent ?? scenario.prompt,
      rawStudyRequest: intent.data?.rawRequest ?? scenario.prompt,
    },
    required: false,
    timeoutMs: 60000,
  });

  const diagnosticStart = await pollDiagnosticStartStatus(steps, {
    token,
    planRequestId: diagnosticAccepted.data?.planRequestId,
    timeoutMs: 12 * 60 * 1000,
    intervalMs: 5000,
  });

  const questions = parseQuestions(diagnosticStart.data?.questionsJson);
  const planRequestId = diagnosticStart.data?.planRequestId;
  const quizRunId = diagnosticStart.data?.quizRunId;
  const diagnosticTerminalStatus = normalizePlanDiagnosticStatus(diagnosticStart.data?.status);
  const diagnosticFailed = diagnosticTerminalStatus === "Failed" || questions.length === 0;
  if (diagnosticFailed) {
    const rootMessage = diagnosticStart.redacted?.error ??
      diagnosticStart.data?.errorMessage ??
      diagnosticStart.data?.message ??
      "Diagnostic generation did not produce learner-ready questions.";
    await recordSyntheticStep(steps, "diagnostic.finalize", "POST", "/api/quiz/plan-diagnostic/finalize", {
      status: 0,
      ok: false,
      required: true,
      message: `skipped_due_to_diagnostic_failed: ${rootMessage}`,
    });
    await recordSyntheticStep(steps, "plan.curriculum", "GET", `/api/topics/${topicId}/curriculum`, {
      status: 0,
      ok: false,
      required: true,
      message: `skipped_due_to_diagnostic_failed: ${rootMessage}`,
    });
    await recordSyntheticStep(steps, "plan.readiness", "GET", `/api/plan-quality/topic/${topicId}/readiness`, {
      status: 0,
      ok: false,
      required: true,
      message: `skipped_due_to_diagnostic_failed: ${rootMessage}`,
    });
    await recordSyntheticStep(steps, "plan.latest", "GET", `/api/plan-quality/topic/${topicId}/latest`, {
      status: 0,
      ok: false,
      required: true,
      message: `skipped_due_to_diagnostic_failed: ${rootMessage}`,
    });
    return { steps, topicId, finalize: null, scenarioIndex: index };
  }

  if (planRequestId && quizRunId && questions.length > 0) {
    for (const [questionIndex, question] of questions.entries()) {
      const shouldSkip = scenario.behavior === "skip_all" ||
        (scenario.behavior === "mixed_blank" && questionIndex >= Math.ceil(questions.length / 2)) ||
        (scenario.behavior === "mixed" && questionIndex % 3 === 2);
      const options = Array.isArray(question.options) ? question.options : [];
      const selected = shouldSkip ? null : optionId(options[questionIndex % Math.max(options.length, 1)] ?? options[0]);
      await step(steps, `diagnostic.attempt.${questionIndex + 1}`, "POST", `/api/quiz/plan-diagnostic/${planRequestId}/attempt`, {
        token,
        body: {
          quizRunId,
          topicId,
          questionId: question.id ?? question.questionId ?? question.assessmentItemId ?? `audit-question-${questionIndex + 1}`,
          assessmentItemId: question.assessmentItemId ?? question.id ?? question.questionId ?? `audit-assessment-${questionIndex + 1}`,
          conceptTag: question.conceptTag ?? question.conceptKey ?? question.skillTag,
          selectedOptionId: selected,
          wasSkipped: shouldSkip,
          responseTimeMs: shouldSkip ? 1200 : 2400,
        },
        meta: { wasSkipped: shouldSkip },
        timeoutMs: 60000,
      });
    }
  }

  let finalize = { data: null };
  if (planRequestId) {
    finalize = await step(steps, "diagnostic.finalize", "POST", "/api/quiz/plan-diagnostic/finalize", {
      token,
      body: { planRequestId },
      required: true,
      timeoutMs: 240000,
    });
  } else {
    await recordSyntheticStep(steps, "diagnostic.finalize", "POST", "/api/quiz/plan-diagnostic/finalize", {
      status: 0,
      ok: false,
      required: true,
      message: "Skipped because diagnostic.start did not return a planRequestId.",
    });
  }

  const planTopicId = finalize.data?.generatedPlanRootTopicId ?? topicId;
  if (finalize.ok && finalize.data?.planGenerated === true) {
    await step(steps, "plan.curriculum", "GET", `/api/topics/${planTopicId}/curriculum`, { token, required: true, timeoutMs: 90000 });
    await step(steps, "plan.readiness", "GET", `/api/plan-quality/topic/${planTopicId}/readiness`, { token, required: true, timeoutMs: 90000 });
    await step(steps, "plan.latest", "GET", `/api/plan-quality/topic/${planTopicId}/latest`, { token, required: true, timeoutMs: 90000 });
  } else {
    const rootMessage = finalize.data?.message ?? finalize.data?.errorMessage ?? "finalize did not generate a materialized plan.";
    await recordSyntheticStep(steps, "plan.curriculum", "GET", `/api/topics/${planTopicId}/curriculum`, {
      status: 0,
      ok: false,
      required: true,
      message: `skipped_due_to_plan_not_generated: ${rootMessage}`,
    });
    await recordSyntheticStep(steps, "plan.readiness", "GET", `/api/plan-quality/topic/${planTopicId}/readiness`, {
      status: 0,
      ok: false,
      required: true,
      message: `skipped_due_to_plan_not_generated: ${rootMessage}`,
    });
    await recordSyntheticStep(steps, "plan.latest", "GET", `/api/plan-quality/topic/${planTopicId}/latest`, {
      status: 0,
      ok: false,
      required: true,
      message: `skipped_due_to_plan_not_generated: ${rootMessage}`,
    });
  }
  await step(steps, "mastery.adaptiveProfile", "GET", `/api/learning/topic/${topicId}/adaptive-profile`, { token, timeoutMs: 90000 });
  await step(steps, "snapshot.studentContext", "GET", `/api/learning-snapshots/student-context?topicId=${encodeURIComponent(topicId)}`, { token, timeoutMs: 90000 });
  await step(steps, "snapshot.activeLesson", "GET", `/api/learning-snapshots/active-lesson?topicId=${encodeURIComponent(topicId)}`, { token, timeoutMs: 90000 });

  const tutor = await step(steps, "tutor.chat", "POST", "/api/chat/message", {
    token,
    body: { content: scenario.tutorPrompt, topicId, isPlanMode: false },
    required: true,
    timeoutMs: 180000,
  });
  const traceId = tutor.data?.metadata?.tutorActionTraceId ?? tutor.data?.Metadata?.TutorActionTraceId ?? tutor.data?.tutorActionTraceId;
  if (traceId) {
    await step(steps, "tutor.trace", "GET", `/api/tutor/trace/${traceId}`, { token, timeoutMs: 90000 });
  }
  await step(steps, "tutor.policy", "GET", `/api/tutor/policy/topic/${topicId}`, { token, required: true, timeoutMs: 90000 });
  await step(steps, "tutor.pedagogy", "POST", `/api/tutor/pedagogy/evaluate/recent?topicId=${encodeURIComponent(topicId)}`, { token, required: true, timeoutMs: 120000 });
  await step(steps, "tutor.state", "GET", `/api/tutor/state/topic/${topicId}`, { token, timeoutMs: 90000 });
  await step(steps, "tooling.runtime", "GET", `/api/tools/runtime/traces?topicId=${encodeURIComponent(topicId)}&take=20`, { token, timeoutMs: 90000 });

  const wiki = await step(steps, "wiki.pages", "GET", `/api/wiki/${topicId}`, { token, required: true, timeoutMs: 120000 });
  const firstPage = Array.isArray(wiki.data)
    ? wiki.data.find((page) => page.contentReadiness === "ready" && page.conceptKey) ??
      wiki.data.find((page) => page.contentReadiness === "ready") ??
      wiki.data.find((page) => page.conceptKey) ??
      wiki.data[0]
    : null;
  if (firstPage?.id) {
    await step(steps, "wiki.page", "GET", `/api/wiki/page/${firstPage.id}`, { token, timeoutMs: 90000 });
    await step(steps, "wiki.curation", "GET", `/api/wiki/page/${firstPage.id}/curation`, { token, timeoutMs: 90000 });
    await step(steps, "wiki.copilot", "GET", `/api/wiki/page/${firstPage.id}/copilot`, { token, timeoutMs: 90000 });
    const wikiQuestions = await step(steps, "wiki.page.questions", "GET", `/api/wiki/page/${firstPage.id}/questions?count=3`, {
      token,
      required: true,
      timeoutMs: 90000,
    });
    const wikiQuestionList = Array.isArray(wikiQuestions.data?.questions) ? wikiQuestions.data.questions : [];
    const wikiPractice = await step(steps, "wiki.page.practice.start", "POST", `/api/wiki/page/${firstPage.id}/practice/start`, {
      token,
      body: { count: 3, mode: "wiki_page_audit_drill" },
      required: true,
      timeoutMs: 90000,
    });
    const wikiPracticeQuestions = Array.isArray(wikiPractice.data?.questions) ? wikiPractice.data.questions : [];
    if (wikiQuestionList.length > 0 && wikiPracticeQuestions.length > 0) {
      await step(steps, "wiki.page.practice.submit", "POST", "/api/question-practice/submit", {
        token,
        body: {
          practiceSetId: wikiPractice.data?.practiceSetId,
          topicId: wikiPractice.data?.topicId ?? firstPage.topicId ?? topicId,
          sessionId: null,
          mode: wikiPractice.data?.mode ?? "wiki_page_audit_drill",
          answers: wikiPracticeQuestions.slice(0, 3).map((question) => {
            const firstOption = Array.isArray(question.options) ? question.options[0] : null;
            return {
              questionItemId: question.questionItemId,
              selectedOptionKey: firstOption?.optionKey ?? null,
              wasSkipped: !firstOption?.optionKey,
            };
          }),
        },
        timeoutMs: 90000,
      });
    } else {
      await recordSyntheticStep(steps, "wiki.page.practice.submit", "POST", "/api/question-practice/submit", {
        status: 0,
        ok: false,
        required: false,
        message: "skipped_due_to_no_wiki_page_practice_ready_questions",
      });
    }
  } else {
    await recordSyntheticStep(steps, "wiki.page.questions", "GET", "/api/wiki/page/{pageId}/questions", {
      status: 0,
      ok: false,
      required: true,
      message: "skipped_due_to_no_wiki_page",
    });
    await recordSyntheticStep(steps, "wiki.page.practice.start", "POST", "/api/wiki/page/{pageId}/practice/start", {
      status: 0,
      ok: false,
      required: true,
      message: "skipped_due_to_no_wiki_page",
    });
    await recordSyntheticStep(steps, "wiki.page.practice.submit", "POST", "/api/question-practice/submit", {
      status: 0,
      ok: false,
      required: false,
      message: "skipped_due_to_no_wiki_page",
    });
  }
  await step(steps, "wiki.studyCards", "GET", `/api/wiki/${topicId}/study-cards`, { token, timeoutMs: 90000 });
  await step(steps, "wiki.recommendations", "GET", `/api/wiki/${topicId}/recommendations`, { token, timeoutMs: 90000 });

  await step(steps, "source.evidenceBundle", "GET", `/api/sources/topic/${topicId}/evidence-bundle`, { token, timeoutMs: 90000 });
  await step(steps, "source.lifecycle", "GET", `/api/sources/topic/${topicId}/lifecycle-summary`, { token, timeoutMs: 90000 });
  await step(steps, "learning.quality", "GET", `/api/learning-quality/topic/${topicId}`, { token, required: true, timeoutMs: 120000 });
  await step(steps, "mission.control", "GET", `/api/learning/mission-control?topicId=${encodeURIComponent(topicId)}`, { token, required: true, timeoutMs: 90000 });
  await step(steps, "study.coach", "GET", `/api/learning/study-coach?topicId=${encodeURIComponent(topicId)}`, { token, required: true, timeoutMs: 90000 });
  await step(steps, "questionBank.filter", "GET", "/api/questions?take=5&difficulty=Medium&questionType=MultipleChoice", { token, timeoutMs: 90000 });
  const systemBound = await step(steps, "questionBank.systemBound", "GET", `/api/questions?learningTopicId=${encodeURIComponent(topicId)}&take=5&qualityStatus=diagnostic_ready`, { token, timeoutMs: 90000 });
  const systemBoundQuestions = Array.isArray(systemBound.data) ? systemBound.data : [];
  const typedPracticeSeed = systemBoundQuestions.find((question) => question.assessmentItemId || question.learningConceptId) ?? {};
  const typedPracticeBody = {
    topicId,
    sessionId: null,
    mode: "weak_concept_drill",
    count: 3,
    questionBankSource: "diagnostic_assessment_item",
  };
  if (typedPracticeSeed.assessmentItemId) typedPracticeBody.assessmentItemIds = [typedPracticeSeed.assessmentItemId];
  if (typedPracticeSeed.conceptGraphSnapshotId) typedPracticeBody.conceptGraphSnapshotId = typedPracticeSeed.conceptGraphSnapshotId;
  if (typedPracticeSeed.learningConceptId) typedPracticeBody.learningConceptIds = [typedPracticeSeed.learningConceptId];
  const practiceStart = await step(steps, "questionBank.practice.start", "POST", "/api/question-practice/start", {
    token,
    body: typedPracticeBody,
    meta: {
      typedFilters: Boolean(typedPracticeBody.assessmentItemIds?.length || typedPracticeBody.learningConceptIds?.length || typedPracticeBody.conceptGraphSnapshotId),
    },
    timeoutMs: 90000,
  });
  const practiceQuestions = Array.isArray(practiceStart.data?.questions) ? practiceStart.data.questions : [];
  if (practiceQuestions.length > 0) {
    await step(steps, "questionBank.practice.submit", "POST", "/api/question-practice/submit", {
      token,
      body: {
        practiceSetId: practiceStart.data?.practiceSetId,
        topicId,
        sessionId: null,
        mode: practiceStart.data?.mode ?? "weak_concept_drill",
        answers: practiceQuestions.slice(0, 3).map((question) => {
          const firstOption = Array.isArray(question.options) ? question.options[0] : null;
          return {
            questionItemId: question.questionItemId,
            selectedOptionKey: firstOption?.optionKey ?? null,
            wasSkipped: !firstOption?.optionKey,
          };
        }),
      },
      timeoutMs: 90000,
    });
  } else {
    await recordSyntheticStep(steps, "questionBank.practice.submit", "POST", "/api/question-practice/submit", {
      status: 0,
      ok: false,
      required: false,
      message: "skipped_due_to_no_practice_ready_questions",
    });
  }

  return { steps, topicId, finalize: finalize.data, scenarioIndex: index };
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.error(`  FAIL scenario.collection error=${message}`);
    return { steps, scenarioIndex: index, collectionError: message };
  }
}

async function step(steps, key, method, url, { token, body, timeoutMs, required = false, meta } = {}) {
  await writeStepCheckpoint(steps, { key, method, url, required, meta, inFlight: true, startedAt: new Date().toISOString() });
  const response = await api.request(method, url, { token, body, timeoutMs, evidenceKey: key });
  response.key = key;
  response.required = required;
  response.meta = meta;
  steps.push(response);
  await writeStepCheckpoint(steps);
  const label = response.ok ? "OK" : required ? "FAIL" : "WARN";
  console.log(`  ${label} ${key} status=${response.status} ${response.durationMs}ms`);
  return response;
}

async function pollDiagnosticStartStatus(steps, { token, planRequestId, timeoutMs, intervalMs }) {
  if (!planRequestId) {
    await recordSyntheticStep(steps, "diagnostic.start", "GET", "/api/quiz/plan-diagnostic/{planRequestId}/status", {
      status: 0,
      ok: false,
      required: true,
      message: "Skipped because diagnostic.start did not return a planRequestId.",
    });
    return steps.find((step) => step.key === "diagnostic.start") ?? { data: null };
  }

  const deadline = Date.now() + timeoutMs;
  let pollIndex = 0;
  let latest = null;
  while (Date.now() < deadline) {
    pollIndex += 1;
    latest = await step(
      steps,
      pollIndex === 1 ? "diagnostic.status" : `diagnostic.status.${pollIndex}`,
      "GET",
      `/api/quiz/plan-diagnostic/${planRequestId}/status`,
      { token, required: pollIndex === 1, timeoutMs: 60000 },
    );

    const status = normalizePlanDiagnosticStatus(latest.data?.status);
    if (latest.ok && latest.data?.isReady === true && parseQuestions(latest.data?.questionsJson).length > 0) {
      return await recordAliasStep(steps, latest, "diagnostic.start", true);
    }

    if (status === "Failed") {
      return await recordAliasStep(steps, {
        ...latest,
        ok: false,
        redacted: {
          ...(latest.redacted ?? {}),
          error: latest.data?.errorMessage ?? latest.data?.message ?? "Diagnostic generation failed.",
        },
      }, "diagnostic.start", true);
    }

    await sleep(intervalMs);
  }

  await recordSyntheticStep(steps, "diagnostic.start", "GET", `/api/quiz/plan-diagnostic/${planRequestId}/status`, {
    status: 0,
    ok: false,
    required: true,
    message: `Timed out waiting for async diagnostic readiness after ${timeoutMs}ms.`,
  });
  return latest ?? { data: null };
}

async function recordAliasStep(steps, source, key, required) {
  const response = {
    ...source,
    key,
    required,
    meta: {
      ...(source.meta ?? {}),
      sourceStep: source.key,
      asyncPolled: true,
    },
  };
  steps.push(response);
  await writeStepCheckpoint(steps);
  const label = response.ok ? "OK" : required ? "FAIL" : "WARN";
  console.log(`  ${label} ${key} status=${response.status} ${response.durationMs}ms`);
  return response;
}

async function recordSyntheticStep(steps, key, method, url, { status, ok, required, message }) {
  const response = {
    key,
    method,
    url,
    ok,
    status,
    durationMs: 0,
    parseable: true,
    data: { error: message },
    text: "",
    required,
    evidenceRef: privacy.evidenceRef(message),
    redacted: { error: message },
  };
  steps.push(response);
  await writeStepCheckpoint(steps);
  const label = ok ? "OK" : required ? "FAIL" : "WARN";
  console.log(`  ${label} ${key} status=${status} 0ms`);
  return response;
}

function normalizePlanDiagnosticStatus(value) {
  if (typeof value === "string") return value;
  const numeric = Number(value);
  if (!Number.isFinite(numeric)) return "";
  return ["Researching", "ResearchReady", "QuizPending", "QuizCompleted", "PlanGenerating", "PlanGenerated", "Failed"][numeric] ?? "";
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function writeStepCheckpoint(steps, inFlight = null) {
  if (!checkpointScenario) return;
  const checkpoint = {
    runId,
    baseUrl,
    scenarioId: checkpointScenario.id,
    updatedAt: new Date().toISOString(),
    steps: [
      ...steps.map((step) => ({
      key: step.key,
      method: step.method,
      url: step.url,
      ok: step.ok,
      status: step.status,
      durationMs: step.durationMs,
      parseable: step.parseable,
      required: step.required,
      evidenceRef: step.evidenceRef,
      error: step.error,
      redacted: step.redacted,
      })),
      ...(inFlight ? [inFlight] : []),
    ],
  };
  await fs.mkdir(reportDir, { recursive: true });
  const checkpointPath = path.join(reportDir, `${slugify(checkpointScenario.id)}.checkpoint.json`);
  await safeWriteJson(checkpointPath, checkpoint);
  await safeWriteJson(path.join(reportDir, "pedagogical-quality-audit.checkpoint.json"), checkpoint);
}

async function safeWriteJson(filePath, value) {
  const payload = JSON.stringify(value, null, 2);
  const tempPath = `${filePath}.${process.pid}.${Date.now()}.tmp`;
  try {
    await fs.writeFile(tempPath, payload, "utf8");
    try {
      await fs.rename(tempPath, filePath);
    } catch {
      await fs.writeFile(filePath, payload, "utf8");
      await fs.rm(tempPath, { force: true });
    }
  } catch (error) {
    try {
      await fs.rm(tempPath, { force: true });
    } catch {
      // Best-effort cleanup only; checkpoint writes must never fail the audit itself.
    }
    console.warn(`  WARN checkpoint.write path=${path.basename(filePath)} error=${error?.code ?? error?.message ?? error}`);
  }
}

function parseQuestions(value) {
  if (Array.isArray(value)) return value;
  try {
    const parsed = JSON.parse(String(value ?? "[]"));
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

function optionId(option) {
  if (!option) return null;
  return option.id ?? option.optionId ?? option.value ?? option.text ?? option.label ?? null;
}

function slugify(value) {
  return String(value ?? "").toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
}
