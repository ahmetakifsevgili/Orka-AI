import { useState, useEffect } from "react";
import {
  BookOpen,
  AlertTriangle,
  ArrowRight,
  ChevronRight,
  CheckCircle2,
  CircleDashed,
  Activity,
  Award,
  Code2,
  Compass,
  Cpu,
  FileText,
  GraduationCap,
  Lightbulb,
  MessageSquareText,
  Repeat2,
  ShieldCheck,
} from "lucide-react";
import { useQuizHistory } from "@/contexts/QuizHistoryContext";
import { QuizAPI, DashboardAPI, UserAPI, storage, type DashboardTodayDto } from "@/services/api";
import type { ApiTopic, ApiGlobalStats, ApiDashboardStats, ApiGamification } from "@/lib/types";
import SystemHealthHUD from "@/components/SystemHealthHUD";
import { useLanguage } from "@/contexts/LanguageContext";
import { WorkspaceHeader, SourceHealthStrip, WorkspaceMetric } from "./AgenticWorkspace";

interface DashboardPanelProps {
  topics: ApiTopic[];
  onViewChange: (view: string) => void;
  mode?: "today" | "progress";
}

/** 
 * Custom Sparkline Component
 * UX Mandate: No heavy chart libs, premium SVG feel.
 */
function SuccessRateSparkline({ data }: { data: ApiGlobalStats['dailyProgress'] }) {
  if (!data || data.length < 2) return null;
  
  const width = 200;
  const height = 40;
  const padding = 5;
  
  const maxVal = 100;
  const minVal = 0;
  
  const points = data.map((d, i) => {
    const x = (i / (data.length - 1)) * (width - 2 * padding) + padding;
    const y = height - ((d.accuracy - minVal) / (maxVal - minVal)) * (height - 2 * padding) - padding;
    return `${x},${y}`;
  }).join(" ");

  return (
    <div className="relative group">
      <svg width={width} height={height} className="overflow-visible">
        {/* Shadow path for depth */}
        <polyline
          points={points}
          fill="none"
          stroke="rgba(16, 185, 129, 0.1)"
          strokeWidth="4"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
        {/* Main path */}
        <polyline
          points={points}
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
          className="text-emerald-500/80"
        />
        {/* End dot */}
        <circle 
          cx={(width - padding)} 
          cy={height - ((data[data.length-1].accuracy - minVal) / (maxVal - minVal)) * (height - 2 * padding) - padding}
          r="3"
          className="fill-emerald-400 stroke-emerald-950 stroke-2"
        />
      </svg>
    </div>
  );
}

const STUDY_FOCUS_OPTIONS = [
  { id: "general", labelKey: "focus_general", hintKey: "focus_general_hint" },
  { id: "kpss", labelKey: "KPSS", hintKey: "focus_kpss_hint" },
  { id: "yks", labelKey: "YKS", hintKey: "focus_yks_hint" },
  { id: "language", labelKey: "focus_language", hintKey: "focus_language_hint" },
  { id: "software", labelKey: "focus_software", hintKey: "focus_software_hint" },
  { id: "math", labelKey: "focus_math", hintKey: "focus_math_hint" },
];

type CoordinationMetric = NonNullable<DashboardTodayDto["coordinationHealth"]>["metrics"][number];
type WeakSkillSignal = NonNullable<ApiDashboardStats["learningSignalBook"]>["weakSkills"][number];
type WeakConceptSignal = DashboardTodayDto["weakConcepts"][number];
type HealthTone = "ok" | "warning" | "missing";
type GuidanceActionView = "chat" | "wiki" | "practice" | "sources" | "learning" | "dashboard";

interface WeakConceptQueueItem {
  id: string;
  title: string;
  detail: string;
  reason: string;
  actionLabel: string;
  actionView: GuidanceActionView;
  tone: "weak" | "source" | "practice";
}

const COORDINATION_METRIC_ORDER = [
  "sourceCoverage",
  "wikiReadiness",
  "quizCoverage",
  "learningProfileCoverage",
  "ragScopeCoverage",
  "sourceQuality",
];

function metricTone(status?: string): HealthTone {
  switch ((status ?? "").toLowerCase()) {
    case "healthy":
      return "ok";
    case "watch":
    case "degraded":
    case "critical":
      return "warning";
    default:
      return "missing";
  }
}

function metricToneClass(tone: HealthTone): string {
  switch (tone) {
    case "ok":
      return "border-[#8fb7a2]/35 bg-[#f2faf5]/72 text-[#47725d]";
    case "warning":
      return "border-[#e8c46f]/35 bg-[#fff8ee]/78 text-[#8a641f]";
    default:
      return "border-[#526d82]/12 bg-white/58 text-[#667085]";
  }
}

function metricIcon(tone: HealthTone) {
  if (tone === "ok") return <CheckCircle2 className="h-3.5 w-3.5" />;
  if (tone === "warning") return <AlertTriangle className="h-3.5 w-3.5" />;
  return <CircleDashed className="h-3.5 w-3.5" />;
}

function coordinationLabel(metric: CoordinationMetric): string {
  const key = metric.key;
  const status = (metric.status ?? "").toLowerCase();
  if (key === "sourceCoverage") {
    if (metric.total <= 0) return "Kaynak yok";
    return metric.count >= metric.total ? "Kaynaklar hazır" : "Kaynaklar hazırlanıyor";
  }
  if (key === "wikiReadiness") return status === "healthy" ? "Wiki içeriği var" : "Wiki eksik olabilir";
  if (key === "quizCoverage") return metric.count > 0 ? "Quiz kanıtı var" : "Quiz kanıtı zayıf";
  if (key === "learningProfileCoverage") return metric.count > 0 ? "Öğrenme sinyali oluşuyor" : "Öğrenme sinyali bekleniyor";
  if (key === "ragScopeCoverage") {
    if (status === "healthy") return "RAG kaynak kalitesi iyi";
    if (status === "idle") return "RAG henüz çalışmadı";
    return "RAG kanıtı izlenmeli";
  }
  if (key === "sourceQuality") {
    if (status === "healthy") return "Kaynak kalitesi iyi";
    if (status === "idle") return "Kaynak kalite raporu yok";
    return "Kaynak kalitesi zayıf";
  }
  return metric.userSafeLabel || "Durum izleniyor";
}

