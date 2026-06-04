import fs from "node:fs";
import { execFileSync } from "node:child_process";

const DEFAULT_MODEL = process.env.ORKA_AUDIT_JUDGE_MODEL || "openai/gpt-4o-mini";

export async function runOptionalJudge({ includeAiProvider, bundle }) {
  if (!includeAiProvider) {
    return { status: "disabled", llmJudgeUsed: false };
  }

  const provider = resolveJudgeProvider();
  if (!provider) {
    return {
      status: "judge_unavailable",
      llmJudgeUsed: false,
      score: null,
      rubric: {},
      evidenceRefs: [],
      riskNotes: ["No external judge provider env was available. Set OPENROUTER_API_KEY, GITHUB_TOKEN, GITHUB_MODELS_TOKEN, GEMINI_API_KEY, or configure Vertex ADC."],
    };
  }

  const payload = buildJudgePayload(bundle);
  try {
    const raw = await callJudge(provider, payload);
    const parsed = parseJudgeJson(raw);
    if (!parsed) {
      return unavailable("Judge returned non-JSON or invalid JSON.", provider.name);
    }

    const rubric = {
      plan_specificity: clamp(parsed.rubric?.plan_specificity),
      objective_alignment: clamp(parsed.rubric?.objective_alignment),
      prerequisite_sequence: clamp(parsed.rubric?.prerequisite_sequence),
      quiz_diagnostic_power: clamp(parsed.rubric?.quiz_diagnostic_power),
      misconception_coverage: clamp(parsed.rubric?.misconception_coverage),
      evidence_humility: clamp(parsed.rubric?.evidence_humility),
    };
    const score = clamp(parsed.score ?? average(Object.values(rubric)));
    return {
      status: "scored",
      llmJudgeUsed: true,
      provider: provider.name,
      model: provider.model,
      score,
      rubric,
      evidenceRefs: Array.isArray(parsed.evidenceRefs) ? parsed.evidenceRefs.slice(0, 12) : ["contentReview.plan", "contentReview.quiz"],
      riskNotes: Array.isArray(parsed.riskNotes) ? parsed.riskNotes.slice(0, 8).map(String) : [],
    };
  } catch (error) {
    return unavailable(`Judge call failed: ${error?.message ?? error}`, provider.name);
  }
}

function resolveJudgeProvider() {
  if (process.env.OPENROUTER_API_KEY) {
    return {
      name: "openrouter",
      model: DEFAULT_MODEL,
      url: "https://openrouter.ai/api/v1/chat/completions",
      headers: {
        Authorization: `Bearer ${process.env.OPENROUTER_API_KEY}`,
        "HTTP-Referer": "http://localhost",
        "X-Title": "Orka Pedagogical Audit",
      },
    };
  }

  const githubToken = process.env.GITHUB_MODELS_TOKEN || process.env.GITHUB_TOKEN;
  if (githubToken) {
    return {
      name: "github-models",
      model: process.env.ORKA_AUDIT_JUDGE_MODEL || "openai/gpt-4o-mini",
      url: "https://models.github.ai/inference/chat/completions",
      headers: { Authorization: `Bearer ${githubToken}` },
    };
  }

  const vertex = resolveVertexProvider();
  if (vertex) return vertex;

  if (process.env.GEMINI_API_KEY) {
    const model = process.env.ORKA_AUDIT_JUDGE_MODEL || readGeminiConfig().ModelTutor || "gemini-3.1-pro-preview";
    return {
      name: "gemini",
      model,
      url: `https://generativelanguage.googleapis.com/v1beta/models/${cleanGeminiModel(model)}:generateContent?key=${process.env.GEMINI_API_KEY}`,
      headers: {},
    };
  }

  return null;
}

function resolveVertexProvider() {
  const config = readGeminiConfig();
  const baseUrl = process.env.VERTEX_GEMINI_BASE_URL || config.BaseUrl;
  const useVertex = String(process.env.VERTEX_GEMINI_USE_VERTEX ?? config.UseVertexAi ?? "").toLowerCase() === "true" ||
    String(baseUrl ?? "").includes("aiplatform.googleapis.com");
  if (!useVertex || !baseUrl || !hasGcloudAdc()) return null;

  const requested = process.env.ORKA_AUDIT_JUDGE_MODEL;
  const model = requested?.startsWith("gemini-")
    ? requested
    : config.ModelTutor || config.ModelDeepPlan || config.ModelQuiz || "gemini-3.1-pro-preview";
  return {
    name: "vertex-gemini",
    model,
    url: `${String(baseUrl).replace(/\/$/, "")}/${cleanGeminiModel(model)}:generateContent`,
    headers: {},
  };
}

function readGeminiConfig() {
  for (const path of ["Orka.API/appsettings.Development.json", "Orka.API/appsettings.json"]) {
    try {
      const json = JSON.parse(fs.readFileSync(path, "utf8"));
      const gemini = json?.AI?.Gemini;
      if (gemini && Object.keys(gemini).length > 0) return gemini;
    } catch {
      // Keep audit portable; missing config only disables Vertex fallback.
    }
  }
  return {};
}

function hasGcloudAdc() {
  try {
    const token = execFileSync(gcloudCommand(), gcloudArgs(), {
      encoding: "utf8",
      stdio: ["ignore", "pipe", "ignore"],
      timeout: 15000,
    }).trim();
    return token.length > 20;
  } catch {
    return false;
  }
}

