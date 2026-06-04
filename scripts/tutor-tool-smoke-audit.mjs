#!/usr/bin/env node

import fs from "node:fs/promises";
import net from "node:net";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(__dirname, "..");

const TUTOR_TOOLS = [
  { id: "source_search", capability: "sources_query", risk: "low", purpose: "Check learner-owned source context before source-grounded claims." },
  { id: "wiki_search", capability: "sources_query", risk: "low", purpose: "Check Orka Wiki memory for the active topic." },
  { id: "ide_last_result", capability: "ide_execution", risk: "medium", purpose: "Read the latest IDE execution summary already present in the session." },
  { id: "review_query", capability: "review_query", risk: "low", purpose: "Check spaced-repetition pressure for the active topic." },
  { id: "flashcard_query", capability: "flashcards", risk: "low", purpose: "Find active flashcards related to the current concept." },
  { id: "wolfram_alpha", capability: "wolfram_alpha", risk: "medium", purpose: "Verify bounded math or computation when needed." },
  { id: "weather", capability: "weather", risk: "low", purpose: "Fetch bounded geography/weather context for an educational example." },
  { id: "news", capability: "news", risk: "medium", purpose: "Fetch current-news evidence only for a current-events question." },
  { id: "crypto", capability: "crypto", risk: "medium", purpose: "Fetch educational market data without financial advice." },
  { id: "visual_generation", capability: "visual_generation", risk: "medium", purpose: "Suggest or create a visual learning artifact." },
  { id: "mermaid_graph", capability: "mermaid", risk: "low", purpose: "Create a local Mermaid diagram for a concept relationship." },
  { id: "knowledge_entity", capability: "knowledge_entity", risk: "low", purpose: "Fetch public entity evidence for stable educational context." },
  { id: "geo_context", capability: "geo_context", risk: "low", purpose: "Fetch public geographic context evidence." },
  { id: "socioeconomic_context", capability: "socioeconomic_context", risk: "medium", purpose: "Fetch public socioeconomic context evidence." },
  { id: "science_context", capability: "science_context", risk: "low", purpose: "Fetch public science context evidence." },
  { id: "research_context", capability: "research_context", risk: "medium", purpose: "Fetch bounded academic/research context evidence." },
  { id: "forum_signal", capability: "forum_signal", risk: "medium", purpose: "Fetch public misconception/forum pattern signals." },
];

const args = parseArgs(process.argv.slice(2));
const runId = String(args["run-id"] ?? new Date().toISOString().replace(/[-:.TZ]/g, "").slice(0, 14));
const baseUrl = trimSlash(args["base-url"] ?? args["api-url"] ?? process.env.ORKA_API_URL ?? "http://localhost:5065");
const reportDir = path.resolve(ROOT, args["report-dir"] ?? "life_tests/reports/tutor-tool-smoke");
const timeoutMs = Number(args.timeoutMs ?? args["timeout-ms"] ?? 30000);
const includeChatProbes = boolArg(args, "include-chat-probes");
const password = `OrkaToolSmoke${runId}!`;

main().catch(async (error) => {
  const message = error instanceof Error ? error.message : String(error);
  console.error(`FATAL tutor-tool-smoke-audit error: ${message}`);
  const report = baseReport();
  report.fatalError = message;
  report.releasePass = false;
  await writeReports(report);
  process.exit(2);
});