function coordinationDetail(metric: CoordinationMetric): string {
  if (metric.userSafeDetail) return metric.userSafeDetail;
  if (metric.total > 0) return `${metric.count}/${metric.total} kanıt görünüyor.`;
  return "Bu alanda henüz yeterli öğrenme verisi yok.";
}

function pickCoordinationMetrics(metrics?: CoordinationMetric[]): CoordinationMetric[] {
  if (!metrics?.length) return [];
  const byKey = new Map(metrics.map((metric) => [metric.key, metric]));
  return COORDINATION_METRIC_ORDER
    .map((key) => byKey.get(key))
    .filter(Boolean) as CoordinationMetric[];
}

function buildWeakConceptActionQueue(input: {
  weakConcepts?: WeakConceptSignal[] | null;
  weakSkills: WeakSkillSignal[];
  sourceHealth?: DashboardTodayDto["sourceHealth"] | null;
  hasStudyData: boolean;
  totalQuizzes: number;
}): WeakConceptQueueItem[] {
  const items: WeakConceptQueueItem[] = [];

  for (const concept of (input.weakConcepts ?? []).slice(0, 2)) {
    items.push({
      id: `concept-${concept.conceptKey || concept.label}`,
      title: concept.label || "Zayıf kavram",
      detail: concept.userSafeStatus || "Bu kavram daha fazla tekrar istiyor.",
      reason: concept.masteryProbability != null
        ? `Mastery tahmini %${Math.round(concept.masteryProbability * 100)}.`
        : "Son öğrenme sinyallerinde telafi ihtiyacı görünüyor.",
      actionLabel: "Tutor’da telafi et",
      actionView: "chat",
      tone: "weak",
    });
  }

  for (const skill of input.weakSkills.slice(0, Math.max(0, 3 - items.length))) {
    items.push({
      id: `skill-${skill.skillTag}-${skill.topicPath}`,
      title: skill.skillTag || "Zayıf beceri",
      detail: skill.topicPath || "Konu yolu henüz netleşmedi.",
      reason: `${skill.wrongCount}/${skill.totalCount} cevapta zorlanma var; doğruluk %${Math.round(skill.accuracy)}.`,
      actionLabel: "Wiki’den tekrar et",
      actionView: "wiki",
      tone: "weak",
    });
  }

  const sourceStatus = input.sourceHealth?.status?.toLowerCase();
  if (items.length < 4 && (sourceStatus === "unknown" || sourceStatus === "source_retrieval_empty")) {
    items.push({
      id: "source-health",
      title: "Kaynak kanıtını güçlendir",
      detail: input.sourceHealth?.userSafeDetail || "Kaynak ekledikçe Tutor ve Wiki cevapları daha güvenilir olur.",
      reason: input.sourceHealth?.userSafeLabel || "Kaynak durumu eksik görünüyor.",
      actionLabel: "Kaynakları kontrol et",
      actionView: "sources",
      tone: "source",
    });
  }

  if (items.length < 4 && input.hasStudyData && input.totalQuizzes === 0) {
    items.push({
      id: "quiz-evidence",
      title: "İlk quiz/pratik kanıtını oluştur",
      detail: "Quiz çözdükçe Orka zayıf kavramları daha net yakalar.",
      reason: "Henüz quiz kanıtı yok.",
      actionLabel: "Quiz/pratik çöz",
      actionView: "practice",
      tone: "practice",
    });
  }

  return items;
}

function deriveGuidance(today: DashboardTodayDto | null, fallback: {
  title: string;
  reason: string;
  actionLabel: string;
  actionView: string;
  hasStudyData: boolean;
  weakSkill?: string | null;
  activeLessonTitle?: string | null;
  totalQuizzes: number;
}) {
  if (!today && !fallback.hasStudyData) {
    return {
      title: "İlk dersi başlat",
      reason: "Henüz yeterli veri yok. Bir derse başlayarak Orka'nın öneri üretmesini sağlayabilirsin.",
      actionLabel: "Derse başla",
      actionView: "chat",
      evidence: "Veri bekleniyor",
    };
  }

  if ((today?.dueReviewCount ?? 0) > 0) {
    return {
      title: "Bekleyen tekrarları kapat",
      reason: `${today?.dueReviewCount} tekrar hazır. Önce bunları bitirmek yeni konudan daha verimli olur.`,
      actionLabel: "Tekrarları aç",
      actionView: "learning",
      evidence: "SRS kuyruğu",
    };
  }

  if (fallback.weakSkill) {
    return {
      title: "Zayıf kavramı toparla",
      reason: `${fallback.weakSkill} için son cevaplarda zayıf sinyal var. Tutor ile kısa telafi iyi olur.`,
      actionLabel: "Tutor ile toparla",
      actionView: "chat",
      evidence: "Quiz ve sinyal kanıtı",
    };
  }

  if (fallback.activeLessonTitle) {
    return {
      title: "Kaldığın dersten devam et",
      reason: `${fallback.activeLessonTitle} aktif plan içinde sıradaki güvenli dönüş noktası görünüyor.`,
      actionLabel: "Derse dön",
      actionView: "chat",
      evidence: "Aktif ders",
    };
  }

  if (fallback.hasStudyData && fallback.totalQuizzes === 0) {
    return {
      title: "Kısa quiz/pratik çöz",
      reason: "Henüz quiz kanıtı yok. Kısa bir pratik Orka'nın zayıf kavramları daha net yakalamasını sağlar.",
      actionLabel: "Pratiğe geç",
      actionView: "practice",
      evidence: "Quiz kanıtı bekleniyor",
    };
  }

  const sourceStatus = today?.sourceHealth?.status?.toLowerCase();
  if (sourceStatus === "unknown" || sourceStatus === "source_retrieval_empty") {
    return {
      title: "Kaynakları güçlendir",
      reason: today?.sourceHealth?.userSafeDetail || "Kaynak eklemek Wiki ve Tutor cevaplarını daha güvenli hale getirir.",
      actionLabel: "Kaynakları aç",
      actionView: "sources",
      evidence: "Kaynak sağlığı",
    };
  }

  if (today?.coordinationHealth?.overallStatus === "critical") {
    return {
      title: "Eksik kanıtları tamamla",
      reason: today.coordinationHealth.userSafeSummary || "Plan çalışıyor ama bazı kanıtlar eksik görünüyor.",
      actionLabel: today.nextAction?.label || "Devam et",
      actionView: today.nextAction?.view || "dashboard",
      evidence: "Koordinasyon özeti",
    };
  }

  return {
    title: today?.nextAction?.label || fallback.title,
    reason: today?.nextAction?.reason || today?.dailyFocusReason || fallback.reason,
    actionLabel: today?.nextAction?.label || fallback.actionLabel,
    actionView: today?.nextAction?.view || fallback.actionView,
    evidence: today?.nextAction?.userSafeStatus || "Hazır",
  };
}

