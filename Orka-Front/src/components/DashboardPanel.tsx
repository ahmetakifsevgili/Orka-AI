import { useEffect, useMemo, useState, type ComponentType } from "react";
import {
  Activity,
  ArrowRight,
  Award,
  BookOpen,
  Brain,
  Code2,
  Cpu,
  Flame,
  Lightbulb,
  MessageSquare,
  Sparkles,
  Target,
  TrendingUp,
} from "lucide-react";
import { motion } from "framer-motion";
import { useQuizHistory } from "@/contexts/QuizHistoryContext";
import { QuizAPI, DashboardAPI, UserAPI, storage } from "@/services/api";
import type { ActiveLearningContext, ApiTopic, ApiGlobalStats, ApiDashboardStats, ApiGamification, ContextRailTab } from "@/lib/types";
import SystemHealthHUD from "@/components/SystemHealthHUD";
import SkillTreePanel from "@/components/SkillTreePanel";

interface DashboardPanelProps {
  topics: ApiTopic[];
  onViewChange: (view: string) => void;
  onFocusTopic?: (topic: ApiTopic, options?: { tab?: ContextRailTab; intent?: ActiveLearningContext["intent"] }) => void;
}

type TabId = "karne" | "hud" | "agac";

// ARCH: clampPercent defined BEFORE any component that uses it
function clampPercent(value: number) {
  if (!Number.isFinite(value)) return 0;
  return Math.max(0, Math.min(100, Math.round(value)));
}

function LevelProgressCard({ gamification, loading }: { gamification: ApiGamification | null; loading: boolean }) {
  if (loading || !gamification) return <MetricCard icon={TrendingUp} label="Toplam XP" value="..." tone="blue" />;

  // BUG-1 FIX: correct percentage = xpInLevel / totalXpPerLevel
  const totalInLevel = gamification.xpInLevel + gamification.xpToNextLevel;
  const pct = clampPercent(totalInLevel > 0 ? (gamification.xpInLevel / totalInLevel) * 100 : 0);

  return (
    <div className="rounded-[1.35rem] border border-[#526d82]/12 bg-[#f4f7f7]/72 p-5 shadow-[0_10px_28px_rgba(66,91,112,0.06)] backdrop-blur-xl transition hover:-translate-y-0.5 hover:shadow-[0_14px_30px_rgba(66,91,112,0.08)]">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <div className="grid h-12 w-12 place-items-center rounded-2xl bg-[#dcecf3]/78 text-[#2d5870] shadow-inner">
            <Award className="h-6 w-6" />
          </div>
          <div>
            <p className="text-[11px] font-black uppercase tracking-[0.15em] text-[#667085]">Seviye {gamification.level}</p>
            <p className="text-lg font-black tracking-tight text-[#172033]">{gamification.levelLabel}</p>
          </div>
        </div>
        <div className="text-right">
          <p className="text-2xl font-black tracking-tight text-[#2d5870]">{gamification.totalXP} XP</p>
        </div>
      </div>
      <div className="mt-5">
        <div className="flex justify-between px-1 text-[10px] font-extrabold uppercase tracking-wider text-[#8a97a0]">
          <span>{gamification.xpInLevel} XP</span>
          <span>Sonraki seviyeye {gamification.xpToNextLevel} XP</span>
        </div>
        <div className="mt-2 h-2.5 overflow-hidden rounded-full bg-[#e1e9ea]/80 shadow-inner">
          <div className="h-full rounded-full bg-gradient-to-r from-[#8fb7a2] to-[#547c61] transition-all duration-1000" style={{ width: `${pct}%` }} />
        </div>
      </div>
    </div>
  );
}

function SuccessRateSparkline({ data }: { data?: ApiGlobalStats["dailyProgress"] }) {
  if (!data || data.length < 2) return <div className="h-10 rounded-xl bg-[#e7ecec]/70" />;

  const width = 220;
  const height = 48;
  const padding = 6;
  const points = data
    .map((item, index) => {
      const x = (index / (data.length - 1)) * (width - 2 * padding) + padding;
      const y = height - (clampPercent(item.accuracy) / 100) * (height - 2 * padding) - padding;
      return `${x},${y}`;
    })
    .join(" ");

  return (
    <svg width="100%" height={height} viewBox={`0 0 ${width} ${height}`} className="overflow-visible">
      <polyline points={points} fill="none" stroke="rgba(84,124,97,0.14)" strokeWidth="8" strokeLinecap="round" strokeLinejoin="round" />
      <polyline points={points} fill="none" stroke="#547c61" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" />
      <circle cx={width - padding} cy={height - (clampPercent(data[data.length - 1].accuracy) / 100) * (height - 2 * padding) - padding} r="4" fill="#547c61" stroke="#f7f4ec" strokeWidth="3" />
    </svg>
  );
}

