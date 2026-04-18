import { useState, useEffect, useCallback } from "react";
import {
  Activity,
  Zap,
  AlertTriangle,
  CheckCircle2,
  Clock,
  TrendingUp,
  DollarSign,
  Award,
  RefreshCw,
  MessageSquare,
  Database,
  Cpu,
  WifiOff,
  Layers,
} from "lucide-react";
import { DashboardAPI } from "@/services/api";

// ──────────────────────────────────────────────
// Types from backend
// ──────────────────────────────────────────────
interface AgentStat {
  agentRole: string;
  avgLatencyMs: number;
  totalCalls: number;
  errorCount: number;
  errorRatePct: number;
  lastProvider: string;
  // SQL AgentEvaluations kaynaklı kalite metrikleri
  avgQualityScore: number;
  totalEvals: number;
  goldCount: number;
  warnCount: number;
  status: "online" | "degraded" | "critical" | "idle";
}

interface EvaluatorLog {
  score: number;
  feedback: string;
  recordedAt: string;
  agentRole?: string;
  quality: "gold" | "good" | "ok" | "warn";
}

interface ProviderUsage {
  provider: string;
  callCount: number;
  errorCount: number;
  percentage: number;
  avgLatencyMs: number;
}

interface SystemHealth {
  tokens: { total: number; costUSD: number };
  pedagogy: { score: number; masteredTopics: number };
  sessions: { total: number; lastDate: string | null };
  llmops: { avgEvaluatorScore: number; totalEvaluations: number; recentLogs: EvaluatorLog[] };
  agents: AgentStat[];
  modelMix: ProviderUsage[];
}

// Provider etiketleri — failover sırasına göre rol anlatır
const PROVIDER_LABELS: Record<string, { label: string; rank: number }> = {
  GitHub:  { label: "GitHub (Primary)",  rank: 1 },
  Groq:    { label: "Groq (Fallback 1)", rank: 2 },
  Gemini:  { label: "Gemini (Fallback 2)", rank: 3 },
  Unknown: { label: "Bilinmeyen",        rank: 99 },
};

function providerMeta(p: string) {
  return PROVIDER_LABELS[p] ?? { label: p, rank: 50 };
}

/**
 * Evaluator feedback string'i "[HALL] [F:5 P:4 C:4] text..." formatında
 * gömülü multi-dimensional skor taşıyabilir. Parse edip ayrı alanlara böler.
 */
function parseEvaluatorFeedback(raw: string): {
  hallucinationRisk: boolean;
  factual: number | null;
  pedagogy: number | null;
  context: number | null;
  text: string;
} {
  let text = raw ?? "";
  let hallucinationRisk = false;
  let factual: number | null = null;
  let pedagogy: number | null = null;
  let context: number | null = null;

  if (text.startsWith("[HALL]")) {
    hallucinationRisk = true;
    text = text.slice("[HALL]".length).trim();
  }

  const scoreMatch = text.match(/^\[F:(\d)\s+P:(\d)\s+C:(\d)\]\s*/);
  if (scoreMatch) {
    factual   = parseInt(scoreMatch[1], 10);
    pedagogy  = parseInt(scoreMatch[2], 10);
    context   = parseInt(scoreMatch[3], 10);
    text      = text.slice(scoreMatch[0].length);
  }

  return { hallucinationRisk, factual, pedagogy, context, text };
}

// Sistemin failover sağlığı: Primary (GitHub) yüzdesine göre
function failoverHealth(mix: ProviderUsage[]): { label: string; color: string; dot: string } {
  const primary = mix.find(m => m.provider === "GitHub");
  const pct = primary?.percentage ?? 0;
  if (mix.length === 0)  return { label: "Veri yok",            color: "text-zinc-500",    dot: "bg-zinc-700" };
  if (pct >= 85)          return { label: "Primary sağlıklı",    color: "text-emerald-400", dot: "bg-emerald-500 animate-pulse" };
  if (pct >= 60)          return { label: "Failover aktif",      color: "text-amber-400",   dot: "bg-amber-400" };
  return                         { label: "Primary bozuk",        color: "text-amber-300",   dot: "bg-amber-300 animate-ping" };
}

