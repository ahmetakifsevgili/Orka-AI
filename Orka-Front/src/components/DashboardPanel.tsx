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
import type { AdaptiveStudyPlanDto, AdaptiveStudyPlanRequestDto, ApiTopic, ApiGlobalStats, ApiDashboardStats, ApiGamification } from "@/lib/types";
import { evidenceQualityDetail, evidenceQualityLabel, evidenceQualityTone } from "@/lib/citationDisplay";
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
type LearningMemory = NonNullable<DashboardTodayDto["learningMemory"]>;
type AdaptiveStudyPlan = NonNullable<DashboardTodayDto["adaptiveStudyPlan"]>;
type HealthTone = "ok" | "warning" | "missing";
type GuidanceActionView = "chat" | "wiki" | "practice" | "sources" | "learning" | "dashboard";
type SourceCoverageTone = "ready" | "watch" | "empty";

interface WeakConceptQueueItem {
  id: string;
  title: string;
  detail: string;
  reason: string;
  actionLabel: string;
  actionView: GuidanceActionView;
  confidenceLabel?: string;
  tone: "weak" | "source" | "practice";
}

interface SourceCoverageCoachModel {
  title: string;
  detail: string;
  tone: SourceCoverageTone;
  primaryLabel: string;
  primaryView: GuidanceActionView;
  secondaryLabel?: string;
  secondaryView?: GuidanceActionView;
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

function confidenceStatusLabel(status?: string | null): string {
  const normalized = (status ?? "").toLowerCase();
  if (normalized === "usable") return "Kanıt güvenilir";
  if (normalized === "observed_only") return "Kanıt düşük";
  if (normalized === "ignored") return "Plan girdisi yapılmadı";
  return "Kanıt izleniyor";
}

function remediationAction(action?: string | null): { label: string; view: GuidanceActionView } {
  switch ((action ?? "").toLowerCase()) {
    case "wiki_review":
      return { label: "Wiki’den tekrar et", view: "wiki" };
    case "practice_quiz":
      return { label: "Benzer pratik çöz", view: "practice" };
    case "source_check":
      return { label: "Kaynakları kontrol et", view: "sources" };
    case "prerequisite_review":
      return { label: "Ön koşulu gözden geçir", view: "wiki" };
    case "tutor_explain":
    default:
      return { label: "Tutor’da telafi et", view: "chat" };
  }
}

function planAction(action?: string | null): { label: string; view: GuidanceActionView } {
  switch ((action ?? "").toLowerCase()) {
    case "wiki_review":
      return { label: "Wiki’den tekrar et", view: "wiki" };
    case "practice_quiz":
      return { label: "Pratik çöz", view: "practice" };
    case "source_check":
      return { label: "Kaynakları kontrol et", view: "sources" };
    case "prerequisite_review":
      return { label: "Ön koşulu tekrar et", view: "wiki" };
    case "diagnostic_check":
      return { label: "Kısa seviye tespiti yap", view: "practice" };
    case "continue_lesson":
      return { label: "Derse devam et", view: "chat" };
    default:
      return { label: "Tutor’da telafi et", view: "chat" };
  }
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
    const action = remediationAction(concept.remediationSeed?.firstAction);
    const signalLabel = concept.misconceptionSignal?.userSafeLabel ?? concept.remediationSeed?.userSafeMisconceptionLabel;
    items.push({
      id: `concept-${concept.conceptKey || concept.label}`,
      title: concept.label || "Zayıf kavram",
      detail: signalLabel
        ? `${concept.userSafeStatus || "Bu kavram daha fazla tekrar istiyor."} · ${signalLabel}`
        : concept.userSafeStatus || "Bu kavram daha fazla tekrar istiyor.",
      reason: concept.masteryProbability != null
        ? `Mastery tahmini %${Math.round(concept.masteryProbability * 100)}.`
        : concept.remediationSeed?.reason ?? "Son öğrenme sinyallerinde telafi ihtiyacı görünüyor.",
      actionLabel: action.label,
      actionView: action.view,
      confidenceLabel: confidenceStatusLabel(concept.learningSignalConfidence?.status ?? concept.remediationSeed?.confidenceStatus),
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

function deriveSourceCoverageCoach(sourceHealth?: DashboardTodayDto["sourceHealth"] | null): SourceCoverageCoachModel {
  if (!sourceHealth) {
    return {
      title: "Kaynak durumu için henüz yeterli veri yok.",
      detail: "Kaynak, Wiki veya RAG kanıtı oluştuğunda Orka bu konunun kapsamasını burada gösterecek.",
      tone: "empty",
      primaryLabel: "Kaynakları aç",
      primaryView: "sources",
      secondaryLabel: "Wiki’yi kontrol et",
      secondaryView: "wiki",
    };
  }

  if (sourceHealth.evidenceQuality) {
    const tone = evidenceQualityTone(sourceHealth.evidenceQuality);
    return {
      title: evidenceQualityLabel(sourceHealth.evidenceQuality),
      detail: evidenceQualityDetail(sourceHealth.evidenceQuality),
      tone,
      primaryLabel: tone === "ready" ? "Wiki’yi kontrol et" : "Kaynak ekle",
      primaryView: tone === "ready" ? "wiki" : "sources",
      secondaryLabel: "Quiz/pratikle kanıt oluştur",
      secondaryView: "practice",
    };
  }

  const status = (sourceHealth.status ?? "").toLowerCase();
  const citationCoverage = sourceHealth.citationCoverage ?? 0;
  const unsupportedCitationCount = sourceHealth.unsupportedCitationCount ?? 0;

  if (status === "healthy" || status === "ready") {
    return {
      title: "Bu konu için kaynaklar hazır.",
      detail: sourceHealth.userSafeDetail || "Kaynaklar Tutor, Wiki ve RAG cevaplarını destekleyecek durumda görünüyor.",
      tone: "ready",
      primaryLabel: "Wiki’yi kontrol et",
      primaryView: "wiki",
      secondaryLabel: "Quiz/pratikle kanıt oluştur",
      secondaryView: "practice",
    };
  }

  if (status === "source_retrieval_empty" || status === "unknown") {
    return {
      title: "Bu konuda kaynak eksik olabilir.",
      detail: sourceHealth.userSafeDetail || "RAG yanıtları için yeterli kaynak bulunamayabilir.",
      tone: "watch",
      primaryLabel: "Kaynak ekle",
      primaryView: "sources",
      secondaryLabel: "Wiki’yi kontrol et",
      secondaryView: "wiki",
    };
  }

  if (citationCoverage > 0 && (citationCoverage < 0.55 || unsupportedCitationCount > 0)) {
    return {
      title: "Kaynak kalitesi zayıf; yeni kaynak eklemek faydalı olabilir.",
      detail: `${Math.round(citationCoverage * 100)}% citation kapsamı ve ${unsupportedCitationCount} desteksiz citation görünüyor.`,
      tone: "watch",
      primaryLabel: "Kaynak ekle",
      primaryView: "sources",
      secondaryLabel: "Quiz/pratikle kanıt oluştur",
      secondaryView: "practice",
    };
  }

  return {
    title: sourceHealth.userSafeLabel || "Kaynak kapsaması izleniyor.",
    detail: sourceHealth.userSafeDetail || "Kaynak ve Wiki kanıtları geldikçe bu konu daha net değerlendirilecek.",
    tone: "empty",
    primaryLabel: "Kaynakları aç",
    primaryView: "sources",
    secondaryLabel: "Wiki’yi kontrol et",
    secondaryView: "wiki",
  };
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
                  {item.confidenceLabel ? (
                    <p className="mt-1 text-[10px] font-black uppercase tracking-[0.12em] text-[#8a641f]">
                      {item.confidenceLabel}
                    </p>
                  ) : null}
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

function SourceCoverageCoach({
  coach,
  onViewChange,
}: {
  coach: SourceCoverageCoachModel;
  onViewChange: (view: string) => void;
}) {
  const toneClass = {
    ready: "border-[#8fb7a2]/28 bg-[#f2faf5]/78 text-[#47725d]",
    watch: "border-[#e8c46f]/34 bg-[#fff8ee]/82 text-[#8a641f]",
    empty: "border-[#526d82]/12 bg-white/58 text-[#667085]",
  } satisfies Record<SourceCoverageTone, string>;

  return (
    <div className={`mb-8 rounded-2xl border p-4 shadow-sm ${toneClass[coach.tone]}`}>
      <div className="mb-3 flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-[10px] font-black uppercase tracking-[0.16em] opacity-80">
            Kaynak kapsaması
          </p>
          <h3 className="mt-1 text-sm font-black text-[#172033]">{coach.title}</h3>
          <p className="mt-1 text-xs leading-5 opacity-85">{coach.detail}</p>
        </div>
        <div className="flex shrink-0 flex-wrap gap-2">
          <button
            onClick={() => onViewChange(coach.primaryView)}
            className="inline-flex items-center gap-1.5 rounded-lg bg-[#172033] px-3 py-2 text-[11px] font-black text-white transition hover:bg-[#243044] focus:outline-none focus:ring-2 focus:ring-[#9ec7d9]"
          >
            {coach.primaryLabel}
            <ArrowRight className="h-3 w-3" />
          </button>
          {coach.secondaryLabel && coach.secondaryView ? (
            <button
              onClick={() => onViewChange(coach.secondaryView!)}
              className="inline-flex items-center gap-1.5 rounded-lg border border-[#526d82]/14 bg-white/68 px-3 py-2 text-[11px] font-black text-[#172033] transition hover:bg-white focus:outline-none focus:ring-2 focus:ring-[#9ec7d9]"
            >
              {coach.secondaryLabel}
            </button>
          ) : null}
        </div>
      </div>
    </div>
  );
}

function sourceReadinessLabel(status?: string | null): string {
  switch ((status ?? "").toLowerCase()) {
    case "ready":
      return "Kaynak zemini hazır";
    case "limited":
      return "Kaynak zemini sınırlı";
    case "weak":
      return "Kaynak zemini zayıf";
    case "missing":
      return "Kaynak kanıtı eksik";
    default:
      return "Kaynak zemini izleniyor";
  }
}

function StudentProfileSummary({ memory }: { memory?: LearningMemory | null }) {
  const strong = memory?.strongTopics?.[0];
  const weak = memory?.weakConcepts?.[0] ?? memory?.weakTopics?.[0];
  const uncertain = memory?.recentMisconceptions?.find((item) => item.confidenceStatus !== "usable");
  const remediation = memory?.remediationReadyItems?.[0];
  const hasMemory = Boolean(memory?.hasEnoughSignals || strong || weak || uncertain || remediation);

  return (
    <div className="mb-4 rounded-2xl border border-[#526d82]/12 bg-white/58 p-4">
      <div className="mb-3 flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="flex items-center gap-2 text-[10px] font-black uppercase tracking-[0.16em] text-[#52768a]">
            <ShieldCheck className="h-3.5 w-3.5" />
            Orka’nın öğrenci profili
          </p>
          <h3 className="mt-1 text-sm font-black text-[#172033]">
            {memory?.summary || "Henüz yeterli öğrenme sinyali yok."}
          </h3>
        </div>
        <span className="rounded-full bg-[#eef1f3]/85 px-2.5 py-1 text-[10px] font-bold text-[#667085]">
          {confidenceStatusLabel(memory?.confidenceStatus)}
        </span>
      </div>

      {!hasMemory ? (
        <p className="rounded-xl border border-dashed border-[#526d82]/16 bg-[#f7f9fa]/58 px-3 py-3 text-xs leading-6 text-[#667085]">
          Henüz yeterli öğrenme sinyali yok. Quiz, chat ve Wiki kullandıkça profil oluşur.
        </p>
      ) : (
        <div className="grid gap-2 md:grid-cols-2">
          <div className="rounded-xl bg-[#f2faf5]/76 px-3 py-3">
            <p className="text-[10px] font-black uppercase tracking-[0.14em] text-[#47725d]">Güçlü ilerlediğin alanlar</p>
            <p className="mt-1 text-xs font-bold text-[#172033]">{strong?.label || "Güçlü alan için daha fazla kanıt gerekiyor."}</p>
            <p className="mt-1 text-[11px] leading-5 text-[#667085]">{strong?.userSafeReason || memory?.confidenceSummary?.userSafeSummary}</p>
          </div>
          <div className="rounded-xl bg-[#fff8ee]/82 px-3 py-3">
            <p className="text-[10px] font-black uppercase tracking-[0.14em] text-[#8a641f]">Tekrar gerektiren alanlar</p>
            <p className="mt-1 text-xs font-bold text-[#172033]">{weak?.label || "Belirgin tekrar alanı yok."}</p>
            <p className="mt-1 text-[11px] leading-5 text-[#667085]">{weak?.userSafeReason || "Orka düşük güvenli sinyalleri kesin etiket gibi kullanmaz."}</p>
          </div>
          <div className="rounded-xl bg-[#eef1f3]/72 px-3 py-3">
            <p className="text-[10px] font-black uppercase tracking-[0.14em] text-[#667085]">Orka’nın emin olmadığı alanlar</p>
            <p className="mt-1 text-xs font-bold text-[#172033]">{uncertain?.label || sourceReadinessLabel(memory?.sourceReadiness)}</p>
            <p className="mt-1 text-[11px] leading-5 text-[#667085]">{uncertain?.userSafeReason || "Sinyal sınırlıysa profil önerisi de temkinli kalır."}</p>
          </div>
          <div className="rounded-xl bg-[#dcecf3]/58 px-3 py-3">
            <p className="text-[10px] font-black uppercase tracking-[0.14em] text-[#2d5870]">Önerilen telafi odağı</p>
            <p className="mt-1 text-xs font-bold text-[#172033]">{remediation?.label || memory?.goalReadiness?.suggestedDiagnosticFocus?.[0] || "Telafi odağı bekleniyor."}</p>
            <p className="mt-1 text-[11px] leading-5 text-[#667085]">{remediation?.userSafeReason || "Planner için güvenli öğrenme girdileri hazırlanıyor; bu bir çalışma planı değildir."}</p>
          </div>
        </div>
      )}
    </div>
  );
}

function levelLabel(level?: string | null): string {
  switch ((level ?? "").toLowerCase()) {
    case "advanced":
      return "İleri";
    case "intermediate":
      return "Orta";
    case "foundation":
    case "beginner":
      return "Başlangıç";
    default:
      return "Bilinmiyor";
  }
}

function goalModeLabel(goalType?: string | null): string {
  switch ((goalType ?? "").toLowerCase()) {
    case "exam":
      return "Sınav";
    case "career":
      return "Kariyer";
    default:
      return "Genel öğrenme";
  }
}

function AdaptiveStudyPlanCard({
  plan,
  onViewChange,
  onPreview,
  previewLoading,
  previewError,
}: {
  plan?: AdaptiveStudyPlan | null;
  onViewChange: (view: string) => void;
  onPreview: (request: AdaptiveStudyPlanRequestDto) => void;
  previewLoading: boolean;
  previewError?: string | null;
}) {
  const [goalType, setGoalType] = useState<AdaptiveStudyPlanRequestDto["goalType"]>("general_learning");
  const [weeklyAvailableMinutes, setWeeklyAvailableMinutes] = useState(180);
  const [currentLevel, setCurrentLevel] = useState("unknown");
  const [targetDate, setTargetDate] = useState("");
  const [examName, setExamName] = useState("KPSS");
  const [careerTarget, setCareerTarget] = useState("Backend Developer");

  const submitPreview = () => {
    onPreview({
      goalType,
      weeklyAvailableMinutes,
      currentLevel,
      targetDate: targetDate || null,
      examName: goalType === "exam" ? examName : null,
      careerTarget: goalType === "career" ? careerTarget : null,
      priorityTopicIds: [],
      prioritySkills: [],
    });
  };

  return (
    <section className="mb-8 rounded-[1.75rem] border border-[#526d82]/12 bg-[#f7f9fa]/72 p-5 shadow-sm backdrop-blur-xl">
      <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="flex items-center gap-2 text-[10px] font-black uppercase tracking-[0.18em] text-[#52768a]">
            <Compass className="h-3.5 w-3.5" />
            Çalışma planı
          </p>
          <h2 className="mt-1 text-base font-extrabold text-[#172033]">
            {plan?.summary || "Bugün için kısa ve güvenli bir çalışma rotası hazırlanıyor."}
          </h2>
          <p className="mt-1 text-xs leading-5 text-[#667085]">
            Bu plan mevcut konu ağına ve öğrenme sinyallerine göre hazırlanır; başarı veya puan garantisi vermez. İşe giriş garantisi değildir.
          </p>
        </div>
        <span className="rounded-full bg-[#dcecf3]/75 px-3 py-1 text-[10px] font-bold text-[#2d5870]">
          {plan?.windowDays ?? 7} günlük rota
        </span>
      </div>

      <div className="grid gap-3">
        {(plan?.items?.length ?? 0) === 0 ? (
          <div className="rounded-2xl border border-dashed border-[#526d82]/16 bg-white/58 px-4 py-3 text-xs leading-6 text-[#667085]">
            Plan üretmek için haftalık çalışma süresi ve hedef bilgisi yeterli olmalı.
          </div>
        ) : (
          plan!.items.slice(0, 5).map((item, index) => {
            const action = planAction(item.actionType);
            return (
              <div key={`${item.title}-${index}`} className="rounded-2xl border border-[#526d82]/12 bg-white/62 p-4">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <p className="text-[10px] font-black uppercase tracking-[0.14em] text-[#667085]">
                      {index + 1}. adım · {item.estimatedMinutes} dk · {confidenceStatusLabel(item.confidenceStatus)}
                    </p>
                    <h3 className="mt-1 text-sm font-black text-[#172033]">{item.title}</h3>
                  </div>
                  <button
                    onClick={() => onViewChange(action.view)}
                    className="inline-flex items-center gap-2 rounded-xl border border-[#526d82]/14 bg-[#f7f4ec]/78 px-3 py-2 text-[11px] font-black text-[#172033] transition hover:bg-white focus:outline-none focus:ring-2 focus:ring-[#9ec7d9]"
                  >
                    {action.label}
                    <ArrowRight className="h-3.5 w-3.5" />
                  </button>
                </div>
                <div className="mt-3 rounded-xl bg-[#eef1f3]/66 px-3 py-2">
                  <p className="text-[10px] font-black uppercase tracking-[0.14em] text-[#52768a]">Neden bu adım?</p>
                  <p className="mt-1 text-xs leading-5 text-[#667085]">{item.reason}</p>
                </div>
              </div>
            );
          })
        )}
      </div>

      {(plan?.warnings?.length ?? 0) > 0 && (
        <div className="mt-4 rounded-2xl border border-[#e8c46f]/30 bg-[#fff8ee]/76 px-4 py-3">
          <p className="mb-2 text-[10px] font-black uppercase tracking-[0.14em] text-[#8a641f]">Plan uyarıları</p>
          <ul className="space-y-1 text-xs leading-5 text-[#667085]">
            {plan!.warnings.slice(0, 3).map((warning) => (
              <li key={warning}>• {warning}</li>
            ))}
          </ul>
        </div>
      )}

      <div className="mt-4 grid gap-3 md:grid-cols-[0.95fr_1.25fr]">
        <div className="rounded-2xl border border-[#526d82]/12 bg-white/58 p-4">
          <p className="text-[10px] font-black uppercase tracking-[0.14em] text-[#667085]">Seviye sinyali</p>
          <p className="mt-2 text-xs leading-5 text-[#667085]">
            Beyan: <span className="font-bold text-[#172033]">{levelLabel(plan?.diagnostic?.intake?.selfDeclaredLevel)}</span> · Gözlenen:{" "}
            <span className="font-bold text-[#172033]">{levelLabel(plan?.diagnostic?.intake?.observedLevel)}</span>
          </p>
          <p className="mt-2 text-xs leading-5 text-[#667085]">
            {plan?.diagnostic?.shouldRunDiagnostic
              ? "Kısa seviye tespiti önerilir; kullanıcı beyanı tek gerçek kabul edilmez."
              : plan?.diagnostic?.userSafeReason || "Mevcut sinyaller plan için yeterli görünüyor."}
          </p>
        </div>

        <div className="rounded-2xl border border-[#526d82]/12 bg-white/58 p-4">
          <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
            <div>
              <p className="text-[10px] font-black uppercase tracking-[0.14em] text-[#52768a]">Hedef önizleme</p>
              <p className="mt-1 text-xs leading-5 text-[#667085]">
                {goalModeLabel(goalType)} modu kayıt yapmadan sadece plan önizlemesi üretir.
              </p>
            </div>
            <span className="rounded-full bg-[#eef1f3]/78 px-2.5 py-1 text-[10px] font-bold text-[#667085]">
              Persistence yok
            </span>
          </div>

          <div className="flex flex-wrap gap-2">
            {[
              ["general_learning", "Genel"],
              ["exam", "Sınav"],
              ["career", "Kariyer"],
            ].map(([value, label]) => (
              <button
                key={value}
                type="button"
                onClick={() => setGoalType(value as AdaptiveStudyPlanRequestDto["goalType"])}
                className={`rounded-xl border px-3 py-2 text-[11px] font-black transition focus:outline-none focus:ring-2 focus:ring-[#9ec7d9] ${
                  goalType === value
                    ? "border-[#52768a]/35 bg-[#dcecf3]/76 text-[#172033]"
                    : "border-[#526d82]/12 bg-[#f7f9fa]/55 text-[#667085] hover:bg-white/70 hover:text-[#172033]"
                }`}
              >
                {label}
              </button>
            ))}
          </div>

          <div className="mt-3 grid gap-2 sm:grid-cols-2">
            <label className="text-[11px] font-bold text-[#667085]">
              Haftalık süre
              <input
                type="number"
                min={0}
                value={weeklyAvailableMinutes}
                onChange={(event) => setWeeklyAvailableMinutes(Number(event.target.value))}
                className="mt-1 w-full rounded-xl border border-[#526d82]/14 bg-white/80 px-3 py-2 text-xs font-bold text-[#172033] outline-none focus:ring-2 focus:ring-[#9ec7d9]"
              />
            </label>
            <label className="text-[11px] font-bold text-[#667085]">
              Mevcut seviye
              <select
                value={currentLevel}
                onChange={(event) => setCurrentLevel(event.target.value)}
                className="mt-1 w-full rounded-xl border border-[#526d82]/14 bg-white/80 px-3 py-2 text-xs font-bold text-[#172033] outline-none focus:ring-2 focus:ring-[#9ec7d9]"
              >
                <option value="unknown">Emin değilim</option>
                <option value="foundation">Başlangıç</option>
                <option value="intermediate">Orta</option>
                <option value="advanced">İleri</option>
              </select>
            </label>
            <label className="text-[11px] font-bold text-[#667085]">
              Hedef tarih
              <input
                type="date"
                value={targetDate}
                onChange={(event) => setTargetDate(event.target.value)}
                className="mt-1 w-full rounded-xl border border-[#526d82]/14 bg-white/80 px-3 py-2 text-xs font-bold text-[#172033] outline-none focus:ring-2 focus:ring-[#9ec7d9]"
              />
            </label>
            {goalType === "exam" && (
              <label className="text-[11px] font-bold text-[#667085]">
                Sınav adı
                <input
                  value={examName}
                  onChange={(event) => setExamName(event.target.value)}
                  className="mt-1 w-full rounded-xl border border-[#526d82]/14 bg-white/80 px-3 py-2 text-xs font-bold text-[#172033] outline-none focus:ring-2 focus:ring-[#9ec7d9]"
                />
              </label>
            )}
            {goalType === "career" && (
              <label className="text-[11px] font-bold text-[#667085]">
                Kariyer hedefi
                <input
                  value={careerTarget}
                  onChange={(event) => setCareerTarget(event.target.value)}
                  className="mt-1 w-full rounded-xl border border-[#526d82]/14 bg-white/80 px-3 py-2 text-xs font-bold text-[#172033] outline-none focus:ring-2 focus:ring-[#9ec7d9]"
                />
              </label>
            )}
          </div>

          <div className="mt-3 flex flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={submitPreview}
              disabled={previewLoading}
              className="inline-flex items-center gap-2 rounded-xl bg-[#172033] px-4 py-2.5 text-xs font-black text-white shadow-sm transition hover:bg-[#243044] disabled:opacity-55"
            >
              {previewLoading ? "Plan hazırlanıyor" : "Planı hedefe göre güncelle"}
            </button>
            {previewError && <span className="text-xs font-bold text-[#8a641f]">{previewError}</span>}
          </div>
        </div>
      </div>
    </section>
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
  const [customPlan, setCustomPlan] = useState<AdaptiveStudyPlanDto | null>(null);
  const [planPreviewLoading, setPlanPreviewLoading] = useState(false);
  const [planPreviewError, setPlanPreviewError] = useState<string | null>(null);
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
  const sourceCoverageCoach = deriveSourceCoverageCoach(today?.sourceHealth);
  const learningMemory = today?.learningMemory ?? null;
  const adaptiveStudyPlan = customPlan ?? today?.adaptiveStudyPlan ?? null;
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

  const handleAdaptivePlanPreview = async (request: AdaptiveStudyPlanRequestDto) => {
    setPlanPreviewLoading(true);
    setPlanPreviewError(null);
    try {
      const plan = await DashboardAPI.previewAdaptiveStudyPlan(request);
      setCustomPlan(plan);
    } catch {
      setPlanPreviewError("Plan önizlemesi alınamadı. Lütfen tekrar dene.");
    } finally {
      setPlanPreviewLoading(false);
    }
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
          <SourceCoverageCoach coach={sourceCoverageCoach} onViewChange={onViewChange} />

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

          <AdaptiveStudyPlanCard
            plan={adaptiveStudyPlan}
            onViewChange={onViewChange}
            onPreview={handleAdaptivePlanPreview}
            previewLoading={planPreviewLoading}
            previewError={planPreviewError}
          />

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

            <StudentProfileSummary memory={learningMemory} />

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