async function main() {
  console.log("Orka Tutor Tool Smoke Audit");
  console.log(`Run: ${runId}`);
  console.log(`Target: ${baseUrl}`);

  const report = baseReport();
  report.redis.localPorts = await checkRedisPorts([6379, 6380]);
  report.health = await request("GET", "/health/ready", { timeoutMs: 10000 });
  report.diagnostics = await request("GET", "/api/dev/diagnostics/config", { timeoutMs: 10000 });
  report.capabilities = await request("GET", "/api/tools/capabilities", { timeoutMs: 15000 });

  const auth = await registerAndLogin();
  report.auth = auth.safeSummary;
  if (!auth.token) {
    report.issues.push(issue("auth_failed", "critical", "Authenticated runtime tool decisions could not be tested."));
    finalize(report);
    await writeReports(report);
    process.exit(1);
  }

  const topic = await createTopic(auth.token);
  report.topic = topic.safeSummary;

  const capabilityById = new Map((report.capabilities.data?.tools ?? []).map((tool) => [normalize(tool.toolId), tool]));
  for (const tool of TUTOR_TOOLS) {
    const capability = capabilityById.get(normalize(tool.capability));
    const decision = await request("POST", "/api/tools/runtime/decide", {
      token: auth.token,
      timeoutMs,
      body: {
        toolId: tool.id,
        caller: "tutor",
        topicId: topic.id,
        purpose: tool.purpose,
        riskLevel: tool.risk,
        inputSummary: `tool=${tool.id}; audit=smoke; concept=tool_governance`,
      },
    });

    const row = {
      toolId: tool.id,
      capabilityId: tool.capability,
      capabilityStatus: capability?.status ?? "missing",
      capabilityDecision: capability?.decision ?? capability?.fallbackMode ?? null,
      runtimeOk: decision.ok,
      runtimeStatus: decision.status,
      runtimeDecision: decision.data?.decision ?? "unavailable",
      allowed: decision.data?.allowed === true,
      reasonCode: decision.data?.reasonCode ?? null,
      canGroundClaims: decision.data?.canGroundClaims === true,
      latencyMs: decision.durationMs,
      evidenceMode: decision.data?.requiredEvidenceMode ?? null,
    };
    report.tools.push(row);

    if (!capability) {
      report.issues.push(issue("capability_missing", "warning", `${tool.id} maps to missing capability ${tool.capability}.`));
    }
    if (!decision.ok) {
      report.issues.push(issue("runtime_decide_failed", "critical", `${tool.id} runtime decision failed with status ${decision.status}.`));
    }
  }

  const unknown = await request("POST", "/api/tools/runtime/decide", {
    token: auth.token,
    timeoutMs,
    body: {
      toolId: "orka_unregistered_tool_probe",
      caller: "tutor",
      topicId: topic.id,
      purpose: "Negative policy probe.",
      riskLevel: "high",
      inputSummary: "audit=negative_probe",
    },
  });
  report.negativeProbe = {
    ok: unknown.ok,
    status: unknown.status,
    allowed: unknown.data?.allowed === true,
    decision: unknown.data?.decision ?? "unavailable",
    reasonCode: unknown.data?.reasonCode ?? null,
    latencyMs: unknown.durationMs,
  };
  if (!unknown.ok || unknown.data?.allowed === true) {
    report.issues.push(issue("unknown_tool_not_denied", "critical", "Unknown tutor tool was not safely denied."));
  }

  if (includeChatProbes) {
    report.chatProbes = await runChatProbes(auth.token, topic.id);
    const runnableTools = new Set(report.tools
      .filter((tool) => tool.allowed === true)
      .map((tool) => normalize(tool.toolId)));
    for (const probe of report.chatProbes) {
      const executed = new Set((probe.toolStatuses ?? []).map((item) => normalize(item.toolId)));
      const expected = (probe.expectedTools ?? []).filter((toolId) => runnableTools.has(normalize(toolId)));
      if (!probe.ok) {
        report.issues.push(issue("chat_probe_failed", "critical", `${probe.id} chat probe failed with status ${probe.chatStatus}.`));
      } else {
        for (const toolId of expected) {
          if (!executed.has(normalize(toolId))) {
            report.issues.push(issue("chat_probe_expected_tool_not_executed", "critical", `${probe.id} did not produce expected governed tool trace ${toolId}.`));
          }
        }
      }
      if (probe.rawPayloadExposed) {
        report.issues.push(issue("chat_probe_raw_payload_exposed", "critical", `${probe.id} exposed raw provider/tool payload metadata.`));
      }
    }
  }

  finalize(report);
  await writeReports(report);
  console.log(`Report: ${path.join(reportDir, "tutor-tool-smoke-audit.md")}`);
  console.log(`Verdict: ${report.releasePass ? "PASS" : "FAIL"}`);
  process.exit(report.releasePass ? 0 : 1);
}

function baseReport() {
  return {
    runId,
    baseUrl,
    startedAt: new Date().toISOString(),
    redis: { expectedDevelopmentEndpoint: "127.0.0.1:6380", localPorts: [] },
    health: null,
    diagnostics: null,
    capabilities: null,
    auth: null,
    topic: null,
    tools: [],
    negativeProbe: null,
    chatProbes: [],
    issues: [],
    releasePass: false,
  };
}

function finalize(report) {
  if (!report.health?.ok) {
    report.issues.push(issue("health_ready_failed", "critical", `/health/ready returned ${report.health?.status ?? 0}.`));
  }
  if (!report.capabilities?.ok) {
    report.issues.push(issue("capabilities_failed", "critical", `/api/tools/capabilities returned ${report.capabilities?.status ?? 0}.`));
  }

  const diagnosticsEndpoint = report.diagnostics?.data?.redisConnection?.endpoint;
  if (diagnosticsEndpoint) {
    report.redis.activeApiEndpoint = diagnosticsEndpoint;
  }

  const criticalCount = report.issues.filter((item) => item.severity === "critical").length;
  report.finishedAt = new Date().toISOString();
  report.releasePass = criticalCount === 0;
}

