/*
 * AgentDashboard — OrkaOS Multi-Agent Orchestration & Operating System Dashboard
 * Premium dark dashboard inspired by Linear + Cursor IDE aesthetics.
 * Uses only mock data — zero API calls, safe to render standalone.
 */

import { useEffect, useRef, useState } from "react";
import {
  Activity,
  Brain,
  ChevronRight,
  CircleCheck,
  Clock,
  Cpu,
  Database,
  FileText,
  Flame,
  Layers,
  LayoutDashboard,
  Loader2,
  MessageSquare,
  Mic,
  PenLine,
  Radio,
  Search,
  Server,
  Sparkles,
  Terminal,
  TrendingUp,
  Triangle,
  Zap,
} from "lucide-react";

// ── Types ──────────────────────────────────────────────────────────────────

type AgentStatus = "thinking" | "executing" | "idle" | "waiting" | "done";
type LLMProvider = "Gemini Pro" | "Gemini Flash" | "DeepSeek R1" | "Edge-TTS" | "Orka-Eval" | "Internal";

interface Agent {
  id: string;
  name: string;
  role: string;
  provider: LLMProvider;
  status: AgentStatus;
  task: string;
  tokensSent: number;
  latencyMs: number;
  icon: React.ElementType;
}

interface LogEntry {
  id: number;
  ts: string;
  level: "info" | "trace" | "warn" | "success" | "system";
  agent: string;
  message: string;
}

interface MetricCard {
  label: string;
  value: string;
  sub: string;
  icon: React.ElementType;
  accent: string;
  trend?: string;
}

// ── Mock Data ──────────────────────────────────────────────────────────────

const INITIAL_AGENTS: Agent[] = [
  {
    id: "tutor",
    name: "Tutor Agent",
    role: "Pedagogical Coach",
    provider: "Gemini Pro",
    status: "thinking",
    task: "Generating Socratic follow-up for Calculus session #1138",
    tokensSent: 3_812,
    latencyMs: 1_240,
    icon: Brain,
  },
  {
    id: "planner",
    name: "DeepPlan Agent",
    role: "Curriculum Architect",
    provider: "DeepSeek R1",
    status: "executing",
    task: "Structuring 12-week roadmap · Algorithms & Data Structures",
    tokensSent: 8_441,
    latencyMs: 2_870,
    icon: Layers,
  },
  {
    id: "diagnostic",
    name: "Diagnostic Agent",
    role: "Knowledge Evaluator",
    provider: "Gemini Flash",
    status: "done",
    task: "Completed gap analysis · topic:linear-algebra · score 74%",
    tokensSent: 1_620,
    latencyMs: 680,
    icon: Search,
  },
  {
    id: "quiz",
    name: "Quiz Agent",
    role: "Assessment Engine",
    provider: "Gemini Flash",
    status: "idle",
    task: "Awaiting session trigger · last run 4m ago",
    tokensSent: 940,
    latencyMs: 510,
    icon: CircleCheck,
  },
  {
    id: "summarizer",
    name: "Summarizer Agent",
    role: "Content Distiller",
    provider: "Gemini Flash",
    status: "executing",
    task: "Chunking & indexing 3 PDFs → vector store · RAG pipeline",
    tokensSent: 11_200,
    latencyMs: 1_950,
    icon: FileText,
  },
  {
    id: "audio",
    name: "Audio Studio Agent",
    role: "TTS Narrator",
    provider: "Edge-TTS",
    status: "idle",
    task: "Idle · last narration: 'Osmosis & Diffusion overview'",
    tokensSent: 0,
    latencyMs: 210,
    icon: Mic,
  },
  {
    id: "intent",
    name: "Intent Classifier",
    role: "Router & Dispatcher",
    provider: "Orka-Eval",
    status: "thinking",
    task: "Classifying incoming message → routing to TutorAgent",
    tokensSent: 128,
    latencyMs: 95,
    icon: Radio,
  },
  {
    id: "evaluator",
    name: "Evaluator Agent",
    role: "Response Quality Guard",
    provider: "Orka-Eval",
    status: "waiting",
    task: "Queued · awaiting Tutor Agent output to score",
    tokensSent: 260,
    latencyMs: 140,
    icon: Sparkles,
  },
];

const RAG_SPARKLINE = [18, 22, 19, 31, 28, 35, 33, 40, 38, 45, 42, 50, 47, 55, 52, 60, 57, 64, 61, 68, 66, 72];

