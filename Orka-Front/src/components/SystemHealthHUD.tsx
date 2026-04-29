import { useCallback, useEffect, useMemo, useState } from "react";
import type { ReactNode } from "react";
import {
  Activity,
  AlertTriangle,
  BarChart3,
  Brain,
  CheckCircle2,
  Clock,
  Cpu,
  Database,
  DollarSign,
  Gauge,
  Layers,
  RefreshCw,
  RotateCcw,
  Server,
  Sparkles,
  Target,
  WifiOff,
  Zap,
} from "lucide-react";
import { DashboardAPI } from "@/services/api";

interface AgentStat {
  agentRole: string;
  avgLatencyMs: number;
  totalCalls: number;
  errorCount: number;
  errorRatePct: number;
  lastProvider: string;
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

interface RedisHealth {
  isConnected: boolean;
  pingMs: number;
  endpointCount: number;
  status: string;
  lastError?: string | null;
  checkedAt: string;
}

interface CacheMetric {
  area: string;
  tool: string;
  hitCount: number;
  missCount: number;
  hitRatePct: number;
  avgLatencyMs: number;
  lastEventAt: string;
}

interface LearningOps {
  windowDays: number;
  totalSignals: number;
  signalCounts: Array<{ signalType: string; count: number }>;
  topWeakSkills: Array<{ skillTag: string; count: number }>;
  quizAttempts: number;
  quizAccuracyPct: number;
  unknownSkillRatePct: number;
  repeatedQuestionRatePct: number;
  remediationCompletionRatePct: number;
  learningBridge?: {
    healthy: number;
    watch: number;
    idle: number;
    bridges: Array<{
      key: string;
      label: string;
      status: "healthy" | "watch" | "idle";
      detail: string;
      signals: Array<{ signalType: string; count: number }>;
    }>;
  };
}

interface EndpointContract {
  method: string;
  path: string;
  status: string;
}

interface EndpointHealth {
  apiBaseUrl: string;
  swagger: { path: string; json: string; enabled: boolean; status: string };
  health: {
    live: string;
    ready: string;
    database: { canConnect: boolean; error?: string | null };
    redis: RedisHealth;
  };
  auth: EndpointContract[];
  core: EndpointContract[];
}

interface SystemHealth {
  tokens: { total: number; costUSD: number };
  pedagogy: { score: number; masteredTopics: number };
  sessions: { total: number; lastDate: string | null };
  llmops: { avgEvaluatorScore: number; totalEvaluations: number; recentLogs: EvaluatorLog[] };
  agents: AgentStat[];
  modelMix: ProviderUsage[];
  redis: RedisHealth;
  cache: { metrics: CacheMetric[]; totalHits: number; totalMisses: number; hitRatePct: number };
  notebookCache: { tools: CacheMetric[]; invalidations: CacheMetric[]; hitRatePct: number };
  learningOps: LearningOps;
  endpointHealth?: EndpointHealth;
}

const PROVIDER_LABELS: Record<string, { label: string; rank: number; color: string }> = {
  GitHub: { label: "GitHub Primary", rank: 1, color: "bg-emerald-400" },
  GitHubModels: { label: "GitHub Models", rank: 1, color: "bg-emerald-400" },
  Groq: { label: "Groq Fallback", rank: 2, color: "bg-amber-300" },
  Gemini: { label: "Gemini Fallback", rank: 3, color: "bg-sky-300" },
  OpenRouter: { label: "OpenRouter", rank: 4, color: "bg-fuchsia-300" },
  Cerebras: { label: "Cerebras", rank: 5, color: "bg-orange-300" },
  SambaNova: { label: "SambaNova", rank: 6, color: "bg-teal-300" },
  Mistral: { label: "Mistral Final", rank: 7, color: "bg-rose-300" },
  Unknown: { label: "Bilinmeyen", rank: 99, color: "bg-zinc-500" },
};

const AGENT_LABELS: Record<string, string> = {
  Tutor: "Tutor",
  Analyzer: "Analyzer",
  Evaluator: "Evaluator",
  Supervisor: "Supervisor",
  Summarizer: "Wiki",
  Korteks: "Korteks",
  Grader: "Grader",
  DeepPlan: "DeepPlan",
  IntentClassifier: "Intent",
  TieredPlanner: "Planner",
  Quiz: "Quiz",
  Diagnostic: "Diagnostic",
  Remedial: "Remedial",
  Visual: "Visual",
  Classroom: "Classroom",
};

function providerMeta(provider: string) {
  return PROVIDER_LABELS[provider] ?? { label: provider, rank: 50, color: "bg-zinc-400" };
}

function latencyClass(ms: number) {
  if (ms === 0) return "text-[#98a2b3]";
  if (ms < 1500) return "text-[#47725d]";
  if (ms < 4000) return "text-amber-300";
  return "text-red-300";
}

function healthTone(value: number, warn: number, critical: number) {
  if (value >= critical) return "text-red-300 border-red-400/25 bg-red-500/10";
  if (value >= warn) return "text-amber-300 border-amber-400/25 bg-amber-500/10";
  return "text-[#47725d] border-emerald-400/20 bg-emerald-500/10";
}

function statusPill(status: AgentStat["status"]) {
  const map = {
    online: "bg-emerald-400/15 text-[#47725d] border-emerald-400/20",
    degraded: "bg-amber-400/15 text-amber-300 border-amber-400/20",
    critical: "bg-red-400/15 text-red-300 border-red-400/20",
    idle: "bg-[#dcecf3]/70/70 text-[#667085] border-[#526d82]/18/50",
  } satisfies Record<AgentStat["status"], string>;

  return <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold uppercase tracking-wider ${map[status]}`}>{status}</span>;
}

function bridgeStatusClass(status: "healthy" | "watch" | "idle") {
  const map = {
    healthy: "border-emerald-400/20 bg-emerald-500/10 text-[#47725d]",
    watch: "border-amber-400/25 bg-amber-500/10 text-amber-200",
    idle: "border-[#526d82]/15 bg-white/62 text-[#667085]",
  } satisfies Record<"healthy" | "watch" | "idle", string>;

  return map[status];
}

function formatNumber(value: number) {
  if (value >= 1_000_000) return `${(value / 1_000_000).toFixed(1)}M`;
  if (value >= 1000) return `${(value / 1000).toFixed(1)}K`;
  return value.toString();
}

function MiniBar({ value, color = "bg-emerald-400" }: { value: number; color?: string }) {
  return (
    <div className="h-1.5 w-full overflow-hidden rounded-full bg-[#dcecf3]/75">
      <div className={`h-full rounded-full transition-all duration-700 ${color}`} style={{ width: `${Math.min(Math.max(value, 0), 100)}%` }} />
    </div>
  );
}

function Panel({ title, icon: Icon, children, right }: { title: string; icon: typeof Activity; children: ReactNode; right?: ReactNode }) {
  return (
    <section className="rounded-3xl border border-[#526d82]/15 bg-white/66 p-5 shadow-[0_18px_70px_rgba(0,0,0,0.28)] backdrop-blur">
      <div className="mb-4 flex items-center justify-between gap-3">
        <h2 className="flex items-center gap-2 text-[11px] font-black uppercase tracking-[0.22em] text-[#667085]">
          <Icon className="h-4 w-4" />
          {title}
        </h2>
        {right}
      </div>
      {children}
    </section>
  );
}

function MetricCard({ icon: Icon, label, value, hint, tone = "zinc" }: { icon: typeof Activity; label: string; value: ReactNode; hint?: string; tone?: "zinc" | "emerald" | "amber" | "red" | "sky" }) {
  const tones = {
    zinc: "border-[#526d82]/15 bg-white/64 text-[#344054]",
    emerald: "border-emerald-400/20 bg-emerald-500/10 text-[#47725d]",
    amber: "border-amber-400/20 bg-amber-500/10 text-amber-200",
    red: "border-red-400/20 bg-red-500/10 text-red-200",
    sky: "border-sky-400/20 bg-sky-500/10 text-sky-200",
  };

  return (
    <div className={`rounded-2xl border p-4 ${tones[tone]}`}>
      <Icon className="mb-3 h-4 w-4 opacity-70" />
      <div className="text-2xl font-black tracking-tight text-zinc-50">{value}</div>
      <div className="mt-1 text-[10px] font-bold uppercase tracking-widest text-[#667085]">{label}</div>
      {hint && <p className="mt-2 text-[11px] leading-relaxed text-[#667085]">{hint}</p>}
    </div>
  );
}

export default function SystemHealthHUD() {
  const [data, setData] = useState<SystemHealth | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [lastRefresh, setLastRefresh] = useState<Date>(new Date());

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
    const timer = window.setInterval(load, 30_000);
    return () => window.clearInterval(timer);
  }, [load]);

  const actionItems = useMemo(() => {
    if (!data) return [];
    const items: Array<{ title: string; detail: string; tone: "emerald" | "amber" | "red"; icon: typeof Activity }> = [];
    const criticalAgents = data.agents.filter((a) => a.status === "critical").length;
    const degradedAgents = data.agents.filter((a) => a.status === "degraded").length;

    if (!data.redis.isConnected) {
      items.push({ title: "Redis offline", detail: data.redis.lastError ?? "Cache ve realtime metrikler fallback modunda.", tone: "red", icon: WifiOff });
    }
    if (data.endpointHealth && !data.endpointHealth.health.database.canConnect) {
      items.push({ title: "DB readiness", detail: data.endpointHealth.health.database.error ?? "SQL baglantisi hazir degil; auth ve user endpointleri etkilenir.", tone: "red", icon: Database });
    }
    if (data.endpointHealth && !data.endpointHealth.swagger.enabled) {
      items.push({ title: "Swagger kapali", detail: "Swagger yalnizca Development ortaminda acik.", tone: "amber", icon: Server });
    }
    if (criticalAgents > 0 || degradedAgents > 0) {
      items.push({ title: "Ajan sagligi", detail: `${criticalAgents} kritik, ${degradedAgents} degraded ajan var.`, tone: criticalAgents > 0 ? "red" : "amber", icon: Cpu });
    }
    if (data.learningOps.unknownSkillRatePct >= 20) {
      items.push({ title: "Skill metadata zayif", detail: `Quiz cevaplarinin %${data.learningOps.unknownSkillRatePct} kadari skill etiketsiz.`, tone: "amber", icon: Target });
    }
    if (data.learningOps.repeatedQuestionRatePct >= 10) {
      items.push({ title: "Quiz tekrar riski", detail: `Soru hash tekrar orani %${data.learningOps.repeatedQuestionRatePct}.`, tone: "amber", icon: RotateCcw });
    }
    if (data.cache.hitRatePct > 0 && data.cache.hitRatePct < 35) {
      items.push({ title: "Cache isabeti dusuk", detail: `Genel hit rate %${data.cache.hitRatePct}; invalidation fazla agresif olabilir.`, tone: "amber", icon: Database });
    }
    if (items.length === 0) {
      items.push({ title: "Sistem stabil", detail: "Redis, agent ve learning sinyalleri normal aralikta gorunuyor.", tone: "emerald", icon: CheckCircle2 });
    }
    return items;
  }, [data]);

  if (loading) {
    return (
      <div className="flex h-full flex-1 items-center justify-center bg-transparent">
        <div className="flex flex-col items-center gap-3">
          <div className="h-7 w-7 animate-spin rounded-full border-2 border-emerald-500/20 border-t-emerald-300" />
          <p className="text-[11px] font-bold uppercase tracking-[0.24em] text-[#98a2b3]">Sistem okunuyor</p>
        </div>
      </div>
    );
  }

  if (!data) {
    return (
      <div className="flex h-full flex-1 items-center justify-center bg-transparent">
        <div className="flex flex-col items-center gap-3 text-center">
          <WifiOff className="h-9 w-9 text-zinc-700" />
          <p className="text-sm text-[#667085]">Sistem verisi alinamadi.</p>
          <button onClick={load} className="rounded-full border border-[#526d82]/15 px-4 py-2 text-[11px] font-bold uppercase tracking-widest text-[#47725d] hover:border-[#8fb7a2]/42">
            Tekrar dene
          </button>
        </div>
      </div>
    );
  }

  const onlineAgents = data.agents.filter((a) => a.status === "online").length;
  const sortedProviders = [...data.modelMix].sort((a, b) => providerMeta(a.provider).rank - providerMeta(b.provider).rank);
  const redisTone = data.redis.isConnected ? "emerald" : "red";
  const endpointHealth = data.endpointHealth;

  return (
    <div className="h-full flex-1 overflow-hidden bg-transparent text-[#172033]">
      <div className="h-full overflow-y-auto">
        <div className="mx-auto flex w-full max-w-6xl flex-col gap-6 px-5 py-8 lg:px-8 lg:py-10">
          <header className="flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
            <div>
              <div className="mb-3 inline-flex items-center gap-2 rounded-full border border-emerald-400/15 bg-emerald-500/10 px-3 py-1 text-[10px] font-black uppercase tracking-[0.24em] text-[#47725d]">
                <Sparkles className="h-3.5 w-3.5" />
                Full Stabilizasyon HUD
              </div>
              <h1 className="text-3xl font-black tracking-tight text-zinc-50">Sistem cockpit</h1>
              <p className="mt-2 max-w-2xl text-sm leading-6 text-[#667085]">
                Redis cache, NotebookLM araclari, agent kalp atislari ve ogrenme sinyalleri tek yerde. Amac ham veri degil, neye mudahale edecegimizi hizli gostermek.
              </p>
            </div>
            <div className="flex items-center gap-3">
              <span className="text-[10px] font-bold uppercase tracking-widest text-[#98a2b3]">Son yenileme {lastRefresh.toLocaleTimeString("tr-TR")}</span>
              <button
                onClick={load}
                disabled={refreshing}
                className="inline-flex items-center gap-2 rounded-full border border-[#526d82]/15 bg-white/70 px-4 py-2 text-[11px] font-bold uppercase tracking-widest text-[#344054] transition hover:border-[#8fb7a2]/42 hover:text-[#47725d] disabled:opacity-50"
              >
                <RefreshCw className={`h-3.5 w-3.5 ${refreshing ? "animate-spin" : ""}`} />
                Yenile
              </button>
            </div>
          </header>

          <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
            {actionItems.map((item) => (
              <MetricCard key={item.title} icon={item.icon} label={item.title} value={item.tone === "emerald" ? "OK" : "Bak"} hint={item.detail} tone={item.tone} />
            ))}
          </div>

          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <MetricCard icon={Server} label="Redis" value={data.redis.status} hint={`${data.redis.pingMs}ms ping · ${data.redis.endpointCount} endpoint`} tone={redisTone} />
            <MetricCard icon={CheckCircle2} label="Auth/Swagger" value={endpointHealth?.swagger.enabled ? "Hazir" : "Dev only"} hint={endpointHealth ? `${endpointHealth.apiBaseUrl} · ${endpointHealth.swagger.json}` : "Endpoint kontrati bekleniyor"} tone={endpointHealth?.health.database.canConnect === false ? "red" : "emerald"} />
            <MetricCard icon={Cpu} label="Ajan online" value={`${onlineAgents}/${data.agents.length}`} hint="Yeni Quiz, Diagnostic, Remedial, Visual ve Classroom rolleri dahil." tone="emerald" />
            <MetricCard icon={Database} label="Cache hit rate" value={`%${data.cache.hitRatePct}`} hint={`${data.cache.totalHits} hit · ${data.cache.totalMisses} miss`} tone={data.cache.hitRatePct >= 50 ? "emerald" : data.cache.hitRatePct > 0 ? "amber" : "zinc"} />
            <MetricCard icon={Brain} label="Learning signal" value={data.learningOps.totalSignals} hint={`Son ${data.learningOps.windowDays} gun · ${data.learningOps.quizAttempts} quiz cevabi`} tone="sky" />
          </div>

          <div className="grid gap-6 xl:grid-cols-5">
            <div className="space-y-6 xl:col-span-3">
              <Panel title="LearningOps kalite" icon={Target} right={<span className="text-[10px] font-bold text-[#98a2b3]">Son {data.learningOps.windowDays} gun</span>}>
                {data.learningOps.learningBridge && (
                  <div className="mb-5 rounded-3xl border border-[#526d82]/15 bg-white/58 p-4">
                    <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
                      <div>
                        <div className="text-[10px] font-black uppercase tracking-[0.22em] text-[#52768a]">Agent bridge monitor</div>
                        <p className="mt-1 text-xs text-[#667085]">
                          Quiz, Wiki/Notebook, Sesli Sınıf, IDE ve telafi sinyalleri aynı öğrenme hafızasına bağlanıyor mu?
                        </p>
                      </div>
                      <div className="flex gap-2 text-[10px] font-black uppercase tracking-wider">
                        <span className="rounded-full bg-emerald-500/10 px-2.5 py-1 text-[#47725d]">{data.learningOps.learningBridge.healthy} healthy</span>
                        <span className="rounded-full bg-amber-500/10 px-2.5 py-1 text-amber-300">{data.learningOps.learningBridge.watch} watch</span>
                        <span className="rounded-full bg-[#dcecf3]/70 px-2.5 py-1 text-[#667085]">{data.learningOps.learningBridge.idle} idle</span>
                      </div>
                    </div>
                    <div className="grid gap-3 lg:grid-cols-2">
                      {data.learningOps.learningBridge.bridges.map((bridge) => (
                        <div key={bridge.key} className={`rounded-2xl border p-4 ${bridgeStatusClass(bridge.status)}`}>
                          <div className="mb-2 flex items-center justify-between gap-3">
                            <div className="font-bold text-[#172033]">{bridge.label}</div>
                            <span className="rounded-full bg-white/64 px-2 py-0.5 text-[9px] font-black uppercase tracking-wider text-[#667085]">{bridge.status}</span>
                          </div>
                          <p className="mb-3 text-[11px] leading-relaxed text-[#667085]">{bridge.detail}</p>
                          <div className="flex flex-wrap gap-1.5">
                            {bridge.signals.map((signal) => (
                              <span key={signal.signalType} className="rounded-full border border-[#526d82]/12 bg-white/64 px-2 py-1 text-[10px] font-semibold text-[#344054]">
                                {signal.signalType}: {signal.count}
                              </span>
                            ))}
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                )}

                <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
                  <div className={`rounded-2xl border p-4 ${healthTone(data.learningOps.unknownSkillRatePct, 12, 25)}`}>
                    <div className="text-2xl font-black">%{data.learningOps.unknownSkillRatePct}</div>
                    <div className="mt-1 text-[10px] font-bold uppercase tracking-widest text-[#667085]">Skill bilinmiyor</div>
                  </div>
                  <div className={`rounded-2xl border p-4 ${healthTone(data.learningOps.repeatedQuestionRatePct, 8, 18)}`}>
                    <div className="text-2xl font-black">%{data.learningOps.repeatedQuestionRatePct}</div>
                    <div className="mt-1 text-[10px] font-bold uppercase tracking-widest text-[#667085]">Soru tekrari</div>
                  </div>
                  <div className="rounded-2xl border border-emerald-400/20 bg-emerald-500/10 p-4 text-[#47725d]">
                    <div className="text-2xl font-black">%{data.learningOps.quizAccuracyPct}</div>
                    <div className="mt-1 text-[10px] font-bold uppercase tracking-widest text-[#667085]">Quiz basarisi</div>
                  </div>
                  <div className="rounded-2xl border border-[#526d82]/15 bg-white/62 p-4">
                    <div className="text-2xl font-black">%{data.learningOps.remediationCompletionRatePct}</div>
                    <div className="mt-1 text-[10px] font-bold uppercase tracking-widest text-[#667085]">Telafi kapanisi</div>
                  </div>
                </div>

                <div className="mt-5 grid gap-4 lg:grid-cols-2">
                  <div className="rounded-2xl border border-[#526d82]/16 bg-black/20 p-4">
                    <div className="mb-3 text-[10px] font-black uppercase tracking-[0.2em] text-[#667085]">Zayif skill radar</div>
                    <div className="space-y-3">
                      {data.learningOps.topWeakSkills.length === 0 ? (
                        <p className="text-xs text-[#98a2b3]">Henuz zayif skill sinyali yok.</p>
                      ) : data.learningOps.topWeakSkills.map((skill) => (
                        <div key={skill.skillTag}>
                          <div className="mb-1 flex items-center justify-between gap-3 text-xs">
                            <span className="truncate text-[#344054]">{skill.skillTag}</span>
                            <span className="font-mono text-[#667085]">{skill.count}</span>
                          </div>
                          <MiniBar value={Math.min(skill.count * 12, 100)} color="bg-amber-300" />
                        </div>
                      ))}
                    </div>
                  </div>

                  <div className="rounded-2xl border border-[#526d82]/16 bg-black/20 p-4">
                    <div className="mb-3 text-[10px] font-black uppercase tracking-[0.2em] text-[#667085]">Sinyal dagilimi</div>
                    <div className="space-y-3">
                      {data.learningOps.signalCounts.length === 0 ? (
                        <p className="text-xs text-[#98a2b3]">LearningSignal henuz olusmadi.</p>
                      ) : data.learningOps.signalCounts.map((signal) => (
                        <div key={signal.signalType} className="flex items-center justify-between rounded-xl bg-white/64 px-3 py-2 text-xs">
                          <span className="text-[#344054]">{signal.signalType}</span>
                          <span className="font-mono text-[#667085]">{signal.count}</span>
                        </div>
                      ))}
                    </div>
                  </div>
                </div>
              </Panel>

              <Panel title="Ajan kalp atisi" icon={Activity}>
                <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
                  {data.agents.map((agent) => (
                    <div key={agent.agentRole} className="rounded-2xl border border-[#526d82]/15 bg-white/60 p-4 transition hover:border-[#8fb7a2]/30">
                      <div className="mb-3 flex items-start justify-between gap-3">
                        <div>
                          <div className="font-bold text-[#172033]">{AGENT_LABELS[agent.agentRole] ?? agent.agentRole}</div>
                          <div className="text-[10px] font-bold uppercase tracking-widest text-[#98a2b3]">{agent.lastProvider}</div>
                        </div>
                        {statusPill(agent.status)}
                      </div>
                      <div className="grid grid-cols-3 gap-2 text-[11px]">
                        <div>
                          <div className="text-[#98a2b3]">TTFT</div>
                          <div className={`font-mono font-bold ${latencyClass(agent.avgLatencyMs)}`}>{agent.avgLatencyMs > 0 ? `${agent.avgLatencyMs}ms` : "-"}</div>
                        </div>
                        <div>
                          <div className="text-[#98a2b3]">Cagri</div>
                          <div className="font-mono font-bold text-[#344054]">{agent.totalCalls}</div>
                        </div>
                        <div>
                          <div className="text-[#98a2b3]">Hata</div>
                          <div className="font-mono font-bold text-[#344054]">%{agent.errorRatePct}</div>
                        </div>
                      </div>
                      {agent.avgQualityScore > 0 && <MiniBar value={agent.avgQualityScore * 10} color={agent.avgQualityScore >= 7 ? "bg-emerald-400" : "bg-amber-300"} />}
                    </div>
                  ))}
                </div>
              </Panel>
            </div>

            <div className="space-y-6 xl:col-span-2">
              {endpointHealth && (
                <Panel title="Auth / endpoint health" icon={Server} right={<span className="text-[10px] font-bold text-[#98a2b3]">{endpointHealth.apiBaseUrl}</span>}>
                  <div className="grid gap-3 sm:grid-cols-2">
                    <div className={`rounded-2xl border p-4 ${endpointHealth.health.database.canConnect ? "border-emerald-400/20 bg-emerald-500/10 text-[#47725d]" : "border-red-400/25 bg-red-500/10 text-red-200"}`}>
                      <div className="text-lg font-black">{endpointHealth.health.database.canConnect ? "DB ready" : "DB down"}</div>
                      <p className="mt-1 line-clamp-2 text-[11px] text-[#667085]">{endpointHealth.health.database.error ?? endpointHealth.health.ready}</p>
                    </div>
                    <div className={`rounded-2xl border p-4 ${endpointHealth.swagger.enabled ? "border-emerald-400/20 bg-emerald-500/10 text-[#47725d]" : "border-amber-400/20 bg-amber-500/10 text-amber-200"}`}>
                      <div className="text-lg font-black">{endpointHealth.swagger.status}</div>
                      <p className="mt-1 text-[11px] text-[#667085]">{endpointHealth.swagger.json}</p>
                    </div>
                  </div>
                  <div className="mt-4 space-y-2">
                    {[...endpointHealth.auth, ...endpointHealth.core].slice(0, 12).map((endpoint) => (
                      <div key={`${endpoint.method}-${endpoint.path}`} className="flex items-center justify-between gap-3 rounded-xl border border-[#526d82]/14 bg-white/60 px-3 py-2 text-[11px]">
                        <span className="font-mono font-bold text-[#47725d]">{endpoint.method}</span>
                        <span className="min-w-0 flex-1 truncate text-[#344054]">{endpoint.path}</span>
                        <span className="rounded-full bg-[#dcecf3]/70 px-2 py-0.5 text-[9px] font-black uppercase tracking-wider text-[#667085]">{endpoint.status}</span>
                      </div>
                    ))}
                  </div>
                </Panel>
              )}

              <Panel title="Notebook cache" icon={Layers} right={<span className="text-[10px] font-bold text-[#47725d]">%{data.notebookCache.hitRatePct} hit</span>}>
                <div className="space-y-3">
                  {data.notebookCache.tools.length === 0 ? (
                    <p className="rounded-2xl border border-dashed border-[#526d82]/15 p-4 text-xs text-[#98a2b3]">Notebook arac cache metrikleri henuz yok.</p>
                  ) : data.notebookCache.tools.map((metric) => (
                    <div key={`${metric.area}-${metric.tool}`} className="rounded-2xl border border-[#526d82]/16 bg-black/20 p-4">
                      <div className="mb-2 flex items-center justify-between gap-3 text-xs">
                        <span className="font-bold text-[#172033]">{metric.tool}</span>
                        <span className="font-mono text-[#667085]">%{metric.hitRatePct}</span>
                      </div>
                      <MiniBar value={metric.hitRatePct} color={metric.hitRatePct >= 50 ? "bg-emerald-400" : "bg-amber-300"} />
                      <div className="mt-2 flex justify-between text-[10px] text-[#98a2b3]">
                        <span>{metric.hitCount} hit · {metric.missCount} miss</span>
                        <span>{metric.avgLatencyMs}ms</span>
                      </div>
                    </div>
                  ))}
                </div>
              </Panel>

              <Panel title="Redis cache" icon={Database}>
                <div className="space-y-3">
                  {data.cache.metrics.slice(0, 8).map((metric) => (
                    <div key={`${metric.area}-${metric.tool}`} className="flex items-center justify-between gap-3 rounded-xl border border-[#526d82]/14 bg-white/60 px-3 py-2 text-xs">
                      <div className="min-w-0">
                        <div className="truncate font-bold text-[#344054]">{metric.area}</div>
                        <div className="text-[10px] text-[#98a2b3]">{metric.tool}</div>
                      </div>
                      <div className="text-right font-mono">
                        <div className="text-[#172033]">%{metric.hitRatePct}</div>
                        <div className="text-[10px] text-[#98a2b3]">{metric.hitCount}/{metric.hitCount + metric.missCount}</div>
                      </div>
                    </div>
                  ))}
                </div>
              </Panel>

              <Panel title="Model failover" icon={Gauge}>
                <div className="space-y-4">
                  {sortedProviders.length === 0 ? (
                    <p className="text-xs text-[#98a2b3]">Provider metrigi henuz yok.</p>
                  ) : sortedProviders.map((provider) => {
                    const meta = providerMeta(provider.provider);
                    return (
                      <div key={provider.provider}>
                        <div className="mb-1.5 flex items-center justify-between text-xs">
                          <span className="font-bold text-[#344054]">{meta.label}</span>
                          <span className="font-mono text-[#667085]">%{provider.percentage}</span>
                        </div>
                        <MiniBar value={provider.percentage} color={meta.color} />
                        <div className="mt-1 flex justify-between text-[10px] text-[#98a2b3]">
                          <span>{provider.callCount} cagri</span>
                          <span>{provider.avgLatencyMs}ms · {provider.errorCount} hata</span>
                        </div>
                      </div>
                    );
                  })}
                </div>
              </Panel>

              <Panel title="LLMOps son log" icon={Zap}>
                <div className="max-h-72 space-y-2 overflow-y-auto pr-1">
                  {data.llmops.recentLogs.length === 0 ? (
                    <p className="rounded-2xl border border-dashed border-[#526d82]/15 p-4 text-xs text-[#98a2b3]">Evaluator logu henuz yok.</p>
                  ) : data.llmops.recentLogs.map((log, idx) => (
                    <div key={`${log.recordedAt}-${idx}`} className="rounded-2xl border border-[#526d82]/15 bg-white/60 p-3">
                      <div className="mb-1 flex items-center justify-between gap-2">
                        <span className="text-[10px] font-bold uppercase tracking-widest text-[#98a2b3]">{log.agentRole ?? "Agent"}</span>
                        <span className="font-mono text-xs font-bold text-amber-300">{log.score}/10</span>
                      </div>
                      <p className="line-clamp-2 text-[11px] leading-relaxed text-[#667085]">{log.feedback || "Geri bildirim yok."}</p>
                      <div className="mt-1 flex items-center gap-1 text-[10px] text-zinc-700"><Clock className="h-3 w-3" />{log.recordedAt}</div>
                    </div>
                  ))}
                </div>
              </Panel>

              <div className="grid grid-cols-2 gap-3">
                <MetricCard icon={BarChart3} label="Token" value={formatNumber(data.tokens.total)} hint={`$${data.tokens.costUSD.toFixed(4)} maliyet`} />
                <MetricCard icon={DollarSign} label="Eval" value={data.llmops.avgEvaluatorScore > 0 ? `${data.llmops.avgEvaluatorScore}/10` : "-"} hint={`${data.llmops.totalEvaluations} kayit`} />
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