async function registerAndLogin() {
  const email = `orka-tool-smoke-${runId}@orka.local`;
  const firstName = "ToolSmoke";
  const register = await request("POST", "/api/auth/register", {
    timeoutMs,
    body: { firstName, lastName: "Audit", name: "ToolSmoke Audit", email, password },
  });
  const login = await request("POST", "/api/auth/login", {
    timeoutMs,
    body: { email, password },
  });

  const token = login.data?.token ?? register.data?.token ?? null;
  return {
    token,
    safeSummary: {
      registerStatus: register.status,
      loginStatus: login.status,
      authenticated: Boolean(token),
    },
  };
}

async function createTopic(token) {
  const response = await request("POST", "/api/topics", {
    token,
    timeoutMs,
    body: {
      title: `Tutor Tool Smoke ${runId}`,
      emoji: "T",
      category: "audit",
    },
  });
  return {
    id: response.data?.id ?? null,
    safeSummary: {
      status: response.status,
      ok: response.ok,
      topicCreated: Boolean(response.data?.id),
    },
  };
}

async function runChatProbes(token, topicId) {
  if (!topicId) {
    return [{ id: "visual-diagram", ok: false, skipped: true, reason: "topic_unavailable" }];
  }

  const probes = [
    {
      id: "visual-diagram",
      prompt: "Explain this concept with a Mermaid flow diagram and a visual artifact idea: how binary search narrows a sorted list.",
      expectedTools: ["mermaid_graph", "visual_generation"],
    },
  ];

  const results = [];
  for (const probe of probes) {
    const chat = await request("POST", "/api/chat/message", {
      token,
      timeoutMs: Math.max(timeoutMs, 180000),
      body: { content: probe.prompt, topicId, isPlanMode: false },
    });
    const traceId = chat.data?.metadata?.tutorActionTraceId ?? chat.data?.tutorActionTraceId ?? null;
    const trace = traceId
      ? await request("GET", `/api/tutor/trace/${traceId}`, { token, timeoutMs: 90000 })
      : { ok: false, status: 0, durationMs: 0, data: null };
    const toolStatuses = extractToolStatuses(trace.data);
    results.push({
      id: probe.id,
      ok: chat.ok,
      chatStatus: chat.status,
      chatLatencyMs: chat.durationMs,
      traceStatus: trace.status,
      traceLatencyMs: trace.durationMs,
      traceIdPresent: Boolean(traceId),
      expectedTools: probe.expectedTools,
      toolStatuses,
      rawPayloadExposed: trace.data?.professionalContract?.rawPayloadExposed === true,
    });
  }

  return results;
}