// ──────────────────────────────────────────────
// Helpers
// ──────────────────────────────────────────────
const AGENT_EMOJIS: Record<string, string> = {
  Tutor: "🧑‍🏫",
  Evaluator: "⚖️",
  Supervisor: "🕵️",
  Summarizer: "📝",
  Korteks: "🧠",
  Grader: "✅",
  DeepPlan: "🗺️",
};

function latencyColor(ms: number) {
  if (ms === 0) return "text-zinc-600";
  if (ms < 1500) return "text-emerald-400";
  if (ms < 4000) return "text-amber-400";
  return "text-amber-300"; // kritik: daha açık amber ile öne çıkar
}

function statusBadge(status: AgentStat["status"]) {
  switch (status) {
    case "online":
      return (
        <span className="flex items-center gap-1 text-[10px] font-bold text-emerald-400 uppercase tracking-widest">
          <span className="w-1.5 h-1.5 rounded-full bg-emerald-400 animate-pulse" />
          Online
        </span>
      );
    case "degraded":
      return (
        <span className="flex items-center gap-1 text-[10px] font-bold text-amber-400 uppercase tracking-widest">
          <span className="w-1.5 h-1.5 rounded-full bg-amber-400" />
          Bozuk
        </span>
      );
    case "critical":
      return (
        <span className="flex items-center gap-1 text-[10px] font-bold text-amber-300 uppercase tracking-widest">
          <span className="w-1.5 h-1.5 rounded-full bg-amber-300 animate-ping" />
          Kritik
        </span>
      );
    default:
      return (
        <span className="flex items-center gap-1 text-[10px] font-bold text-zinc-600 uppercase tracking-widest">
          <span className="w-1.5 h-1.5 rounded-full bg-zinc-700" />
          Boşta
        </span>
      );
  }
}

function qualityStyle(q: EvaluatorLog["quality"]) {
  switch (q) {
    case "gold": return "border-l-amber-400 bg-amber-500/5";
    case "good": return "border-l-emerald-500 bg-emerald-500/5";
    case "ok":   return "border-l-zinc-500 bg-zinc-800/30";
    default:     return "border-l-amber-300 bg-amber-500/10";
  }
}

function qualityLabel(q: EvaluatorLog["quality"]) {
  switch (q) {
    case "gold": return { label: "Altın", color: "text-amber-400" };
    case "good": return { label: "İyi", color: "text-emerald-400" };
    case "ok":   return { label: "Orta", color: "text-zinc-400" };
    default:     return { label: "Uyarı", color: "text-amber-300" };
  }
}

// ──────────────────────────────────────────────
// Miniature Bar Chart for Evaluator Average
// ──────────────────────────────────────────────
function ScoreGauge({ score, max = 10 }: { score: number; max?: number }) {
  const pct = Math.min((score / max) * 100, 100);
  // Palet: amber (altın) → emerald (iyi) → zinc (orta) → amber-light (düşük)
  const color = pct >= 90 ? "#f59e0b" : pct >= 70 ? "#10b981" : pct >= 50 ? "#71717a" : "#fcd34d";
  return (
    <div className="relative w-full h-2 bg-zinc-800 rounded-full overflow-hidden">
      <div
        className="h-full rounded-full transition-all duration-1000"
        style={{ width: `${pct}%`, backgroundColor: color }}
      />
    </div>
  );
}