function WeeklyActivityBars({ data }: { data?: ApiDashboardStats["activity"] }) {
  // Son 7 günü tam doldurmak için boş günleri 0 ile besler.
  const filled = useMemo(() => {
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    const buckets: Array<{ date: string; count: number; label: string }> = [];
    for (let i = 6; i >= 0; i--) {
      const d = new Date(today);
      d.setDate(today.getDate() - i);
      const isoDate = d.toISOString().slice(0, 10);
      const found = data?.find((a) => a.date.slice(0, 10) === isoDate);
      const dayLabel = ["Paz", "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt"][d.getDay()];
      buckets.push({ date: isoDate, count: found?.count ?? 0, label: dayLabel });
    }
    return buckets;
  }, [data]);

  const max = Math.max(1, ...filled.map((b) => b.count));
  const total = filled.reduce((acc, b) => acc + b.count, 0);

  return (
    <div className="space-y-2">
      <div className="flex items-end gap-1.5 h-20">
        {filled.map((bucket) => {
          const heightPct = (bucket.count / max) * 100;
          const isToday = bucket.count > 0 && bucket === filled[filled.length - 1];
          return (
            <div key={bucket.date} className="flex flex-1 flex-col items-center gap-1">
              <div className="flex-1 flex items-end w-full">
                <div
                  className={`w-full rounded-t-md transition-all ${isToday ? "bg-[#547c61]" : "bg-[#8fb7a2]/55"}`}
                  style={{ height: `${Math.max(4, heightPct)}%` }}
                  title={`${bucket.date}: ${bucket.count} mesaj`}
                />
              </div>
              <span className="text-[9px] font-bold uppercase tracking-wider text-[#667085]">{bucket.label}</span>
            </div>
          );
        })}
      </div>
      <p className="text-[10px] font-semibold text-[#667085]">
        Bu hafta toplam <span className="font-black text-[#172033]">{total}</span> mesaj.
      </p>
    </div>
  );
}

function MetricCard({ icon: Icon, label, value, helper, tone = "blue" }: {
  icon: ComponentType<{ className?: string }>;
  label: string;
  value: string | number;
  helper?: string;
  tone?: "blue" | "sage" | "paper";
}) {
  const toneClass = {
    blue: "bg-[#dcecf3]/78 text-[#2d5870]",
    sage: "bg-[#ddebe3]/78 text-[#547c61]",
    paper: "bg-[#fff8ee]/86 text-[#8a641f]",
  }[tone];

  return (
    <div className="rounded-[1.35rem] border border-[#526d82]/12 bg-[#f4f7f7]/72 p-4 shadow-[0_10px_28px_rgba(66,91,112,0.06)] backdrop-blur-xl">
      <div className={`mb-4 grid h-9 w-9 place-items-center rounded-2xl ${toneClass}`}>
        <Icon className="h-4 w-4" />
      </div>
      <p className="text-2xl font-black tracking-tight text-[#172033]">{value}</p>
      <p className="mt-1 text-[11px] font-black uppercase tracking-[0.15em] text-[#667085]">{label}</p>
      {helper && <p className="mt-1 text-[11px] font-semibold text-[#8a97a0]">{helper}</p>}
    </div>
  );
}

function SectionHeader({ icon: Icon, title, description }: {
  icon: ComponentType<{ className?: string }>;
  title: string;
  description?: string;
}) {
  return (
    <div className="mb-4 flex items-start justify-between gap-4">
      <div>
        <div className="flex items-center gap-2 text-[11px] font-black uppercase tracking-[0.18em] text-[#52768a]">
          <Icon className="h-3.5 w-3.5" />
          {title}
        </div>
        {description && <p className="mt-1 max-w-2xl text-sm leading-6 text-[#667085]">{description}</p>}
      </div>
    </div>
  );
}