function CoordinationHealthPanel({ health }: { health?: DashboardTodayDto["coordinationHealth"] | null }) {
  const metrics = pickCoordinationMetrics(health?.metrics);
  const empty = !health || metrics.length === 0 || health.overallStatus === "no_plan";

  return (
    <section className="mb-8 rounded-[1.5rem] border border-[#526d82]/12 bg-[#f7f9fa]/72 p-5 shadow-sm backdrop-blur-xl">
      <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="flex items-center gap-2 text-[10px] font-black uppercase tracking-[0.18em] text-[#52768a]">
            <ShieldCheck className="h-3.5 w-3.5" />
            Koordinasyon özeti
          </p>
          <h2 className="mt-1 text-base font-black text-[#172033]">
            {empty ? "Henüz yeterli öğrenme verisi yok" : health?.userSafeSummary || "Plan kanıtları izleniyor."}
          </h2>
        </div>
        {health?.windowDays ? (
          <span className="rounded-full bg-white/70 px-3 py-1 text-[10px] font-bold text-[#667085]">
            Son {health.windowDays} gun
          </span>
        ) : null}
      </div>

      {empty ? (
        <p className="rounded-2xl border border-dashed border-[#526d82]/16 bg-white/55 px-4 py-3 text-xs leading-6 text-[#667085]">
          Bir derse başlayıp kaynak, quiz veya Wiki kanıtı oluştukça Orka burada planın hangi kısımlarının hazır olduğunu gösterecek.
        </p>
      ) : (
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          {metrics.map((metric) => {
            const tone = metricTone(metric.status);
            return (
              <div key={metric.key} className={`rounded-2xl border px-4 py-3 ${metricToneClass(tone)}`}>
                <div className="mb-2 flex items-center justify-between gap-2">
                  <span className="inline-flex items-center gap-1.5 text-[11px] font-black">
                    {metricIcon(tone)}
                    {coordinationLabel(metric)}
                  </span>
                  {metric.total > 0 && (
                    <span className="rounded-full bg-white/58 px-2 py-0.5 text-[10px] font-bold">
                      {metric.count}/{metric.total}
                    </span>
                  )}
                </div>
                <p className="text-[11px] leading-5 opacity-85">{coordinationDetail(metric)}</p>
              </div>
            );
          })}
        </div>
      )}
    </section>
  );
}