const INITIAL_LOGS: LogEntry[] = [
  { id: 1,  ts: "09:41:02.112", level: "system",  agent: "ORCHESTRATOR", message: "OrkaOS kernel boot complete · runtime v1.4.2" },
  { id: 2,  ts: "09:41:02.340", level: "info",    agent: "INTENT",        message: "Message received · userId:u_8a3f · 47 tokens" },
  { id: 3,  ts: "09:41:02.435", level: "trace",   agent: "INTENT",        message: "Classification → TUTOR_QUERY (conf: 0.97)" },
  { id: 4,  ts: "09:41:02.437", level: "info",    agent: "TUTOR",         message: "Session #1138 · topic:calculus · model:gemini-pro" },
  { id: 5,  ts: "09:41:03.800", level: "trace",   agent: "TUTOR",         message: "RAG retrieve · k=5 · latency 340ms · score 0.88" },
  { id: 6,  ts: "09:41:05.210", level: "info",    agent: "TUTOR",         message: "Streaming response · chunk 1/? · first-token 1240ms" },
  { id: 7,  ts: "09:41:07.002", level: "trace",   agent: "EVALUATOR",     message: "Scoring output · coherence:0.91 · relevance:0.89" },
  { id: 8,  ts: "09:41:07.500", level: "success", agent: "EVALUATOR",     message: "Quality gate PASSED · response approved for delivery" },
  { id: 9,  ts: "09:41:08.310", level: "info",    agent: "SUMMARIZER",    message: "Vector index job queued · 3 new PDFs · chunk_size:512" },
  { id: 10, ts: "09:41:09.010", level: "trace",   agent: "SUMMARIZER",    message: "Embedding batch 1/6 · model:text-embedding-004" },
  { id: 11, ts: "09:41:10.440", level: "warn",    agent: "DEEPPLAN",      message: "Token budget 85% · compressing chain-of-thought" },
  { id: 12, ts: "09:41:10.980", level: "info",    agent: "DEEPPLAN",      message: "Curriculum draft · 12 weeks · 48 subtopics generated" },
  { id: 13, ts: "09:41:11.670", level: "success", agent: "DIAGNOSTIC",    message: "Gap analysis complete · weak areas: [recursion, trees]" },
  { id: 14, ts: "09:41:12.200", level: "trace",   agent: "INTENT",        message: "Next message queued · userId:u_8a3f · ETA 400ms" },
];

const NEW_LOG_TEMPLATES: Omit<LogEntry, "id" | "ts">[] = [
  { level: "trace",   agent: "TUTOR",      message: "Generating follow-up question · Socratic method engaged" },
  { level: "info",    agent: "SUMMARIZER", message: "Embedding batch 2/6 complete · 512 vectors stored" },
  { level: "trace",   agent: "INTENT",     message: "Heartbeat · queue depth: 2 · avg latency 92ms" },
  { level: "success", agent: "TUTOR",      message: "Response delivered · 318 tokens · stream closed" },
  { level: "info",    agent: "QUIZ",       message: "Session trigger received · 3 questions generated" },
  { level: "trace",   agent: "EVALUATOR",  message: "Batch score cycle · 4 responses evaluated" },
  { level: "warn",    agent: "DEEPPLAN",   message: "Rate limit headroom 12% · throttling enabled" },
  { level: "info",    agent: "DIAGNOSTIC", message: "Progress delta recalculated · topic:algorithms +6%" },
  { level: "success", agent: "SUMMARIZER", message: "All 6 embedding batches complete · RAG index updated" },
  { level: "trace",   agent: "INTENT",     message: "Classification → PLAN_QUERY (conf: 0.93)" },
];

const STATUS_CONFIG: Record<AgentStatus, { label: string; color: string; dot: string; ring: string }> = {
  thinking:  { label: "Thinking",  color: "text-[#6ed7ce]",  dot: "bg-[#6ed7ce]",  ring: "ring-[#6ed7ce]/20" },
  executing: { label: "Executing", color: "text-[#dac17a]",  dot: "bg-[#dac17a]",  ring: "ring-[#dac17a]/20" },
  idle:      { label: "Idle",      color: "text-[#8f9894]",  dot: "bg-[#8f9894]",  ring: "ring-white/10" },
  waiting:   { label: "Waiting",   color: "text-[#b4a0f0]",  dot: "bg-[#b4a0f0]",  ring: "ring-[#b4a0f0]/20" },
  done:      { label: "Done",      color: "text-[#a7e879]",  dot: "bg-[#a7e879]",  ring: "ring-[#a7e879]/20" },
};