async function request(method, url, { token, body, timeoutMs: perRequestTimeout = timeoutMs } = {}) {
  const started = performance.now();
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), perRequestTimeout);
  const headers = { Accept: "application/json" };
  if (token) headers.Authorization = `Bearer ${token}`;
  if (body !== undefined) headers["Content-Type"] = "application/json";

  try {
    const response = await fetch(`${baseUrl}${url}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: controller.signal,
    });
    const text = await response.text();
    return {
      ok: response.ok,
      status: response.status,
      durationMs: Math.round(performance.now() - started),
      data: parseJson(text),
    };
  } catch (error) {
    return {
      ok: false,
      status: 0,
      durationMs: Math.round(performance.now() - started),
      data: { error: error instanceof Error ? error.message : String(error) },
    };
  } finally {
    clearTimeout(timeout);
  }
}

async function checkRedisPorts(ports) {
  return Promise.all(ports.map(async (port) => ({
    host: "127.0.0.1",
    port,
    tcpOpen: await tcpOpen("127.0.0.1", port, 1500),
  })));
}

function tcpOpen(host, port, timeout) {
  return new Promise((resolve) => {
    const socket = new net.Socket();
    const done = (value) => {
      socket.destroy();
      resolve(value);
    };
    socket.setTimeout(timeout);
    socket.once("connect", () => done(true));
    socket.once("timeout", () => done(false));
    socket.once("error", () => done(false));
    socket.connect(port, host);
  });
}

async function writeReports(report) {
  await fs.mkdir(reportDir, { recursive: true });
  await fs.writeFile(path.join(reportDir, "tutor-tool-smoke-audit.json"), `${JSON.stringify(redactReport(report), null, 2)}\n`, "utf8");
  await fs.writeFile(path.join(reportDir, "tutor-tool-smoke-audit.md"), renderMarkdown(report), "utf8");
}

function renderMarkdown(report) {
  const redisRows = report.redis.localPorts
    .map((port) => `| ${port.host}:${port.port} | ${port.tcpOpen ? "open" : "closed"} |`)
    .join("\n");
  const toolRows = report.tools
    .map((tool) => `| ${tool.toolId} | ${tool.capabilityStatus} | ${tool.runtimeDecision} | ${tool.allowed ? "yes" : "no"} | ${tool.reasonCode ?? ""} | ${tool.latencyMs} | ${tool.evidenceMode ?? ""} |`)
    .join("\n");
  const issues = report.issues.length === 0
    ? "- None"
    : report.issues.map((item) => `- ${item.severity.toUpperCase()} ${item.code}: ${item.message}`).join("\n");
  const chatRows = report.chatProbes.length === 0
    ? "| not_run | - | - | - | - |"
    : report.chatProbes
      .map((probe) => `| ${probe.id} | ${probe.ok ? "ok" : "fail"} | ${probe.chatLatencyMs ?? 0} | ${probe.traceIdPresent ? "yes" : "no"} | ${formatToolStatuses(probe.toolStatuses)} |`)
      .join("\n");

  return `# Tutor Tool Smoke Audit

- Run: ${report.runId}
- Base URL: ${report.baseUrl}
- Verdict: ${report.releasePass ? "PASS" : "FAIL"}
- Chat probes: ${includeChatProbes ? "enabled" : "disabled"}
- Active API Redis endpoint: ${report.redis.activeApiEndpoint ?? "unavailable"}

## Redis Ports

| Endpoint | TCP |
| --- | --- |
${redisRows || "| unavailable | unavailable |"}

## Tutor Tool Runtime Decisions

| Tool | Capability | Runtime decision | Allowed | Reason | Latency ms | Evidence mode |
| --- | --- | --- | --- | --- | ---: | --- |
${toolRows || "| none | none | none | no | missing | 0 | none |"}

## Negative Probe

- Unknown tool allowed: ${report.negativeProbe?.allowed === true ? "yes" : "no"}
- Decision: ${report.negativeProbe?.decision ?? "unavailable"}
- Reason: ${report.negativeProbe?.reasonCode ?? "unavailable"}

## Tutor Chat Probes

| Probe | Chat | Latency ms | Trace | Tool statuses |
| --- | --- | ---: | --- | --- |
${chatRows}

## Issues

${issues}
`;
}

function extractToolStatuses(trace) {
  const contract = trace?.professionalContract ?? trace?.ProfessionalContract ?? null;
  const statuses = contract?.toolStatuses ?? contract?.ToolStatuses ?? [];
  if (!Array.isArray(statuses)) return [];
  return statuses.map((item) => ({
    toolId: item.toolId ?? item.ToolId ?? "unknown",
    status: item.status ?? item.Status ?? "unknown",
    success: item.success ?? item.Success ?? false,
    provider: item.provider ?? item.Provider ?? "unknown",
    fallbackReason: item.fallbackReason ?? item.FallbackReason ?? null,
  }));
}

function formatToolStatuses(statuses) {
  if (!Array.isArray(statuses) || statuses.length === 0) return "none";
  return statuses
    .map((item) => `${item.toolId}:${item.status}${item.success ? "" : ":not_success"}`)
    .slice(0, 6)
    .join("<br>");
}

function redactReport(report) {
  return JSON.parse(JSON.stringify(report, (key, value) => {
    if (/token|password|secret|authorization/i.test(key)) return "[redacted]";
    return value;
  }));
}

function issue(code, severity, message) {
  return { code, severity, message };
}

function parseArgs(argv) {
  const parsed = {};
  for (let i = 0; i < argv.length; i += 1) {
    const raw = argv[i];
    const match = raw.match(/^--([^=]+)(?:=(.*))?$/);
    if (!match) continue;
    const next = argv[i + 1];
    if (match[2] !== undefined) parsed[match[1]] = match[2];
    else if (next && !next.startsWith("--")) {
      parsed[match[1]] = next;
      i += 1;
    } else parsed[match[1]] = "true";
  }
  return parsed;
}

function boolArg(parsed, name) {
  const value = parsed[name];
  return value === true || value === "true" || value === "1" || value === "yes";
}

function parseJson(text) {
  if (!text) return null;
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

function trimSlash(value) {
  return String(value).replace(/\/+$/, "");
}

function normalize(value) {
  return String(value ?? "").trim().toLowerCase();
}