// ──────────────────────────────────────────────
// Main Component
// ──────────────────────────────────────────────
export default function SystemHealthHUD() {
  const [data, setData] = useState<SystemHealth | null>(null);
  const [loading, setLoading] = useState(true);
  const [lastRefresh, setLastRefresh] = useState<Date>(new Date());
  const [refreshing, setRefreshing] = useState(false);

  const load = useCallback(async () => {
    try {
      setRefreshing(true);
      const res = await DashboardAPI.getSystemHealth();
      setData(res.data as SystemHealth);
      setLastRefresh(new Date());
    } catch (err) {
      console.error("System Health fetch error:", err);
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, []);

  useEffect(() => {
    load();
    // Otomatik 30 saniyes'te bir yenile
    const timer = setInterval(load, 30_000);
    return () => clearInterval(timer);
  }, [load]);

  if (loading) {
    return (
      <div className="flex-1 flex items-center justify-center bg-[#0a0a0a]">
        <div className="flex flex-col items-center gap-3">
          <div className="w-6 h-6 border-2 border-emerald-500/30 border-t-emerald-500 rounded-full animate-spin" />
          <p className="text-[11px] text-zinc-600 uppercase tracking-widest">Sistem verisi yükleniyor...</p>
        </div>
      </div>
    );
  }

  if (!data) {
    return (
      <div className="flex-1 flex items-center justify-center bg-[#0a0a0a]">
        <div className="flex flex-col items-center gap-3 text-center">
          <WifiOff className="w-8 h-8 text-zinc-700" />
          <p className="text-sm text-zinc-500">Sistem verisi alınamadı.</p>
          <button onClick={load} className="text-[11px] text-emerald-500 hover:text-emerald-400 uppercase tracking-widest">
            Tekrar Dene
          </button>
        </div>
      </div>
    );
  }

  const onlineAgents = data.agents.filter(a => a.status === "online").length;
  const totalAgents  = data.agents.length;

  return (
    <div className="flex-1 flex flex-col bg-[#0a0a0a] h-full overflow-hidden">
      <div className="flex-1 overflow-y-auto">
        <div className="max-w-5xl mx-auto w-full px-8 py-10 space-y-8">

          {/* Header */}
          <div className="flex items-center justify-between">
            <div>
              <h1 className="text-2xl font-bold text-zinc-100 mb-1.5 tracking-tight">
                Sistem Analitiği
              </h1>
              <div className="flex items-center gap-2">
                <span className="flex h-2 w-2 rounded-full bg-emerald-500 animate-pulse" />
                <p className="text-[11px] font-medium text-zinc-500 uppercase tracking-widest">
                  LLMOps İzleme Paneli · Gerçek Zamanlı Redis Telemetrisi
                </p>
              </div>
            </div>

            <div className="flex items-center gap-4">
              <p className="text-[10px] text-zinc-600">
                Son güncelleme: {lastRefresh.toLocaleTimeString("tr-TR")}
              </p>
              <button
                onClick={load}
                disabled={refreshing}
                className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[11px] text-zinc-400 hover:text-zinc-200 border border-zinc-800 hover:border-zinc-600 transition-all"
              >
                <RefreshCw className={`w-3 h-3 ${refreshing ? "animate-spin" : ""}`} />
                Yenile
              </button>
            </div>
          </div>

          {/* Top KPI Row */}
          <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
            {/* Ajan Sağlığı */}
            <div className="relative p-5 rounded-2xl bg-zinc-900/50 border border-zinc-800/50 overflow-hidden group hover:border-emerald-500/20 transition-colors">
              <Cpu className="w-4 h-4 text-emerald-500/60 mb-4" />
              <p className="text-3xl font-bold text-zinc-100 font-mono">
                {onlineAgents}
                <span className="text-zinc-600 text-lg">/{totalAgents}</span>
              </p>
              <p className="text-[11px] font-medium text-zinc-500 uppercase mt-1">Ajan Online</p>
            </div>

            {/* LLMOps Ortalama Puan */}
            <div className="relative p-5 rounded-2xl bg-zinc-900/50 border border-zinc-800/50 overflow-hidden group hover:border-amber-500/20 transition-colors">
              <Award className="w-4 h-4 text-amber-500/60 mb-4" />
              <p className="text-3xl font-bold text-amber-300 font-mono">
                {data.llmops.avgEvaluatorScore > 0 ? data.llmops.avgEvaluatorScore : "—"}
                <span className="text-zinc-600 text-sm">/10</span>
              </p>
              <p className="text-[11px] font-medium text-zinc-500 uppercase mt-1">Ort. Öğretim Puanı</p>
              {data.llmops.avgEvaluatorScore > 0 && (
                <div className="mt-3">
                  <ScoreGauge score={data.llmops.avgEvaluatorScore} />
                </div>
              )}
            </div>

            {/* Token */}
            <div className="relative p-5 rounded-2xl bg-zinc-900/50 border border-zinc-800/50 overflow-hidden group hover:border-zinc-700/60 transition-colors">
              <Database className="w-4 h-4 text-zinc-500 mb-4" />
              <p className="text-3xl font-bold text-zinc-100 font-mono">
                {data.tokens.total > 1000
                  ? `${(data.tokens.total / 1000).toFixed(1)}K`
                  : data.tokens.total}
              </p>
              <p className="text-[11px] font-medium text-zinc-500 uppercase mt-1">Toplam Token</p>
            </div>

            {/* Maliyet */}
            <div className="relative p-5 rounded-2xl bg-zinc-900/50 border border-zinc-800/50 overflow-hidden group hover:border-zinc-700/60 transition-colors">
              <DollarSign className="w-4 h-4 text-zinc-500 mb-4" />
              <p className="text-3xl font-bold text-zinc-100 font-mono">
                ${data.tokens.costUSD.toFixed(4)}
              </p>
              <p className="text-[11px] font-medium text-zinc-500 uppercase mt-1">Toplam Maliyet</p>
            </div>
          </div>

          {/* Model Mix — LLM Provider Dağılımı */}
          {data.modelMix && data.modelMix.length > 0 && (() => {
            const sorted = [...data.modelMix].sort((a, b) => providerMeta(a.provider).rank - providerMeta(b.provider).rank);
            const health = failoverHealth(data.modelMix);
            const totalCalls = sorted.reduce((acc, m) => acc + m.callCount, 0);
            return (
              <div>
                <div className="flex items-center justify-between mb-4">
                  <h2 className="text-[11px] font-bold text-zinc-500 uppercase tracking-widest flex items-center gap-2">
                    <Layers className="w-3.5 h-3.5" />
                    Model Dağılımı · Failover Sağlığı
                  </h2>
                  <div className="flex items-center gap-1.5">
                    <span className={`w-1.5 h-1.5 rounded-full ${health.dot}`} />
                    <span className={`text-[10px] font-bold uppercase tracking-widest ${health.color}`}>
                      {health.label}
                    </span>
                  </div>
                </div>

                <div className="rounded-2xl bg-zinc-900/40 border border-zinc-800/40 p-5 space-y-3">
                  {sorted.map((m) => {
                    const meta = providerMeta(m.provider);
                    const pct = m.percentage;
                    const isPrimary = m.provider === "GitHub";
                    const barColor = isPrimary
                      ? "bg-emerald-500/70"
                      : m.provider === "Groq"
                        ? "bg-amber-400/60"
                        : m.provider === "Gemini"
                          ? "bg-zinc-500/70"
                          : "bg-zinc-600/50";
                    const errorRate = m.callCount > 0 ? (m.errorCount / m.callCount) * 100 : 0;
                    return (
                      <div key={m.provider} className="space-y-1.5">
                        <div className="flex items-center justify-between text-[11px]">
                          <span className="font-medium text-zinc-300">{meta.label}</span>
                          <span className="font-mono text-zinc-500">
                            <span className="text-zinc-200">{pct.toFixed(1)}%</span>
                            <span className="mx-2 text-zinc-700">·</span>
                            <span>{m.callCount} çağrı</span>
                            <span className="mx-2 text-zinc-700">·</span>
                            <span className={latencyColor(m.avgLatencyMs)}>{Math.round(m.avgLatencyMs)}ms</span>
                            {errorRate > 0 && (
                              <>
                                <span className="mx-2 text-zinc-700">·</span>
                                <span className="text-amber-300">{errorRate.toFixed(0)}% hata</span>
                              </>
                            )}
                          </span>
                        </div>
                        <div className="relative w-full h-1.5 bg-zinc-800/60 rounded-full overflow-hidden">
                          <div
                            className={`h-full ${barColor} rounded-full transition-all duration-1000`}
                            style={{ width: `${Math.max(pct, 0.5)}%` }}
                          />
                        </div>
                      </div>
                    );
                  })}

                  <div className="pt-2 mt-2 border-t border-zinc-800/60 flex items-center justify-between text-[10px] text-zinc-600">
                    <span>Toplam {totalCalls} ajan çağrısı — Son 24 saat</span>
                    <span>
                      Primary hedef ≥ 85% · Aktif: {(data.modelMix.find(m => m.provider === "GitHub")?.percentage ?? 0).toFixed(1)}%
                    </span>
                  </div>
                </div>
              </div>
            );
          })()}

          {/* Ajan Monitörü */}
          <div>
            <h2 className="text-[11px] font-bold text-zinc-500 uppercase tracking-widest flex items-center gap-2 mb-4">
              <Activity className="w-3.5 h-3.5" />
              Ajan Kalp Atış Monitörü
            </h2>

            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-3">
              {data.agents.map((agent) => (
                <div
                  key={agent.agentRole}
                  className={`
                    relative p-4 rounded-2xl border transition-all
                    ${agent.status === "online" ? "bg-zinc-900/40 border-zinc-800/40 hover:border-emerald-500/20" : ""}
                    ${agent.status === "degraded" ? "bg-amber-500/5 border-amber-500/20" : ""}
                    ${agent.status === "critical" ? "bg-amber-500/10 border-amber-500/40" : ""}
                    ${agent.status === "idle" ? "bg-zinc-900/20 border-zinc-800/20 opacity-60" : ""}
                  `}
                >
                  {/* Ajan başlık */}
                  <div className="flex items-start justify-between mb-3">
                    <div className="flex items-center gap-2">
                      <span className="text-base">{AGENT_EMOJIS[agent.agentRole] ?? "🤖"}</span>
                      <div>
                        <p className="text-xs font-bold text-zinc-200">{agent.agentRole}</p>
                        <p className="text-[10px] text-zinc-600">{agent.lastProvider}</p>
                      </div>
                    </div>
                    {statusBadge(agent.status)}
                  </div>

                  {/* Metrikler */}
                  <div className="space-y-1.5">
                    <div className="flex items-center justify-between">
                      <span className="text-[10px] text-zinc-600 flex items-center gap-1">
                        <Clock className="w-3 h-3" /> TTFT
                      </span>
                      <span className={`text-[11px] font-mono font-bold ${latencyColor(agent.avgLatencyMs)}`}>
                        {agent.avgLatencyMs > 0 ? `${agent.avgLatencyMs}ms` : "—"}
                      </span>
                    </div>

                    {agent.avgQualityScore > 0 && (
                      <div className="flex items-center justify-between">
                        <span className="text-[10px] text-zinc-600 flex items-center gap-1">
                          <Award className="w-3 h-3" /> Kalite
                        </span>
                        <span className={`text-[11px] font-mono font-bold ${
                          agent.avgQualityScore >= 8 ? "text-amber-400"
                          : agent.avgQualityScore >= 6 ? "text-emerald-400"
                          : "text-amber-300"
                        }`}>
                          {agent.avgQualityScore}/10
                          {agent.goldCount > 0 && (
                            <span className="ml-1 text-amber-400/60">🏆{agent.goldCount}</span>
                          )}
                        </span>
                      </div>
                    )}

                    <div className="flex items-center justify-between">
                      <span className="text-[10px] text-zinc-600 flex items-center gap-1">
                        <TrendingUp className="w-3 h-3" /> Çağrı
                      </span>
                      <span className="text-[11px] font-mono text-zinc-300">
                        {agent.totalCalls > 0 ? agent.totalCalls : agent.totalEvals > 0 ? `—` : "—"}
                        {agent.totalEvals > 0 && (
                          <span className="text-zinc-600 ml-1">({agent.totalEvals} eval)</span>
                        )}
                      </span>
                    </div>

                    {agent.errorCount > 0 && (
                      <div className="flex items-center justify-between">
                        <span className="text-[10px] text-amber-300 flex items-center gap-1">
                          <AlertTriangle className="w-3 h-3" /> Hata
                        </span>
                        <span className="text-[11px] font-mono text-amber-300">
                          {agent.errorCount} ({agent.errorRatePct}%)
                        </span>
                      </div>
                    )}
                  </div>

                  {/* Latency bar */}
                  {agent.avgLatencyMs > 0 && (
                    <div className="mt-3 w-full h-0.5 bg-zinc-800 rounded-full overflow-hidden">
                      <div
                        className="h-full rounded-full transition-all duration-1000"
                        style={{
                          width: `${Math.min((agent.avgLatencyMs / 5000) * 100, 100)}%`,
                          backgroundColor:
                            agent.avgLatencyMs < 1500 ? "#10b981"
                            : agent.avgLatencyMs < 4000 ? "#f59e0b"
                            : "#fcd34d",
                        }}
                      />
                    </div>
                  )}
                </div>
              ))}
            </div>
          </div>

          {/* Alt İkili Grid: LLMOps Log + Pedagoji */}
          <div className="grid grid-cols-1 lg:grid-cols-5 gap-6">

            {/* LLMOps Log Paneli */}
            <div className="lg:col-span-3">
              <h2 className="text-[11px] font-bold text-zinc-500 uppercase tracking-widest flex items-center gap-2 mb-4">
                <MessageSquare className="w-3.5 h-3.5" />
                Evaluator LLMOps Kayıtları
                {data.llmops.recentLogs.length === 0 && (
                  <span className="text-zinc-700 normal-case font-normal">(Henüz kurs konuşmaları başlatılmadı)</span>
                )}
              </h2>

              <div className="space-y-2 max-h-80 overflow-y-auto pr-1 scrollbar-thin scrollbar-thumb-zinc-800">
                {data.llmops.recentLogs.length === 0 ? (
                  <div className="py-10 text-center border border-dashed border-zinc-800/60 rounded-2xl">
                    <p className="text-xs text-zinc-600">
                      Henüz değerlendirme yok. Bir konuyla sohbet başlatın.
                    </p>
                  </div>
                ) : (
                  data.llmops.recentLogs.map((log, i) => {
                    const q = qualityLabel(log.quality);
                    const parsed = parseEvaluatorFeedback(log.feedback);
                    const hasSubScores = parsed.factual !== null;
                    return (
                      <div
                        key={i}
                        className={`flex gap-3 p-3 rounded-xl border-l-2 ${
                          parsed.hallucinationRisk
                            ? "border-l-amber-300 bg-amber-500/15"
                            : qualityStyle(log.quality)
                        }`}
                      >
                        <div className="flex-shrink-0 flex flex-col items-center gap-1">
                          <span className={`text-sm font-mono font-bold ${q.color}`}>
                            {log.score}
                          </span>
                          <span className={`text-[9px] font-bold uppercase ${q.color}`}>
                            {q.label}
                          </span>
                        </div>
                        <div className="flex-1 min-w-0">
                          <div className="flex items-center gap-2 mb-1 flex-wrap">
                            {log.agentRole && (
                              <span className="text-[10px] text-zinc-600">
                                {AGENT_EMOJIS[log.agentRole] ?? "🤖"} {log.agentRole}
                              </span>
                            )}
                            {parsed.hallucinationRisk && (
                              <span className="text-[9px] font-bold uppercase tracking-wider text-amber-300 bg-amber-500/15 border border-amber-500/40 px-1.5 py-0.5 rounded">
                                Halüsinasyon Riski
                              </span>
                            )}
                            {hasSubScores && (
                              <span className="inline-flex items-center gap-1.5 text-[9px] font-mono text-zinc-500">
                                <span title="Faktual (doğruluk)" className={parsed.factual! <= 2 ? "text-amber-300" : parsed.factual! >= 4 ? "text-emerald-400" : "text-zinc-400"}>
                                  F·{parsed.factual}
                                </span>
                                <span title="Pedagoji (öğreticilik)" className={parsed.pedagogy! <= 2 ? "text-amber-300" : parsed.pedagogy! >= 4 ? "text-emerald-400" : "text-zinc-400"}>
                                  P·{parsed.pedagogy}
                                </span>
                                <span title="Bağlam (konuyla alaka)" className={parsed.context! <= 2 ? "text-amber-300" : parsed.context! >= 4 ? "text-emerald-400" : "text-zinc-400"}>
                                  C·{parsed.context}
                                </span>
                              </span>
                            )}
                          </div>
                          <p className="text-[11px] text-zinc-400 leading-relaxed line-clamp-2">
                            {parsed.text || "Geri bildirim yok."}
                          </p>
                          <p className="text-[10px] text-zinc-700 mt-1">{log.recordedAt}</p>
                        </div>
                        {parsed.hallucinationRisk ? (
                          <AlertTriangle className="w-4 h-4 text-amber-300 flex-shrink-0 mt-0.5 animate-pulse" />
                        ) : log.quality === "gold" ? (
                          <CheckCircle2 className="w-4 h-4 text-amber-400 flex-shrink-0 mt-0.5" />
                        ) : log.quality === "warn" ? (
                          <AlertTriangle className="w-4 h-4 text-amber-300 flex-shrink-0 mt-0.5" />
                        ) : null}
                      </div>
                    );
                  })
                )}
              </div>
            </div>

            {/* Pedagoji & Oturum */}
            <div className="lg:col-span-2 space-y-4">
              <h2 className="text-[11px] font-bold text-zinc-500 uppercase tracking-widest flex items-center gap-2">
                <Zap className="w-3.5 h-3.5" />
                Öğrenme Profili
              </h2>

              {/* Pedagoji Skoru */}
              <div className="p-5 rounded-2xl bg-zinc-900/40 border border-zinc-800/40 space-y-3">
                <div className="flex items-center justify-between">
                  <span className="text-xs text-zinc-400">Algoritma Başarı Skoru</span>
                  <span className="text-lg font-mono font-bold text-emerald-300">
                    {data.pedagogy.score > 0 ? `%${data.pedagogy.score}` : "—"}
                  </span>
                </div>
                <ScoreGauge score={data.pedagogy.score} max={100} />
                <p className="text-[10px] text-zinc-600">
                  {data.pedagogy.masteredTopics} alt konu öğrenildi
                </p>
              </div>

              {/* Oturum Bilgisi */}
              <div className="p-5 rounded-2xl bg-zinc-900/40 border border-zinc-800/40 space-y-2">
                <p className="text-[11px] text-zinc-500 uppercase font-bold tracking-widest">Oturumlar</p>
                <p className="text-3xl font-mono font-bold text-zinc-200">{data.sessions.total}</p>
                {data.sessions.lastDate && (
                  <p className="text-[10px] text-zinc-600">
                    Son: {new Date(data.sessions.lastDate).toLocaleDateString("tr-TR", {
                      day: "2-digit",
                      month: "long",
                      year: "numeric",
                    })}
                  </p>
                )}
              </div>

              {/* Altın Örnek Sayısı */}
              <div className="p-4 rounded-2xl bg-amber-500/5 border border-amber-500/15 flex items-center gap-3">
                <span className="text-2xl">🏆</span>
                <div>
                  <p className="text-xs font-bold text-amber-300">
                    {data.llmops.recentLogs.filter(l => l.quality === "gold").length} Altın Örnek
                  </p>
                  <p className="text-[10px] text-zinc-600">
                    Son 20 kayıtta 9-10 puan
                  </p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