function WeakConceptActionQueue({
  items,
  onViewChange,
}: {
  items: WeakConceptQueueItem[];
  onViewChange: (view: string) => void;
}) {
  const toneClasses = {
    weak: "border-[#e8c46f]/24 bg-[#fff8ee]/78",
    source: "border-[#9ec7d9]/28 bg-[#dcecf3]/52",
    practice: "border-[#8fb7a2]/26 bg-[#f2faf5]/78",
  } satisfies Record<WeakConceptQueueItem["tone"], string>;

  return (
    <div className="mb-5 rounded-2xl border border-[#526d82]/12 bg-white/52 p-4">
      <div className="mb-3 flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-[10px] font-black uppercase tracking-[0.16em] text-[#52768a]">
            Çalışma kuyruğu
          </p>
          <h3 className="mt-1 text-sm font-black text-[#172033]">Eksiklerini tamamla</h3>
        </div>
        <span className="rounded-full bg-[#eef1f3]/85 px-2.5 py-1 text-[10px] font-bold text-[#667085]">
          {items.length > 0 ? `${items.length} aksiyon` : "Veri bekleniyor"}
        </span>
      </div>

      {items.length === 0 ? (
        <p className="rounded-xl border border-dashed border-[#526d82]/16 bg-[#f7f9fa]/58 px-3 py-3 text-xs leading-6 text-[#667085]">
          Henüz zayıf kavram tespit edilmedi. Biraz daha quiz/chat kullandıkça Orka çalışma kuyruğunu oluşturur.
        </p>
      ) : (
        <div className="space-y-2.5">
          {items.map((item) => (
            <div key={item.id} className={`rounded-xl border px-3 py-3 ${toneClasses[item.tone]}`}>
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0 flex-1">
                  <p className="text-xs font-black text-[#172033]">{item.title}</p>
                  <p className="mt-1 text-[11px] leading-5 text-[#667085]">{item.detail}</p>
                  <p className="mt-1 text-[10px] font-bold text-[#8a641f]">{item.reason}</p>
                </div>
                <button
                  onClick={() => onViewChange(item.actionView)}
                  className="inline-flex shrink-0 items-center gap-1.5 rounded-lg bg-white/70 px-3 py-2 text-[11px] font-black text-[#172033] shadow-sm transition hover:bg-white focus:outline-none focus:ring-2 focus:ring-[#9ec7d9]"
                >
                  {item.actionLabel}
                  <ArrowRight className="h-3 w-3" />
                </button>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function ActiveLessonResumeCard({
  lessonTitle,
  parentTitle,
  onViewChange,
}: {
  lessonTitle?: string | null;
  parentTitle?: string | null;
  onViewChange: (view: string) => void;
}) {
  return (
    <div className="mt-4 rounded-xl border border-[#8fb7a2]/24 bg-[#f2faf5]/72 px-3 py-3">
      <p className="text-[10px] font-black uppercase tracking-[0.14em] text-[#47725d]">
        Kaldığın dersten devam et
      </p>
      {lessonTitle ? (
        <>
          <h3 className="mt-1 text-sm font-black text-[#172033]">{lessonTitle}</h3>
          <p className="mt-1 text-[11px] leading-5 text-[#667085]">
            {parentTitle ? `${parentTitle} içinde son çalıştığın ders.` : "Son çalıştığın ders burada hazır."}
          </p>
          <button
            onClick={() => onViewChange("chat")}
            className="mt-3 inline-flex items-center gap-2 rounded-lg bg-[#172033] px-3 py-2 text-[11px] font-black text-white transition hover:bg-[#243044] focus:outline-none focus:ring-2 focus:ring-[#9ec7d9]"
          >
            Derse dön
            <ArrowRight className="h-3 w-3" />
          </button>
        </>
      ) : (
        <p className="mt-1 text-[11px] leading-5 text-[#667085]">
          Aktif ders kanıtı oluşunca burada güvenli dönüş yolu görünecek.
        </p>
      )}
    </div>
  );
}

export default function DashboardPanel({ topics, onViewChange, mode = "today" }: DashboardPanelProps) {
  const { t } = useLanguage();
  const { attempts: sessionAttempts } = useQuizHistory(); // For local feedback
  // HUD yalnızca admin hesaplarda görünür — LLMOps verisi operasyon sırrıdır.
  const isAdmin = storage.getUser()?.isAdmin === true;
  const [activeTab, setActiveTab] = useState<"karne" | "hud">("karne");
  const [stats, setStats] = useState<ApiGlobalStats | null>(null);
  const [dashStats, setDashStats] = useState<ApiDashboardStats | null>(null);
  const [today, setToday] = useState<DashboardTodayDto | null>(null);
  const [gamification, setGamification] = useState<ApiGamification | null>(null);
  const [loading, setLoading] = useState(true);
  const [studyFocusPreference, setStudyFocusPreference] = useState(() => {
    return localStorage.getItem("orka_study_focus") || "general";
  });

  useEffect(() => {
    DashboardAPI.getToday()
      .then(res => setToday(res.data))
      .catch(err => console.error("Dashboard today fetch error:", err));

    // Quiz istatistikleri (doğruluk oranı, sparkline)
    QuizAPI.getGlobalStats()
      .then(res => setStats(res.data))
      .catch(err => console.error("Quiz stats fetch error:", err));

    // Gamification (seviye, xpToNextLevel, levelLabel)
    UserAPI.getGamification()
      .then(res => setGamification(res.data as ApiGamification))
      .catch(err => console.error("Gamification fetch error:", err));

    // Dashboard istatistikleri (XP, Streak, gerçek tamamlama verileri)
    DashboardAPI.getStats()
      .then(res => setDashStats(res.data as ApiDashboardStats))
      .catch(err => console.error("Dashboard stats fetch error:", err))
      .finally(() => setLoading(false));
  }, [sessionAttempts.length]); // Quiz tamamlandığında yenile

  const correctCount = stats?.correctAnswers ?? 0;
  const totalQuizzes = stats?.totalQuizzes ?? 0;
  const accuracy = stats?.accuracy ?? 0;

  const totalLessons = dashStats?.totalSections ?? topics.reduce(
    (sum, t) => sum + (t.totalSections ?? 0), 0
  );
  const completedLessons = dashStats?.completedSections ?? topics.reduce(
    (sum, t) => sum + (t.completedSections ?? 0), 0
  );

  // Gerçek değerler: DB'den gelen XP ve Streak
  const totalXP      = dashStats?.totalXP      ?? 0;
  const activeStreak = dashStats?.currentStreak ?? stats?.dailyProgress.filter(d => d.total > 0).length ?? 0;
  const learningSignalBook = dashStats?.learningSignalBook;
  const weakSkills = learningSignalBook?.weakSkills ?? [];
  const recentSignals = learningSignalBook?.recentSignals ?? [];
  const recentTopic = topics[0] ?? null;
  const nextTopic = topics.find((topic) => (topic.progressPercentage ?? 0) > 0 && (topic.progressPercentage ?? 0) < 100) ?? recentTopic;
  const strongestSignal = weakSkills[0] ?? null;
  const hasStudyData = topics.length > 0 || weakSkills.length > 0 || recentSignals.length > 0 || totalQuizzes > 0;
  const hasRealTopicProgress = topics.some((topic) =>
    (topic.progressPercentage ?? 0) > 0 ||
    (topic.completedSections ?? 0) > 0 ||
    ((topic.totalSections ?? 0) > 0 && (topic.completedSections ?? 0) > 0)
  );
  const studyFocusTitle = strongestSignal?.skillTag || nextTopic?.title || t("first_study_path");
  const studyFocusReason = strongestSignal
    ? `${strongestSignal.topicPath || "Bu konuda"} son denemelerde daha fazla tekrar istiyor.`
    : nextTopic
      ? `${nextTopic.title} kaldığın yerden devam etmeye hazır.`
      : t("no_fake_progress");
  const nextSmallStep = strongestSignal
    ? t("small_step_weak")
    : nextTopic
      ? t("small_step_topic")
      : t("small_step_first");
  const selectedFocus = STUDY_FOCUS_OPTIONS.find((item) => item.id === studyFocusPreference) ?? STUDY_FOCUS_OPTIONS[0];
  const todayFocusTitle = today?.dailyFocusTitle || studyFocusTitle;
  const todayFocusReason = today?.dailyFocusReason || studyFocusReason;
  const todayActionView = today?.nextAction?.view || "chat";
  const todayActionLabel = today?.nextAction?.label || t("continue_with_tutor");
  const sourceHealthLabel = today?.sourceHealth?.userSafeLabel || "Kaynak durumu ölçülüyor";
  const sourceHealthDetail = today?.sourceHealth?.userSafeDetail || "Kaynak ekledikçe Wiki ve Tutor daha güvenli cevap verir.";
  const activeLessonTopicId =
    today?.coordinationScope?.activeLessonTopicId ??
    today?.coordinationHealth?.activeLessonTopicId ??
    today?.nextAction?.topicId ??
    null;
  const activeLessonTopic = activeLessonTopicId ? topics.find((topic) => topic.id === activeLessonTopicId) : null;
  const activeLessonParent = activeLessonTopic?.parentTopicId
    ? topics.find((topic) => topic.id === activeLessonTopic.parentTopicId)
    : null;
  const activeLessonTitle = activeLessonTopic?.title ?? (activeLessonTopicId ? today?.activePlan?.title : null) ?? null;
  const activeLessonParentTitle = activeLessonParent?.title ?? today?.activePlan?.title ?? null;
  const weakConceptQueue = buildWeakConceptActionQueue({
    weakConcepts: today?.weakConcepts,
    weakSkills,
    sourceHealth: today?.sourceHealth,
    hasStudyData,
    totalQuizzes,
  });
  const todayGuidance = deriveGuidance(today, {
    title: todayFocusTitle,
    reason: todayFocusReason,
    actionLabel: todayActionLabel,
    actionView: todayActionView,
    hasStudyData,
    weakSkill: strongestSignal?.skillTag,
    activeLessonTitle,
    totalQuizzes,
  });

  const handleStudyFocusChange = (focusId: string) => {
    setStudyFocusPreference(focusId);
    localStorage.setItem("orka_study_focus", focusId);
  };

  return (
    <div className="flex-1 flex flex-col bg-transparent h-full overflow-hidden">

      {/* Tab Switcher */}
      <div className="flex-shrink-0 flex items-center gap-1 px-8 pt-6 pb-0">
        <button
          onClick={() => setActiveTab("karne")}
          className={`flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-bold transition-all ${
            activeTab === "karne"
              ? "bg-[#dcecf3]/85 text-[#172033] border border-[#9ec7d9]/45 shadow-sm"
              : "text-[#667085] hover:text-[#172033] border border-transparent"
          }`}
        >
          <Award className="w-3.5 h-3.5" />
          Öğrenme Karnesi
        </button>
        {isAdmin && (
          <button
            onClick={() => setActiveTab("hud")}
            className={`flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-bold transition-all ${
              activeTab === "hud"
                ? "bg-[#dcecf3]/85 text-[#172033] border border-[#9ec7d9]/45 shadow-sm"
                : "text-[#667085] hover:text-[#172033] border border-transparent"
            }`}
            title="Admin paneli — LLMOps İzleme"
          >
            <Cpu className="w-3.5 h-3.5" />
            Sistem Analitiği
            <span className="flex h-1.5 w-1.5 rounded-full bg-[#8fb7a2] animate-pulse" />
            <span className="ml-1 text-[9px] font-bold uppercase tracking-widest text-[#9a6b24]/80 border border-amber-500/30 bg-[#fff8ee] px-1.5 py-0.5 rounded">
              Admin
            </span>
          </button>
        )}
      </div>

      {/* Tab Content */}
      {activeTab === "hud" && isAdmin ? (
        <SystemHealthHUD />
      ) : (
      <div className="flex-1 overflow-y-auto">
        <div className="mx-auto w-full max-w-5xl px-8 py-10">
          
          {/* Header & Mastery Card */}
          <div className="mb-10 flex items-center justify-between gap-6">
            <WorkspaceHeader
              eyebrow={mode === "progress" ? "Evidence & Progress Workspace" : "Agent Command Center"}
              title={mode === "progress" ? "İlerleme" : "Bugün"}
              description={
                mode === "progress"
                  ? "Kavram kanıtı, kaynak sağlığı ve Tutor kararları sade bir ilerleme raporunda toplanır."
                  : "Orka bugün hangi adımı önerdiğini, nedenini ve hangi kanıta dayandığını burada gösterir."
              }
            />
            
            <div id="tour-global-stats" className="hidden sm:flex items-center gap-6 bg-[#f7f9fa]/68 border border-[#526d82]/14 backdrop-blur-xl px-6 py-4 rounded-2xl">
               <div className="text-right">
                  <p className="text-[10px] text-[#667085] uppercase font-bold tracking-tighter mb-0.5">Global Başarı</p>
                  <p className="text-xl font-mono font-bold text-[#47725d]">%{accuracy}</p>
               </div>
               {stats && <SuccessRateSparkline data={stats.dailyProgress} />}
            </div>
          </div>

          <SourceHealthStrip
            label={sourceHealthLabel}
            detail={sourceHealthDetail}
            status={today?.sourceHealth?.status}
            citationCoverage={today?.sourceHealth?.citationCoverage}
            unsupportedCitationCount={today?.sourceHealth?.unsupportedCitationCount}
          />

          <section className="mb-8 rounded-[1.75rem] border border-[#526d82]/12 bg-[#f7f4ec]/76 p-5 shadow-sm backdrop-blur-xl">
            <div className="grid gap-5 lg:grid-cols-[1.35fr_0.9fr]">
              <div>
                <div className="mb-3 inline-flex items-center gap-2 rounded-full border border-[#9ec7d9]/35 bg-[#dcecf3]/65 px-3 py-1 text-[10px] font-black uppercase tracking-[0.16em] text-[#2d5870]">
                  <Compass className="h-3.5 w-3.5" />
                  {t("daily_focus")}
                </div>
                <h2 className="text-xl font-black tracking-tight text-[#172033]">{todayFocusTitle}</h2>
                <p className="mt-2 max-w-2xl text-sm leading-6 text-[#5f6f7b]">{todayFocusReason}</p>
                <div className="mt-4 rounded-2xl border border-[#526d82]/12 bg-white/58 px-4 py-3">
                  <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                    <p className="text-[10px] font-black uppercase tracking-[0.16em] text-[#52768a]">
                      Sıradaki en iyi adım
                    </p>
                    <span className="rounded-full bg-[#eef1f3]/82 px-2.5 py-1 text-[10px] font-bold text-[#667085]">
                      {todayGuidance.evidence}
                    </span>
                  </div>
                  <h3 className="text-base font-black text-[#172033]">{todayGuidance.title}</h3>
                  <p className="mt-1 text-xs leading-5 text-[#667085]">{todayGuidance.reason}</p>
                  {today?.activePlan?.title && (
                    <p className="mt-2 text-[11px] font-bold text-[#52768a]">
                      Plan: {today.activePlan.title}
                    </p>
                  )}
                </div>
                {!hasStudyData && (
                  <p className="mt-3 rounded-2xl border border-dashed border-[#526d82]/16 bg-white/48 px-4 py-3 text-xs leading-6 text-[#667085]">
                    Henüz yeterli veri yok. Bir derse başlayarak Orka'nın öneri üretmesini sağlayabilirsin.
                  </p>
                )}
                <div className="mt-5 flex flex-wrap gap-2">
                  <button
                    onClick={() => onViewChange(todayGuidance.actionView)}
                    className="inline-flex items-center gap-2 rounded-xl bg-[#172033] px-4 py-2.5 text-xs font-black text-white shadow-sm transition hover:bg-[#243044] focus:outline-none focus:ring-2 focus:ring-[#9ec7d9]"
                  >
                    <MessageSquareText className="h-4 w-4" />
                    {todayGuidance.actionLabel}
                  </button>
                  <button
                    onClick={() => onViewChange("learning")}
                    className="inline-flex items-center gap-2 rounded-xl border border-[#526d82]/14 bg-white/58 px-4 py-2.5 text-xs font-black text-[#172033] transition hover:bg-[#f7f9fa] focus:outline-none focus:ring-2 focus:ring-[#9ec7d9]"
                  >
                    <Repeat2 className="h-4 w-4" />
                    {t("open_review_loop")}
                  </button>
                </div>
              </div>
              <div className="rounded-2xl border border-[#526d82]/12 bg-white/58 p-4">
                <p className="mb-2 flex items-center gap-2 text-[11px] font-black uppercase tracking-[0.16em] text-[#667085]">
                  <Lightbulb className="h-3.5 w-3.5 text-[#8a641f]" />
                  {t("next_small_step")}
                </p>
                <p className="text-sm font-bold leading-6 text-[#172033]">{nextSmallStep}</p>
                <div className="mt-4 rounded-xl border border-[#526d82]/12 bg-[#f7f9fa]/65 px-3 py-2">
                  <p className="text-[10px] font-black uppercase tracking-[0.14em] text-[#667085]">Kaynak sağlığı</p>
                  <p className="mt-1 text-xs font-bold text-[#172033]">{sourceHealthLabel}</p>
                  <p className="mt-1 text-[11px] leading-5 text-[#667085]">{sourceHealthDetail}</p>
                </div>
                <ActiveLessonResumeCard
                  lessonTitle={activeLessonTitle}
                  parentTitle={activeLessonParentTitle}
                  onViewChange={onViewChange}
                />
                <div className="mt-4 grid grid-cols-2 gap-2 text-[11px]">
                  <div className="rounded-xl bg-[#dcecf3]/55 px-3 py-2">
                    <span className="block text-base font-black text-[#172033]">{weakSkills.length}</span>
                    <span className="text-[#667085]">{t("weak_signal")}</span>
                  </div>
                  <div className="rounded-xl bg-[#fff8ee]/85 px-3 py-2">
                    <span className="block text-base font-black text-[#172033]">{topics.length}</span>
                    <span className="text-[#667085]">{t("study_path")}</span>
                  </div>
                </div>
              </div>
            </div>
            <div className="mt-5 rounded-2xl border border-[#526d82]/12 bg-white/45 p-4">
              <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
                <div>
                  <p className="flex items-center gap-2 text-[11px] font-black uppercase tracking-[0.16em] text-[#667085]">
                    <GraduationCap className="h-3.5 w-3.5 text-[#52768a]" />
                    {t("study_focus")}
                  </p>
                  <p className="mt-1 text-xs leading-5 text-[#667085]">
                    {t("study_focus_note")}
                  </p>
                </div>
                <span className="rounded-full bg-[#dcecf3]/70 px-3 py-1 text-[10px] font-bold text-[#2d5870]">
                  {t(selectedFocus.hintKey)}
                </span>
              </div>
              <div className="flex flex-wrap gap-2">
                {STUDY_FOCUS_OPTIONS.map((option) => (
                  <button
                    key={option.id}
                    onClick={() => handleStudyFocusChange(option.id)}
                    className={`rounded-xl border px-3 py-2 text-[11px] font-black transition focus:outline-none focus:ring-2 focus:ring-[#9ec7d9] ${
                      studyFocusPreference === option.id
                        ? "border-[#52768a]/35 bg-[#dcecf3]/76 text-[#172033]"
                        : "border-[#526d82]/12 bg-[#f7f9fa]/55 text-[#667085] hover:bg-white/70 hover:text-[#172033]"
                    }`}
                  >
                    {option.labelKey === "KPSS" || option.labelKey === "YKS" ? option.labelKey : t(option.labelKey)}
                  </button>
                ))}
              </div>
            </div>
          </section>

          <CoordinationHealthPanel health={today?.coordinationHealth} />

          {/* Core Stats Grid */}
          <div className="mb-10 grid grid-cols-2 gap-4 lg:grid-cols-4">
            <WorkspaceMetric label="Toplam XP" value={loading ? "—" : totalXP} detail={gamification ? `${gamification.levelLabel} · Seviye ${gamification.level}` : "kanıt puanı"} />
            <WorkspaceMetric label="Tamamlanan ders" value={loading ? "—" : (totalLessons > 0 ? `${completedLessons}/${totalLessons}` : topics.length)} detail="gerçek ilerleme" />
            <WorkspaceMetric label="Doğruluk oranı" value={loading ? "—" : `%${accuracy}`} detail={`${correctCount}/${totalQuizzes} cevap`} />
            <WorkspaceMetric label="Öğrenme serisi" value={loading ? "—" : activeStreak} detail={activeStreak > 1 ? `${activeStreak} günlük seri` : "devam sinyali"} />
          </div>

          <div className="mb-10 rounded-[1.75rem] border border-[#526d82]/12 bg-[#f7f9fa]/72 p-5 shadow-sm backdrop-blur-xl">
            <div className="mb-4 flex items-start justify-between gap-4">
              <div>
                <p className="text-[10px] font-black uppercase tracking-[0.18em] text-[#52768a]">
                  Öğrenci Sinyal Defteri
                </p>
                <h2 className="mt-1 text-base font-extrabold text-[#172033]">
                  {learningSignalBook?.summary || "Henüz belirgin zayıf beceri sinyali yok."}
                </h2>
              </div>
              <span className="rounded-full bg-[#dcecf3]/80 px-3 py-1 text-[10px] font-bold text-[#2d5870]">
                {learningSignalBook?.totalRecentAttempts ?? 0} son deneme
              </span>
            </div>

            <WeakConceptActionQueue items={weakConceptQueue} onViewChange={onViewChange} />

            <div className="grid gap-3 md:grid-cols-2">
              <div className="rounded-2xl bg-[#eef1f3]/70 p-4">
                <p className="mb-3 text-[11px] font-black uppercase tracking-[0.16em] text-[#667085]">
                  Zayıf beceriler
                </p>
                {weakSkills.length === 0 ? (
                  <p className="text-xs leading-6 text-[#667085]">
                    Quiz cevapları skill etiketiyle geldikçe burada kişisel telafi hedefleri oluşacak.
                  </p>
                ) : (
                  <div className="space-y-2">
                    {weakSkills.slice(0, 3).map((skill) => (
                      <div key={`${skill.skillTag}-${skill.topicPath}`} className="rounded-xl bg-[#f7f4ec]/78 px-3 py-2">
                        <div className="flex items-center justify-between gap-3">
                          <span className="text-xs font-bold text-[#172033]">{skill.skillTag || "unknown skill"}</span>
                          <span className="text-[10px] font-mono text-[#9a6b24]">%{Math.round(skill.accuracy)}</span>
                        </div>
                        <p className="mt-1 text-[11px] text-[#667085]">{skill.topicPath}</p>
                      </div>
                    ))}
                  </div>
                )}
              </div>

              <div className="rounded-2xl bg-[#fff8ee]/76 p-4">
                <p className="mb-3 text-[11px] font-black uppercase tracking-[0.16em] text-[#8a641f]">
                  Son öğrenme sinyalleri
                </p>
                {recentSignals.length === 0 ? (
                  <p className="text-xs leading-6 text-[#667085]">
                    “Anlamadım”, quiz cevabı, Wiki aksiyonu ve IDE çıktıları geldikçe ajan köprüsü burada görünür olur.
                  </p>
                ) : (
                  <div className="space-y-2">
                    {recentSignals.slice(0, 3).map((signal, index) => (
                      <div key={`${signal.signalType}-${index}`} className="flex items-center justify-between gap-3 rounded-xl bg-white/60 px-3 py-2">
                        <span className="text-xs font-semibold text-[#172033]">{signal.signalType}</span>
                        <span className="text-[10px] text-[#667085]">{signal.skillTag || signal.topicPath || "genel"}</span>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </div>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
            <div id="tour-course-progress" className="lg:col-span-2">
               <div className="flex items-center justify-between mb-6">
                <h2 className="text-sm font-bold text-[#172033] uppercase tracking-widest flex items-center gap-2">
                  <Activity className="w-4 h-4 text-[#667085]" />
                  Konu İlerlemesi
                </h2>
                <button
                  onClick={() => onViewChange("chat")}
                   className="text-[11px] font-bold text-[#667085] hover:text-[#344054] flex items-center gap-1 transition-colors uppercase tracking-wider"
                >
                  Çalışmaya geç
                  <ChevronRight className="w-3 h-3" />
                </button>
              </div>

              {topics.length === 0 || !hasRealTopicProgress ? (
                <div className="rounded-3xl border border-dashed border-[#526d82]/15 px-6 py-12 text-center">
                  <div className="mx-auto mb-4 grid h-10 w-10 place-items-center rounded-2xl bg-[#dcecf3]/65">
                    <FileText className="h-4 w-4 text-[#52768a]" />
                  </div>
                  <p className="text-sm font-bold text-[#172033]">
                    {topics.length === 0 ? "Henüz aktif bir öğrenme yolun bulunmuyor." : "Plan var; gerçek ilerleme henüz başlamadı."}
                  </p>
                  <p className="mx-auto mt-2 max-w-sm text-xs leading-6 text-[#667085]">
                    {topics.length === 0
                      ? "Tutor'a hedefini yaz; Orka ilk konu yolunu açsın. Kaynak, kod hatası, quiz ve tekrar sinyalleri geldikçe burası gerçek verilerle dolar."
                      : "Bu liste sahte %0 kartları basmaz. İlk ders, quiz, IDE sonucu veya tekrar aksiyonu geldikçe ilerleme burada gerçek veriye dönüşür."}
                  </p>
                  <button
                    onClick={() => onViewChange("chat")}
                    className="mt-5 inline-flex items-center gap-2 rounded-xl bg-[#172033] px-4 py-2.5 text-xs font-black text-white transition hover:bg-[#243044]"
                  >
                    <MessageSquareText className="h-4 w-4" />
                    İlk konuya başla
                  </button>
                </div>
              ) : (
                <div className="space-y-4">
                  {topics.slice(0, 4).map((topic) => {
                    const pct = topic.totalSections ? Math.round((topic.completedSections || 0) / topic.totalSections * 100) : 0;
                    return (
                      <div
                        key={topic.id}
                        className="p-5 rounded-2xl bg-[#f7f9fa]/66 border border-[#526d82]/12 backdrop-blur-xl hover:bg-[#f7f4ec]/50 transition-all cursor-pointer group"
                      >
                        <div className="flex items-center justify-between mb-4">
                          <div className="flex items-center gap-3">
                            <div className="w-10 h-10 rounded-xl bg-[#dcecf3]/55 flex items-center justify-center text-lg shadow-inner">
                              {topic.emoji}
                            </div>
                            <div>
                              <p className="text-sm font-semibold text-[#172033] group-hover:text-[#172033] transition-colors">{topic.title}</p>
                              <p className="text-[10px] text-[#98a2b3] uppercase font-bold tracking-tighter">{topic.category || 'GENEL'}</p>
                            </div>
                          </div>
                          <div className="text-right">
                             <p className="text-sm font-mono font-bold text-[#667085]">%{pct}</p>
                          </div>
                        </div>
                        <div className="w-full h-1 bg-[#dcecf3]/55 rounded-full overflow-hidden">
                           <div 
                             className="h-full bg-zinc-600 rounded-full transition-all duration-1000 group-hover:bg-zinc-400"
                             style={{ width: `${pct}%` }}
                           />
                        </div>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>

            <div className="space-y-6">
              <h2 className="text-sm font-bold text-[#172033] uppercase tracking-widest flex items-center gap-2">
                <Award className="w-4 h-4 text-[#667085]" />
                Hızlı Erişim
              </h2>
              
              <div className="grid grid-cols-1 gap-3">
                <button
                  onClick={() => onViewChange("chat")}
                  className="p-5 rounded-2xl bg-[#f7f9fa]/66 border border-[#526d82]/12 backdrop-blur-xl hover:border-zinc-600/50 transition-all text-left flex items-center justify-between group"
                >
                  <div className="flex flex-col">
                    <span className="text-xs font-bold text-[#172033] transition-colors">Öğrenmeye Devam</span>
                    <span className="text-[10px] text-[#667085]">En son kaldığın ders</span>
                  </div>
                  <div className="w-8 h-8 rounded-full bg-[#dcecf3]/70 flex items-center justify-center group-hover:bg-zinc-700 transition-colors">
                    <ArrowRight className="w-4 h-4 text-[#667085]" />
                  </div>
                </button>

                <button
                  onClick={() => onViewChange("practice")}
                  className="p-5 rounded-2xl bg-[#f7f9fa]/66 border border-[#526d82]/12 backdrop-blur-xl hover:border-zinc-600/50 transition-all text-left flex items-center justify-between group"
                >
                  <div className="flex flex-col">
                    <span className="text-xs font-bold text-[#172033]">Pratik yap</span>
                    <span className="text-[10px] text-[#667085]">Quiz veya IDE sonucunu Tutor'a bağla</span>
                  </div>
                  <div className="w-8 h-8 rounded-full bg-[#dcecf3]/70 flex items-center justify-center group-hover:bg-zinc-700 transition-colors">
                    <Code2 className="w-4 h-4 text-[#667085]" />
                  </div>
                </button>

                <button
                  id="tour-wiki-access"
                  onClick={() => onViewChange("sources")}
                  className="p-5 rounded-2xl bg-[#f7f9fa]/66 border border-[#526d82]/12 backdrop-blur-xl hover:border-zinc-600/50 transition-all text-left flex items-center justify-between group"
                >
                   <div className="flex flex-col">
                    <span className="text-xs font-bold text-[#172033]">Kaynakları aç</span>
                    <span className="text-[10px] text-[#667085]">Wiki ve OrkaLM kanıtlarını gör</span>
                  </div>
                  <div className="w-8 h-8 rounded-full bg-[#dcecf3]/70 flex items-center justify-center group-hover:bg-zinc-700 transition-colors">
                    <BookOpen className="w-4 h-4 text-[#667085]" />
                  </div>
                </button>
              </div>

              {/* Tips Section */}
              <div className="p-6 rounded-3xl bg-emerald-500/5 border border-emerald-500/10">
                 <h4 className="text-[10px] font-bold text-emerald-600 uppercase tracking-widest mb-2">Günlük İpucu</h4>
                 <p className="text-[11px] text-[#667085] leading-relaxed italic">
                   "Öğrenilenlerin %70'i ilk 24 saat içinde unutulur. Quizleri düzenli çözerek kalıcı hafızayı güçlendir."
                 </p>
              </div>
            </div>
          </div>
        </div>
      </div>
      )}
    </div>
  );
}