const PROVIDER_CONFIG: Record<LLMProvider, { bg: string; text: string; border: string }> = {
  "Gemini Pro":   { bg: "bg-blue-500/10",     text: "text-blue-300",   border: "border-blue-500/20" },
  "Gemini Flash": { bg: "bg-sky-500/10",      text: "text-sky-300",    border: "border-sky-500/20" },
  "DeepSeek R1":  { bg: "bg-[#dac17a]/10",   text: "text-[#dac17a]", border: "border-[#dac17a]/20" },
  "Edge-TTS":     { bg: "bg-[#b4a0f0]/10",   text: "text-[#b4a0f0]", border: "border-[#b4a0f0]/20" },
  "Orka-Eval":    { bg: "bg-[#6ed7ce]/10",   text: "text-[#6ed7ce]", border: "border-[#6ed7ce]/20" },
  "Internal":     { bg: "bg-white/5",         text: "text-[#8f9894]", border: "border-white/10" },
};

const LOG_LEVEL_CONFIG = {
  info:    { color: "text-[#8f9894]",  prefix: "INFO " },
  trace:   { color: "text-[#5a6360]",  prefix: "TRACE" },
  warn:    { color: "text-[#dac17a]",  prefix: "WARN " },
  success: { color: "text-[#a7e879]",  prefix: "OK   " },
  system:  { color: "text-[#6ed7ce]",  prefix: "SYS  " },
};

function now(): string {
  const d = new Date();
  return `${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}:${String(d.getSeconds()).padStart(2, "0")}.${String(d.getMilliseconds()).padStart(3, "0")}`;
}

// ── Sub-components ─────────────────────────────────────────────────────────

function PulsingDot({ color }: { color: string }) {
  return (
    <span className="relative flex h-2 w-2 shrink-0">
      <span className={`animate-ping absolute inline-flex h-full w-full rounded-full ${color} opacity-60`} />
      <span className={`relative inline-flex h-2 w-2 rounded-full ${color}`} />
    </span>
  );
}

function StaticDot({ color }: { color: string }) {
  return <span className={`inline-flex h-2 w-2 rounded-full ${color} shrink-0`} />;
}

function MetricCardWidget({ card }: { card: MetricCard }) {
  const Icon = card.icon;
  return (
    <div className="group relative flex flex-col gap-3 rounded-xl border border-white/[0.07] bg-[#0d1014]/80 p-4 backdrop-blur-sm transition-all duration-200 hover:border-white/[0.12] hover:bg-[#111418]/80">
      <div className="flex items-start justify-between">
        <div
          className="flex h-8 w-8 items-center justify-center rounded-lg"
          style={{ background: `${card.accent}18`, border: `1px solid ${card.accent}30` }}
        >
          <Icon className="h-4 w-4" style={{ color: card.accent }} />
        </div>
        {card.trend && (
          <span className="flex items-center gap-0.5 rounded-full border border-[#a7e879]/20 bg-[#a7e879]/10 px-1.5 py-0.5 text-[10px] font-medium text-[#a7e879]">
            <TrendingUp className="h-2.5 w-2.5" />
            {card.trend}
          </span>
        )}
      </div>
      <div>
        <p className="text-[22px] font-semibold leading-none tracking-tight text-[#f4f6f3]">{card.value}</p>
        <p className="mt-1 text-xs font-medium text-[#8f9894]">{card.label}</p>
        <p className="mt-0.5 text-[11px] text-[#5a6360]">{card.sub}</p>
      </div>
    </div>
  );
}