function EmptyState({ title, body, action, onAction }: { title: string; body: string; action: string; onAction: () => void }) {
  return (
    <div className="rounded-[1.5rem] border border-dashed border-[#526d82]/18 bg-[#eef1f3]/54 p-8 text-center">
      <p className="text-sm font-extrabold text-[#172033]">{title}</p>
      <p className="mx-auto mt-2 max-w-md text-xs leading-6 text-[#667085]">{body}</p>
      <button onClick={onAction} className="mt-5 inline-flex items-center gap-2 rounded-2xl bg-[#172033] px-4 py-2 text-xs font-extrabold text-white shadow-sm transition hover:-translate-y-0.5 hover:bg-[#24314b]">
        {action}
        <ArrowRight className="h-3.5 w-3.5" />
      </button>
    </div>
  );
}

function TopicProgressRow({ topic, onFocusTopic }: { topic: ApiTopic; onFocusTopic?: (topic: ApiTopic) => void }) {
  const pct = clampPercent(topic.totalSections ? ((topic.completedSections || 0) / topic.totalSections) * 100 : topic.progressPercentage || 0);

  return (
    <button
      type="button"
      onClick={() => onFocusTopic?.(topic)}
      className="group w-full rounded-[1.35rem] border border-[#526d82]/11 bg-[#f7f4ec]/58 p-4 text-left transition hover:-translate-y-0.5 hover:border-[#9ec7d9]/45 hover:bg-[#f7f9fa]/84 hover:shadow-[0_14px_30px_rgba(66,91,112,0.08)]"
    >
      <div className="flex items-center justify-between gap-4">
        <div className="flex min-w-0 items-center gap-3">
          <div className="grid h-11 w-11 flex-shrink-0 place-items-center rounded-2xl bg-[#dcecf3]/70 text-lg shadow-inner">
            {topic.emoji || "O"}
          </div>
          <div className="min-w-0">
            <p className="truncate text-sm font-extrabold text-[#172033]">{topic.title}</p>
            <p className="mt-0.5 text-[11px] font-bold uppercase tracking-[0.12em] text-[#8a97a0]">{topic.category || "Genel"}</p>
          </div>
        </div>
        <span className="rounded-full bg-[#eef1f3]/86 px-3 py-1 text-xs font-black text-[#2d5870]">%{pct}</span>
      </div>
      <div className="mt-4 h-2 overflow-hidden rounded-full bg-[#e1e9ea]">
        <div className="h-full rounded-full bg-gradient-to-r from-[#8fb7a2] to-[#9ec7d9] transition-all duration-700" style={{ width: `${pct}%` }} />
      </div>
    </button>
  );
}