function getGcloudAccessToken() {
  return execFileSync(gcloudCommand(), gcloudArgs(), {
    encoding: "utf8",
    stdio: ["ignore", "pipe", "ignore"],
    timeout: 15000,
  }).trim();
}

function gcloudCommand() {
  return process.platform === "win32" ? "cmd.exe" : "gcloud";
}

function gcloudArgs() {
  const args = ["auth", "application-default", "print-access-token"];
  return process.platform === "win32" ? ["/c", "gcloud.cmd", ...args] : args;
}

function cleanGeminiModel(model) {
  return String(model ?? "gemini-3.1-pro-preview").replace(/^models\//, "");
}

function buildJudgePayload(bundle) {
  return {
    persona: bundle.persona,
    intent: bundle.intent,
    diagnostic: {
      questionCount: bundle.diagnostic.questionCount,
      conceptDiversity: bundle.diagnostic.conceptDiversity,
      difficultyDiversity: bundle.diagnostic.difficultyDiversity,
      blankCount: bundle.diagnostic.blankCount,
      assessmentQualityStatus: bundle.diagnostic.assessmentQualityStatus,
    },
    plan: {
      chapterCount: bundle.plan.chapterCount,
      lessonCount: bundle.plan.lessonCount,
      materialized: bundle.plan.isMaterialized,
      readinessStatus: bundle.contentReview.planReadinessStatus,
      qualityStatus: bundle.contentReview.planQualityStatus,
      repairLoopCount: bundle.plan.repairLoopCount,
      checkpointCoverage: bundle.plan.checkpointCoverage,
    },
    content: bundle.contentReview,
    sourceGrounding: bundle.sourceGrounding,
  };
}

async function callJudge(provider, payload) {
  const system = [
    "You are an independent educational quality judge.",
    "Evaluate whether a generated curriculum plan and diagnostic quiz are professionally useful.",
    "Use constructive alignment: learning objectives, assessment tasks, and instruction hooks must align.",
    "Use evidence-centered assessment: quiz items should reveal concept-level proficiency, prerequisite gaps, and misconceptions.",
    "Penalize generic module spines reused across unrelated topics.",
    "Return strict compact JSON only.",
  ].join(" ");
  const user = JSON.stringify({
    rubric: {
      plan_specificity: "0-1: module/lesson titles are topic-specific, not generic stages.",
      objective_alignment: "0-1: plan objectives, quiz concepts, and tutor/wiki hooks align.",
      prerequisite_sequence: "0-1: sequence follows prerequisite logic for the topic.",
      quiz_diagnostic_power: "0-1: quiz can identify concept gaps beyond right/wrong.",
      misconception_coverage: "0-1: items probe likely misconceptions/distractors.",
      evidence_humility: "0-1: source/grounding limits are not overclaimed.",
    },
    requiredJson: {
      score: "number 0..1",
      rubric: "object with six numeric rubric fields",
      evidenceRefs: "short array using labels like moduleTitles[0], quizItems[3]",
      riskNotes: "short array",
    },
    evidence: payload,
  });

  if (provider.name === "gemini" || provider.name === "vertex-gemini") {
    const authHeaders = provider.name === "vertex-gemini"
      ? { Authorization: `Bearer ${getGcloudAccessToken()}` }
      : {};
    const response = await fetch(provider.url, {
      method: "POST",
      headers: { "Content-Type": "application/json", ...authHeaders, ...provider.headers },
      body: JSON.stringify({
        contents: [{ role: "user", parts: [{ text: `${system}\n\n${user}` }] }],
        generationConfig: { temperature: 0.1, responseMimeType: "application/json" },
      }),
    });
    const data = await response.json().catch(() => null);
    if (!response.ok) throw new Error(`${provider.name} judge HTTP ${response.status}`);
    return data?.candidates?.[0]?.content?.parts?.map((part) => part.text).join("") ?? "";
  }

  const response = await fetch(provider.url, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...provider.headers },
    body: JSON.stringify({
      model: provider.model,
      temperature: 0.1,
      messages: [
        { role: "system", content: system },
        { role: "user", content: user },
      ],
      response_format: { type: "json_object" },
    }),
  });
  const data = await response.json().catch(() => null);
  if (!response.ok) throw new Error(`${provider.name} judge HTTP ${response.status}`);
  return data?.choices?.[0]?.message?.content ?? "";
}

function parseJudgeJson(raw) {
  try {
    return JSON.parse(String(raw ?? "").trim());
  } catch {
    const match = String(raw ?? "").match(/\{[\s\S]*\}/);
    if (!match) return null;
    try { return JSON.parse(match[0]); } catch { return null; }
  }
}

function unavailable(note, provider = "unknown") {
  return {
    status: "judge_unavailable",
    llmJudgeUsed: false,
    provider,
    score: null,
    rubric: {},
    evidenceRefs: [],
    riskNotes: [note],
  };
}

function clamp(value) {
  const n = Number(value);
  if (!Number.isFinite(n)) return 0;
  return Math.max(0, Math.min(1, n));
}

function average(values) {
  const nums = values.map(Number).filter(Number.isFinite);
  return nums.length === 0 ? 0 : nums.reduce((sum, n) => sum + n, 0) / nums.length;
}