function AgentRow({ agent }: { agent: Agent }) {
  const sc = STATUS_CONFIG[agent.status];
  const pc = PROVIDER_CONFIG[agent.provider];
  const Icon = agent.icon;
  const isActive = agent.status === "thinking" || agent.status === "executing";

  return (
    <div
      className={`group flex items-start gap-3 rounded-lg border px-3 py-3 transition-all duration-200 hover:bg-white/[0.025] ${
        isActive ? `border-white/[0.09] ring-1 ${sc.ring}` : "border-white/[0.05]"
      }`}
    >
      <div className="mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-md border border-white/[0.08] bg-white/[0.04]">
        <Icon className="h-3.5 w-3.5 text-[#8f9894]" />
      </div>

      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-[13px] font-semibold text-[#f4f6f3]">{agent.name}</span>
          <span className="text-[11px] text-[#5a6360]">{agent.role}</span>
        </div>
        <p className="mt-1 truncate text-[11px] text-[#8f9894]">{agent.task}</p>
        <div className="mt-2 flex flex-wrap items-center gap-2">
          <span
            className={`inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 text-[10px] font-medium ${pc.bg} ${pc.text} ${pc.border}`}
          >
            {agent.provider}
          </span>
          <span className="flex items-center gap-1.5 text-[10px] text-[#5a6360]">
            <Zap className="h-2.5 w-2.5" />
            {agent.tokensSent.toLocaleString()}t
          </span>
          <span className="flex items-center gap-1.5 text-[10px] text-[#5a6360]">
            <Clock className="h-2.5 w-2.5" />
            {agent.latencyMs}ms
          </span>
        </div>
      </div>

      <div className="flex shrink-0 items-center gap-1.5">
        {isActive ? <PulsingDot color={sc.dot} /> : <StaticDot color={sc.dot} />}
        <span className={`text-[11px] font-medium ${sc.color}`}>{sc.label}</span>
      </div>
    </div>
  );
}

function RagSparkline({ data }: { data: number[] }) {
  const max = Math.max(...data);
  const min = Math.min(...data);
  const range = max - min || 1;
  const w = 400;
  const h = 64;
  const padX = 8;
  const points = data.map((v, i) => {
    const x = padX + (i / (data.length - 1)) * (w - padX * 2);
    const y = h - 8 - ((v - min) / range) * (h - 16);
    return `${x},${y}`;
  });
  const polyline = points.join(" ");
  const area = `${padX},${h - 8} ${polyline} ${w - padX},${h - 8}`;

  return (
    <svg viewBox={`0 0 ${w} ${h}`} className="h-16 w-full" preserveAspectRatio="none">
      <defs>
        <linearGradient id="rag-grad" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="#6ed7ce" stopOpacity="0.25" />
          <stop offset="100%" stopColor="#6ed7ce" stopOpacity="0" />
        </linearGradient>
      </defs>
      <polygon points={area} fill="url(#rag-grad)" />
      <polyline
        points={polyline}
        fill="none"
        stroke="#6ed7ce"
        strokeWidth="1.5"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}

// ── Main Component ──────────────────────────────────────────────────────────

export default function AgentDashboard() {
  const [logs, setLogs] = useState<LogEntry[]>(INITIAL_LOGS);
  const [agents, setAgents] = useState<Agent[]>(INITIAL_AGENTS);
  const logRef = useRef<HTMLDivElement>(null);
  const logIdRef = useRef(INITIAL_LOGS.length + 1);
  const templateIdxRef = useRef(0);

  // Simulate live log stream
  useEffect(() => {
    const interval = setInterval(() => {
      const tmpl = NEW_LOG_TEMPLATES[templateIdxRef.current % NEW_LOG_TEMPLATES.length];
      templateIdxRef.current++;
      const entry: LogEntry = { ...tmpl, id: logIdRef.current++, ts: now() };
      setLogs((prev) => {
        const next = [...prev, entry];
        return next.length > 80 ? next.slice(next.length - 80) : next;
      });
    }, 1_800);
    return () => clearInterval(interval);
  }, []);

  // Auto-scroll terminal
  useEffect(() => {
    if (logRef.current) {
      logRef.current.scrollTop = logRef.current.scrollHeight;
    }
  }, [logs]);

  // Simulate agent status churn
  useEffect(() => {
    const cycle: AgentStatus[] = ["thinking", "executing", "waiting", "done", "idle"];
    const interval = setInterval(() => {
      setAgents((prev) =>
        prev.map((a) => {
          if (Math.random() > 0.75) {
            const next = cycle[Math.floor(Math.random() * cycle.length)];
            return { ...a, status: next };
          }
          return a;
        })
      );
    }, 4_000);
    return () => clearInterval(interval);
  }, []);

  const activeCount = agents.filter((a) => a.status === "thinking" || a.status === "executing").length;
  const totalTokens = agents.reduce((s, a) => s + a.tokensSent, 0);
  const avgLatency = Math.round(agents.filter((a) => a.latencyMs > 0).reduce((s, a, _, arr) => s + a.latencyMs / arr.length, 0));

  const METRIC_CARDS: MetricCard[] = [
    {
      label: "Active Agents",
      value: `${activeCount} / ${agents.length}`,
      sub: "Thinking + Executing now",
      icon: Activity,
      accent: "#6ed7ce",
      trend: "+2 from 1h ago",
    },
    {
      label: "RAG Document Sync",
      value: "3 PDFs",
      sub: "Indexing · 6 embedding batches",
      icon: Database,
      accent: "#a7e879",
    },
    {
      label: "Total Token Usage",
      value: `${(totalTokens / 1000).toFixed(1)}k`,
      sub: "Session aggregate · all agents",
      icon: Cpu,
      accent: "#dac17a",
      trend: "↑12% vs last session",
    },
    {
      label: "Avg LLM Latency",
      value: `${avgLatency}ms`,
      sub: "Across active model calls",
      icon: Zap,
      accent: "#b4a0f0",
    },
  ];

  return (
    <div className="flex h-full flex-col overflow-auto bg-transparent">
      {/* ── Header ── */}
      <div className="flex shrink-0 items-center justify-between border-b border-white/[0.06] px-6 py-4">
        <div className="flex items-center gap-3">
          <div className="flex h-8 w-8 items-center justify-center rounded-lg border border-[#6ed7ce]/25 bg-[#6ed7ce]/10">
            <LayoutDashboard className="h-4 w-4 text-[#6ed7ce]" />
          </div>
          <div>
            <h1 className="text-[15px] font-semibold leading-none text-[#f4f6f3]">OrkaOS Control Center</h1>
            <p className="mt-0.5 text-[11px] text-[#5a6360]">Multi-Agent AI Orchestration Dashboard</p>
          </div>
        </div>
        <div className="flex items-center gap-2 rounded-full border border-[#a7e879]/20 bg-[#a7e879]/8 px-3 py-1.5">
          <PulsingDot color="bg-[#a7e879]" />
          <span className="text-[11px] font-medium text-[#a7e879]">System Operational</span>
        </div>
      </div>

      <div className="flex-1 overflow-auto p-5">
        {/* ── Top Metrics Row ── */}
        <div className="mb-5 grid grid-cols-2 gap-3 lg:grid-cols-4">
          {METRIC_CARDS.map((card) => (
            <MetricCardWidget key={card.label} card={card} />
          ))}
        </div>

        {/* ── Main Grid ── */}
        <div className="grid grid-cols-1 gap-4 xl:grid-cols-[1fr_360px]">
          {/* Left col */}
          <div className="flex flex-col gap-4">
            {/* Agents Panel */}
            <div className="rounded-xl border border-white/[0.07] bg-[#0d1014]/80">
              <div className="flex items-center justify-between border-b border-white/[0.06] px-4 py-3">
                <div className="flex items-center gap-2">
                  <Server className="h-3.5 w-3.5 text-[#8f9894]" />
                  <span className="text-[13px] font-semibold text-[#f4f6f3]">Active Agents</span>
                  <span className="rounded-full border border-white/10 bg-white/[0.05] px-1.5 py-0.5 text-[10px] text-[#8f9894]">
                    {agents.length}
                  </span>
                </div>
                <div className="flex items-center gap-1.5 text-[11px] text-[#5a6360]">
                  <Loader2 className="h-3 w-3 animate-spin" />
                  Live
                </div>
              </div>
              <div className="flex flex-col gap-1.5 p-3">
                {agents.map((agent) => (
                  <AgentRow key={agent.id} agent={agent} />
                ))}
              </div>
            </div>

            {/* RAG & Vector DB */}
            <div className="rounded-xl border border-white/[0.07] bg-[#0d1014]/80">
              <div className="flex items-center justify-between border-b border-white/[0.06] px-4 py-3">
                <div className="flex items-center gap-2">
                  <Database className="h-3.5 w-3.5 text-[#8f9894]" />
                  <span className="text-[13px] font-semibold text-[#f4f6f3]">RAG & Vector DB Performance</span>
                </div>
                <span className="rounded-full border border-[#6ed7ce]/20 bg-[#6ed7ce]/8 px-2 py-0.5 text-[10px] font-medium text-[#6ed7ce]">
                  Chroma · Local
                </span>
              </div>
              <div className="px-4 pt-3 pb-1">
                <div className="mb-3 grid grid-cols-3 gap-3">
                  {[
                    { label: "Indexed Vectors",   value: "24,813", icon: Layers },
                    { label: "Avg Search Latency", value: "38ms",   icon: Zap },
                    { label: "Documents Stored",   value: "147",    icon: FileText },
                  ].map(({ label, value, icon: Icon }) => (
                    <div key={label} className="rounded-lg border border-white/[0.05] bg-white/[0.025] px-3 py-2.5">
                      <div className="flex items-center gap-1.5">
                        <Icon className="h-3 w-3 text-[#5a6360]" />
                        <span className="text-[11px] text-[#5a6360]">{label}</span>
                      </div>
                      <p className="mt-1 text-[17px] font-semibold text-[#f4f6f3]">{value}</p>
                    </div>
                  ))}
                </div>
                <div className="rounded-lg border border-white/[0.05] bg-white/[0.02] px-2 py-2">
                  <p className="mb-1 px-1 text-[10px] text-[#5a6360]">Search latency trend · last 22 queries (ms)</p>
                  <RagSparkline data={RAG_SPARKLINE} />
                </div>
              </div>

            </div>
          </div>

          {/* Right col — Terminal */}
          <div className="flex flex-col rounded-xl border border-white/[0.07] bg-[#0d1014]/80">
            <div className="flex shrink-0 items-center justify-between border-b border-white/[0.06] px-4 py-3">
              <div className="flex items-center gap-2">
                <Terminal className="h-3.5 w-3.5 text-[#8f9894]" />
                <span className="text-[13px] font-semibold text-[#f4f6f3]">Agent Execution Log</span>
              </div>
              <div className="flex items-center gap-1.5">
                <PulsingDot color="bg-[#a7e879]" />
                <span className="text-[10px] font-medium text-[#a7e879]">Live</span>
              </div>
            </div>

            {/* Terminal window chrome */}
            <div className="flex shrink-0 items-center gap-1.5 border-b border-white/[0.04] bg-white/[0.02] px-4 py-1.5">
              <span className="h-2.5 w-2.5 rounded-full bg-[#ff7b7b]/60" />
              <span className="h-2.5 w-2.5 rounded-full bg-[#dac17a]/60" />
              <span className="h-2.5 w-2.5 rounded-full bg-[#a7e879]/60" />
              <span className="ml-2 text-[10px] text-[#3a403d]">orka-os · agent-runtime</span>
            </div>

            <div
              ref={logRef}
              className="flex-1 overflow-y-auto p-3 font-mono text-[11px] leading-relaxed"
              style={{ minHeight: 0, maxHeight: "calc(100vh - 340px)" }}
            >
              {logs.map((log) => {
                const lc = LOG_LEVEL_CONFIG[log.level];
                return (
                  <div key={log.id} className="flex gap-2 py-[2px]">
                    <span className="shrink-0 text-[#3a403d]">{log.ts}</span>
                    <span className={`w-[34px] shrink-0 font-bold ${lc.color}`}>{lc.prefix}</span>
                    <span className="w-[68px] shrink-0 text-[#5a6360]">[{log.agent}]</span>
                    <span className={`break-all ${lc.color}`}>{log.message}</span>
                  </div>
                );
              })}
              {/* blinking cursor */}
              <div className="flex gap-2 py-[2px]">
                <span className="shrink-0 text-[#3a403d]">{now()}</span>
                <span className="w-[34px] shrink-0 text-[#5a6360]">     </span>
                <span className="w-[68px] shrink-0 text-[#5a6360]">     </span>
                <span className="animate-pulse text-[#6ed7ce]">_</span>
              </div>
            </div>

            {/* Terminal footer — legend */}
            <div className="shrink-0 border-t border-white/[0.05] px-3 py-2">
              <div className="flex flex-wrap gap-x-3 gap-y-1">
                {Object.entries(LOG_LEVEL_CONFIG).map(([lvl, cfg]) => (
                  <span key={lvl} className={`text-[10px] ${cfg.color}`}>
                    {cfg.prefix.trim()} ·{" "}
                    <span className="capitalize text-[#3a403d]">{lvl}</span>
                  </span>
                ))}
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