export default function DashboardPanel({ topics, onViewChange, onFocusTopic }: DashboardPanelProps) {
  const { attempts: sessionAttempts } = useQuizHistory();
  const isAdmin = storage.getUser()?.isAdmin === true;
  const [activeTab, setActiveTab] = useState<TabId>("karne");
  const [stats, setStats] = useState<ApiGlobalStats | null>(null);
  const [dashStats, setDashStats] = useState<ApiDashboardStats | null>(null);
  const [gamification, setGamification] = useState<ApiGamification | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    setLoading(true);
    setLoadError(null);

    Promise.allSettled([
      QuizAPI.getGlobalStats(),
      UserAPI.getGamification(),
      DashboardAPI.getStats(),
    ])
      .then(([quizResult, gamificationResult, dashboardResult]) => {
        if (!active) return;

        if (quizResult.status === "fulfilled") setStats(quizResult.value.data as ApiGlobalStats);
        if (gamificationResult.status === "fulfilled") setGamification(gamificationResult.value.data as ApiGamification);
        if (dashboardResult.status === "fulfilled") setDashStats(dashboardResult.value.data as ApiDashboardStats);

        if ([quizResult, gamificationResult, dashboardResult].every((result) => result.status === "rejected")) {
          setLoadError("Dashboard verileri alınamadı. Bağlantıyı kontrol edip tekrar deneyin.");
        }
      })
      .catch((err) => {
        console.error("Dashboard fetch error:", err);
        if (active) setLoadError("Dashboard verileri alınamadı. Bağlantıyı kontrol edip tekrar deneyin.");
      })
      .finally(() => {
        if (active) setLoading(false);
      });

    return () => { active = false; };
  }, [sessionAttempts.length]);

  const learningSignalBook = dashStats?.learningSignalBook;
  const weakSkills = learningSignalBook?.weakSkills ?? [];
  const recentSignals = learningSignalBook?.recentSignals ?? [];
  const accuracy = clampPercent(stats?.accuracy ?? 0);
  const totalLessons = dashStats?.totalSections ?? topics.reduce((sum, topic) => sum + (topic.totalSections ?? 0), 0);
  const completedLessons = dashStats?.completedSections ?? topics.reduce((sum, topic) => sum + (topic.completedSections ?? 0), 0);
  // DEAD-3 REMOVED: totalXP was unused after LevelProgressCard took over
  const activeStreak = dashStats?.currentStreak ?? gamification?.currentStreak ?? 0;
  const progress = clampPercent(dashStats?.progressPercentage ?? (totalLessons > 0 ? (completedLessons / totalLessons) * 100 : 0));
  const activeLearningCount = dashStats?.activeLearning ?? topics.length;

  const nextAction = useMemo(() => {
    if (weakSkills[0]) {
      return {
        label: "🎯 Günün Meydan Okuması",
        title: `${weakSkills[0].skillTag || "Bir beceri"} üzerine telafi iyi olur`,
        body: weakSkills[0].topicPath || "Son quiz sinyallerine göre kısa bir tekrar ve mikro quiz öneriliyor.",
        action: "Telafi sohbeti aç",
        view: "chat",
        icon: Target,
      };
    }
    if (topics.length > 0) {
      return {
        label: "Bugünkü Öğrenme Kokpiti",
        title: "Bugün bir dersi tamamlamak en iyi hamle",
        body: "Aktif konuna dön, kısa bir anlatım al ve ardından 3 soruluk mini kontrol yap.",
        action: "Derse devam et",
        view: "chat",
        icon: Brain,
      };
    }
    return {
      label: "Bugünkü Öğrenme Kokpiti",
      title: "İlk öğrenme yolunu oluşturalım",
      body: "Bir hedef yaz, Orka bunu plan, quiz, wiki ve telafi zincirine dönüştürsün.",
      action: "Yeni hedef yaz",
      view: "chat",
      icon: Sparkles,
    };
  }, [topics.length, weakSkills]);
  const NextActionIcon = nextAction.icon;
  const focusFirstTopic = (tab: ContextRailTab = "wiki", intent: ActiveLearningContext["intent"] = "lesson") => {
    if (topics[0] && onFocusTopic) {
      onFocusTopic(topics[0], { tab, intent });
      return;
    }
    onViewChange("chat");
  };

  if (activeTab === "hud" && isAdmin) {
    return (
      <div className="flex h-full flex-col bg-transparent">
        <div className="flex-shrink-0 px-6 pt-5">
          {/* UI-2 FIX: HUD tab bar now includes all tabs for consistent navigation */}
          <div className="inline-flex rounded-2xl border border-[#526d82]/12 bg-[#eef1f3]/72 p-1">
            <button onClick={() => setActiveTab("karne")} className="rounded-xl px-4 py-2 text-xs font-extrabold text-[#667085] transition hover:text-[#172033]">Öğrenme Karnesi</button>
            <button onClick={() => setActiveTab("agac")} className="rounded-xl px-4 py-2 text-xs font-extrabold text-[#667085] transition hover:text-[#172033]">Yetenek Ağacı</button>
            <button className="rounded-xl bg-[#172033] px-4 py-2 text-xs font-extrabold text-white shadow-sm">Sistem Analitiği</button>
          </div>
        </div>
        <SystemHealthHUD />
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col bg-transparent">
      <div className="flex-shrink-0 px-4 pt-4 sm:px-6 lg:px-8">
        <div className="flex flex-wrap items-center gap-2 rounded-2xl border border-[#526d82]/10 bg-[#eef1f3]/62 p-1 backdrop-blur-xl">
          <button onClick={() => setActiveTab("karne")} className={`inline-flex items-center gap-2 rounded-xl px-4 py-2 text-xs font-extrabold transition ${activeTab === "karne" ? "bg-[#f7f4ec] text-[#172033] shadow-sm" : "text-[#667085] hover:text-[#172033]"}`}>
            <Award className="h-3.5 w-3.5" />
            Öğrenme Karnesi
          </button>
          <button onClick={() => setActiveTab("agac")} className={`inline-flex items-center gap-2 rounded-xl px-4 py-2 text-xs font-extrabold transition ${activeTab === "agac" ? "bg-[#f7f4ec] text-[#172033] shadow-sm" : "text-[#667085] hover:text-[#172033]"}`}>
            <Sparkles className="h-3.5 w-3.5" />
            Yetenek Ağacı
          </button>
          {isAdmin && (
            <button onClick={() => setActiveTab("hud")} className="inline-flex items-center gap-2 rounded-xl px-4 py-2 text-xs font-extrabold text-[#667085] transition hover:text-[#172033]">
              <Cpu className="h-3.5 w-3.5" />
              Sistem Analitiği
              <span className="h-1.5 w-1.5 rounded-full bg-[#8fb7a2]" />
            </button>
          )}
        </div>
      </div>

      <div className="min-h-0 flex-1 overflow-y-auto px-4 pb-8 pt-5 sm:px-6 lg:px-8">
        <div className="mx-auto flex w-full max-w-6xl flex-col gap-6">
          {activeTab === "agac" ? (
            <SkillTreePanel topics={topics} onFocusTopic={(selected) => onFocusTopic?.(selected, { tab: "wiki", intent: "lesson" })} />
          ) : (
            <>
              <motion.section
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.28 }}
            className="rounded-[2rem] border border-[#526d82]/12 bg-[#f7f4ec]/74 p-5 shadow-[0_18px_55px_rgba(66,91,112,0.08)] backdrop-blur-2xl lg:p-7"
          >
            <div className="grid gap-6 lg:grid-cols-[1.15fr_0.85fr] lg:items-stretch">
              <div className="flex min-h-[260px] flex-col justify-between rounded-[1.5rem] border border-[#526d82]/10 bg-[#f7f9fa]/76 p-5">
                <div>
                  <div className="mb-4 inline-flex items-center gap-2 rounded-full bg-[#dcecf3]/70 px-3 py-1 text-[11px] font-black uppercase tracking-[0.16em] text-[#2d5870]">
                    <Lightbulb className="h-3.5 w-3.5" />
                    {nextAction.label}
                  </div>
                  <h1 className="max-w-2xl text-2xl font-black tracking-tight text-[#172033] sm:text-3xl">
                    {nextAction.title}
                  </h1>
                  <p className="mt-3 max-w-2xl text-sm leading-7 text-[#667085]">{nextAction.body}</p>
                </div>

                <div className="mt-6 flex flex-wrap items-center gap-3">
                  <button onClick={() => focusFirstTopic(weakSkills[0] ? "practice" : "wiki", weakSkills[0] ? "practice" : "lesson")} className="inline-flex items-center gap-2 rounded-2xl bg-[#172033] px-5 py-3 text-sm font-extrabold text-white shadow-sm transition hover:-translate-y-0.5 hover:bg-[#24314b]">
                    <NextActionIcon className="h-4 w-4" />
                    {nextAction.action}
                  </button>
                  <button onClick={() => focusFirstTopic("wiki", "review")} id="tour-wiki-access" className="inline-flex items-center gap-2 rounded-2xl border border-[#526d82]/12 bg-[#eef1f3]/82 px-5 py-3 text-sm font-extrabold text-[#344054] transition hover:-translate-y-0.5 hover:bg-[#e4eaec]">
                    <BookOpen className="h-4 w-4" />
                    Wiki hafızasına bak
                  </button>
                </div>
              </div>

              <div id="tour-global-stats" className="grid gap-3 sm:grid-cols-2 lg:grid-cols-1">
                <LevelProgressCard gamification={gamification} loading={loading} />
                <MetricCard icon={Flame} label="Öğrenme Serisi" value={loading ? "..." : activeStreak} helper={activeStreak > 0 ? `${activeStreak} günlük ritim` : "Bugün başlatılabilir"} tone="paper" />
              </div>
            </div>
          </motion.section>

          {loadError && (
            <div className="rounded-2xl border border-[#c77b6b]/22 bg-[#f4e1dc]/72 p-4 text-sm font-semibold text-[#9a4e3e]">
              {loadError}
            </div>
          )}

          <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <MetricCard icon={Target} label="Doğruluk" value={loading ? "..." : `%${accuracy}`} helper={`${stats?.correctAnswers ?? 0}/${stats?.totalQuizzes ?? 0} doğru cevap`} tone="sage" />
            <MetricCard icon={Brain} label="Ders İlerlemesi" value={loading ? "..." : `${completedLessons}/${totalLessons || 0}`} helper={`Genel ilerleme %${progress}`} tone="blue" />
            <MetricCard icon={Activity} label="Aktif Öğrenme" value={loading ? "..." : activeLearningCount} helper="Canlı topic ve sinyal" tone="paper" />
            <div className="rounded-[1.35rem] border border-[#526d82]/12 bg-[#f4f7f7]/72 p-4 shadow-[0_10px_28px_rgba(66,91,112,0.06)] backdrop-blur-xl">
              <p className="text-[11px] font-black uppercase tracking-[0.15em] text-[#667085]">7 Günlük doğruluk</p>
              <div className="mt-4">
                <SuccessRateSparkline data={stats?.dailyProgress} />
              </div>
            </div>
          </section>

          <section className="grid gap-4 md:grid-cols-[1.4fr_1fr]">
            <div className="rounded-[1.35rem] border border-[#526d82]/12 bg-[#f4f7f7]/72 p-5 shadow-[0_10px_28px_rgba(66,91,112,0.06)] backdrop-blur-xl">
              <div className="flex items-center justify-between mb-3">
                <p className="text-[11px] font-black uppercase tracking-[0.15em] text-[#667085]">Bu hafta etkinliğin</p>
                <span className="text-[10px] font-bold text-[#8a97a0]">son 7 gün</span>
              </div>
              {loading ? (
                <div className="h-20 rounded-xl bg-[#e7ecec]/70 animate-pulse" />
              ) : (
                <WeeklyActivityBars data={dashStats?.activity} />
              )}
            </div>
            <div className="rounded-[1.35rem] border border-[#526d82]/12 bg-[#f4f7f7]/72 p-5 shadow-[0_10px_28px_rgba(66,91,112,0.06)] backdrop-blur-xl">
              <p className="text-[11px] font-black uppercase tracking-[0.15em] text-[#667085]">Son 200 quiz cevabı</p>
              <div className="mt-3">
                <p className="text-3xl font-black tracking-tight text-[#172033]">
                  {loading ? "..." : (learningSignalBook?.totalRecentAttempts ?? 0)}
                </p>
                <p className="mt-1 text-[11px] font-semibold text-[#667085]">
                  {weakSkills.length > 0
                    ? `${weakSkills.length} beceri zayıf etiketlendi.`
                    : "Henüz belirgin zayıf beceri yok."}
                </p>
              </div>
            </div>
          </section>

          <section className="grid gap-6 xl:grid-cols-[1.08fr_0.92fr]">
            <div id="tour-learning-signals" className="rounded-[2rem] border border-[#526d82]/12 bg-[#f7f9fa]/74 p-5 shadow-[0_14px_38px_rgba(66,91,112,0.07)] backdrop-blur-2xl lg:p-6">
              <SectionHeader icon={Sparkles} title="Öğrenci Sinyal Defteri" description={learningSignalBook?.summary || "Quiz cevapları, IDE çıktıları, Wiki aksiyonları ve 'anlamadım' sinyalleri burada kişisel öğrenme haritasına dönüşür."} />

              <div className="grid gap-4 md:grid-cols-2">
                <div className="rounded-[1.35rem] bg-[#eef1f3]/70 p-4">
                  <p className="mb-3 text-[11px] font-black uppercase tracking-[0.16em] text-[#667085]">Zayıf beceriler</p>
                  {weakSkills.length === 0 ? (
                    <p className="text-xs leading-6 text-[#667085]">Henüz güçlü bir zayıf beceri sinyali yok. Bir quiz çözdüğünde burası kişiselleşir.</p>
                  ) : (
                    <div className="space-y-2">
                      {weakSkills.slice(0, 4).map((skill) => (
                        <div key={`${skill.skillTag}-${skill.topicPath}`} className="rounded-2xl border border-[#526d82]/10 bg-[#f7f4ec]/78 px-3 py-3">
                          <div className="flex items-center justify-between gap-3">
                            <span className="truncate text-xs font-extrabold text-[#172033]">{skill.skillTag || "Etiketsiz beceri"}</span>
                            <span className="rounded-full bg-[#fff8ee] px-2 py-0.5 text-[10px] font-black text-[#8a641f]">%{clampPercent(skill.accuracy)}</span>
                          </div>
                          <p className="mt-1 text-[11px] leading-5 text-[#667085]">{skill.topicPath || "Genel konu"}</p>
                        </div>
                      ))}
                    </div>
                  )}
                </div>

                <div className="rounded-[1.35rem] bg-[#fff8ee]/72 p-4">
                  <p className="mb-3 text-[11px] font-black uppercase tracking-[0.16em] text-[#8a641f]">Son öğrenme sinyalleri</p>
                  {recentSignals.length === 0 ? (
                    <p className="text-xs leading-6 text-[#667085]">Anlamadım, quiz cevabı, Wiki tıklaması ve IDE gönderimi geldikçe ajan köprüsü burada görünür olur.</p>
                  ) : (
                    <div className="space-y-2">
                      {recentSignals.slice(0, 5).map((signal, index) => (
                        <div key={`${signal.signalType}-${signal.createdAt}-${index}`} className="flex items-center justify-between gap-3 rounded-2xl bg-[#f7f9fa]/72 px-3 py-2">
                          <span className="truncate text-xs font-extrabold text-[#172033]">{signal.signalType}</span>
                          <span className="truncate text-[10px] font-semibold text-[#667085]">{signal.skillTag || signal.topicPath || "genel"}</span>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </div>
            </div>

            <div className="rounded-[2rem] border border-[#526d82]/12 bg-[#eef1f3]/62 p-5 shadow-[0_14px_38px_rgba(66,91,112,0.06)] backdrop-blur-2xl lg:p-6">
              <SectionHeader icon={Award} title="Hızlı aksiyonlar" description="Kokpit sadece rapor değil; bir sonraki hamleyi başlatır." />
              <div className="grid gap-3">
                <button onClick={() => focusFirstTopic("wiki", "lesson")} className="flex items-center justify-between rounded-[1.25rem] border border-[#526d82]/10 bg-[#f7f9fa]/76 p-4 text-left transition hover:-translate-y-0.5 hover:bg-[#f7f4ec]">
                  <span>
                    <span className="block text-sm font-extrabold text-[#172033]">Öğrenmeye devam et</span>
                    <span className="mt-1 block text-xs text-[#667085]">Aktif topic üzerinden tutor ile ilerle.</span>
                  </span>
                  <MessageSquare className="h-4 w-4 text-[#52768a]" />
                </button>
                <button onClick={() => onViewChange("ide")} className="flex items-center justify-between rounded-[1.25rem] border border-[#526d82]/10 bg-[#f7f9fa]/76 p-4 text-left transition hover:-translate-y-0.5 hover:bg-[#f7f4ec]">
                  <span>
                    <span className="block text-sm font-extrabold text-[#172033]">Kod editörünü aç</span>
                    <span className="mt-1 block text-xs text-[#667085]">Çıktıyı hocaya gönder, hata sinyali yazılsın.</span>
                  </span>
                  <Code2 className="h-4 w-4 text-[#52768a]" />
                </button>
              </div>
            </div>
          </section>

          <section id="tour-course-progress" className="rounded-[2rem] border border-[#526d82]/12 bg-[#f7f9fa]/70 p-5 shadow-[0_14px_38px_rgba(66,91,112,0.06)] backdrop-blur-2xl lg:p-6">
            <SectionHeader icon={Activity} title="Konu ilerlemesi" description="Açık ders yolların, tamamlanma oranı ve sakin ilerleme ritmi." />
            {topics.length === 0 ? (
              <EmptyState title="Henüz öğrenme yolun yok" body="Chat ekranına bir hedef yaz; Orka bunu plan, wiki, quiz ve telafi zincirine dönüştürsün." action="İlk hedefi yaz" onAction={() => onViewChange("chat")} />
            ) : (
              <div className="grid gap-3 lg:grid-cols-2">
                {topics.slice(0, 6).map((topic) => (
                  <TopicProgressRow
                    key={topic.id}
                    topic={topic}
                    onFocusTopic={(selected) => onFocusTopic?.(selected, { tab: "wiki", intent: "lesson" })}
                  />
                ))}
              </div>
            )}
          </section>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
