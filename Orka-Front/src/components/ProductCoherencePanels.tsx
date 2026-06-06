import { lazy, Suspense, useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import {
  AlertTriangle,
  ArrowRight,
  BookOpen,
  BrainCircuit,
  CheckCircle2,
  ClipboardCheck,
  Code2,
  FileText,
  GraduationCap,
  Layers,
  Loader2,
  MessageSquare,
  Network,
  Play,
  RefreshCw,
  ShieldCheck,
  Sparkles,
} from "lucide-react";
import toast from "react-hot-toast";
import { CentralExamsAPI, ClassroomAPI, CodeAPI, DashboardAPI, LearningAPI, NotebookStudioAPI, SourcesAPI } from "@/services/api";
import type { DashboardTodayDto } from "@/services/api";
import type {
  ApiTopic,
  CodeLearningActionDto,
  CodeLearningHandoffDto,
  CodeLearningWarningDto,
  ExamWarRoomActionDto,
  ExamWarRoomWarningDto,
  NotebookStudioPackActionDto,
  NotebookStudioPackWarningDto,
  OrkaCodeLearningIdeDto,
  OrkaExamWarRoomDto,
  OrkaLearningContractQuery,
  OrkaLearningStateDto,
  OrkaLearningStateConflictDto,
  OrkaMissionActionDto,
  OrkaMissionControlDto,
  OrkaMissionModuleCardDto,
  OrkaMissionWarningDto,
  OrkaNotebookStudioProDto,
  OrkaSourceWikiProDto,
  OrkaStudyCoachDto,
  OrkaStudyRoomActionDto,
  OrkaStudyRoomDto,
  OrkaStudyRoomWarningDto,
  SourceWikiProActionDto,
  SourceWikiProWarningDto,
} from "@/lib/types";

const InteractiveIDE = lazy(() => import("./InteractiveIDE"));

type ProductView =
  | "home"
  | "tutor"
  | "study-room"
  | "review"
  | "exams"
  | "sources-wiki"
  | "notebook"
  | "code"
  | "progress"
  | "settings";

type BasePanelProps = {
  activeTopic: ApiTopic | null;
  sessionId: string | null;
  onViewChange: (view: string) => void;
  workspace?: ReactNode;
};

type HomePanelProps = BasePanelProps & {
  topics: ApiTopic[];
};

type ActionLike = {
  actionType?: string | null;
  handoffType?: string | null;
  label?: string | null;
  reason?: string | null;
  priority?: string | null;
  entryPoint?: string | null;
  targetRoute?: string | null;
  reasonCodes?: string[];
};

type WarningLike = {
  warningCode?: string | null;
  severity?: string | null;
  label?: string | null;
  targetRoute?: string | null;
  reasonCodes?: string[];
};

type LoadState<T> = {
  loading: boolean;
  data: T | null;
  error: boolean;
};

const EMPTY_STATE = "Orka bu yüzeyi kişiselleştirmek için biraz daha gerçek çalışma sinyaline ihtiyaç duyuyor.";

function safeList<T>(items?: T[] | null): T[] {
  return Array.isArray(items) ? items : [];
}

const moduleViewMap: Record<string, ProductView> = {
  home: "home",
  dashboard: "home",
  chat: "tutor",
  tutor: "tutor",
  ask_tutor: "tutor",
  classroom: "study-room",
  study_room: "study-room",
  open_study_room: "study-room",
  review: "review",
  quiz: "review",
  practice: "review",
  checkpoint: "review",
  central_exams: "exams",
  "central-exams": "exams",
  exams: "exams",
  exam: "exams",
  sources: "sources-wiki",
  wiki: "sources-wiki",
  source_wiki: "sources-wiki",
  "sources-wiki": "sources-wiki",
  notebook: "notebook",
  notebook_studio: "notebook",
  "notebook-studio": "notebook",
  code: "code",
  ide: "code",
  "code-learning": "code",
  progress: "progress",
  memory: "progress",
  settings: "settings",
};

const moduleIconMap: Record<ProductView, typeof MessageSquare> = {
  home: Sparkles,
  tutor: MessageSquare,
  "study-room": GraduationCap,
  review: ClipboardCheck,
  exams: GraduationCap,
  "sources-wiki": Network,
  notebook: FileText,
  code: Code2,
  progress: BrainCircuit,
  settings: ShieldCheck,
};

function compactParams(activeTopic: ApiTopic | null, sessionId: string | null, extras: Partial<OrkaLearningContractQuery> = {}): OrkaLearningContractQuery {
  return {
    ...(activeTopic?.id ? { topicId: activeTopic.id } : {}),
    ...(sessionId ? { sessionId } : {}),
    ...extras,
  };
}

function normalizeKey(value?: string | null) {
  return (value ?? "").trim().toLowerCase().replace(/\s+/g, "_");
}

function labelize(value?: string | null) {
  const key = normalizeKey(value);
  const labels: Record<string, string> = {
    thin_evidence: "Kanıt zayıf",
    thin_exam_evidence: "Sınav kanıtı zayıf",
    limited: "Sınırlı",
    unknown: "Ölçülmedi",
    ready: "Hazır",
    watch: "Dikkat",
    warning: "Dikkat",
    blocked: "Engelli",
    source_needed: "Kaynak gerekiyor",
    not_ready_yet: "Henüz hazır değil",
    evidence_insufficient: "Kanıt yetersiz",
    source_limited: "Kaynak sınırlı",
    source_grounded: "Kaynaklı",
    wiki_ready: "Wiki hazır",
    preview_ready: "Önizleme hazır",
    preview_only: "Önizleme",
    review: "Tekrar",
    repair: "Onarım",
    exam: "Sınav",
    sources_wiki: "Kaynak / Wiki",
    evidence: "Kanıt",
  };
  if (labels[key]) return labels[key];
  const cleaned = (value ?? "").replace(/[_-]+/g, " ").trim();
  return cleaned ? cleaned.replace(/\b\w/g, (char) => char.toUpperCase()) : "Not ready yet";
}

function toView(targetRoute?: string | null, entryPoint?: string | null, actionType?: string | null): ProductView {
  const keys = [targetRoute, entryPoint, actionType].map(normalizeKey);
  for (const key of keys) {
    if (key && moduleViewMap[key]) return moduleViewMap[key];
  }
  if (keys.some((key) => key.includes("study_room"))) return "study-room";
  if (keys.some((key) => key.includes("exam") || key.includes("deneme"))) return "exams";
  if (keys.some((key) => key.includes("source") || key.includes("wiki") || key.includes("citation"))) return "sources-wiki";
  if (keys.some((key) => key.includes("notebook") || key.includes("artifact") || key.includes("pack"))) return "notebook";
  if (keys.some((key) => key.includes("code") || key.includes("runtime"))) return "code";
  if (keys.some((key) => key.includes("review") || key.includes("quiz") || key.includes("checkpoint"))) return "review";
  if (keys.some((key) => key.includes("progress") || key.includes("memory"))) return "progress";
  return "tutor";
}

function statusTone(status?: string | null) {
  const key = normalizeKey(status);
  if (key.includes("blocked") || key.includes("deleted") || key.includes("failed")) {
    return "border-[#ff7b7b]/25 bg-[#ff7b7b]/10 text-[#ffb0b0]";
  }
  if (key.includes("warning") || key.includes("limited") || key.includes("thin") || key.includes("stale") || key.includes("degraded")) {
    return "border-[#dac17a]/30 bg-[#dac17a]/10 text-[#e6d49b]";
  }
  if (key.includes("ready") || key.includes("stable") || key.includes("clean") || key.includes("passed")) {
    return "border-[#a7e879]/25 bg-[#a7e879]/10 text-[#c9f5a9]";
  }
  return "border-white/[0.1] bg-white/[0.045] text-[#aeb6b2]";
}

function priorityTone(priority?: string | null) {
  const key = normalizeKey(priority);
  if (key.includes("urgent") || key.includes("high")) return "border-[#ff7b7b]/25 bg-[#ff7b7b]/10 text-[#ffb0b0]";
  if (key.includes("medium") || key.includes("normal")) return "border-[#dac17a]/30 bg-[#dac17a]/10 text-[#e6d49b]";
  return "border-white/[0.1] bg-white/[0.045] text-[#aeb6b2]";
}

function missionModuleDisplay(card: OrkaMissionModuleCardDto, view: ProductView) {
  const fallbackSummary = card.userSafeSummary || "Orka bu modu mevcut öğrenme bağlamına göre açar.";
  const map: Partial<Record<ProductView, { label: string; summary: string }>> = {
    tutor: { label: "Tutor", summary: "Bir fikri açıkla, onar veya hızlı kontrol et." },
    "study-room": { label: "Study Room", summary: "Chat yorarsa sınıf hissinde kısa ders anlatımı, örnek ve kontrol akışı aç." },
    review: { label: "Review / Quiz", summary: "Mini quiz, tekrar ve küçük kontrol döngüsünü aç." },
    exams: { label: "Exam War Room", summary: "Zayıf sınav çıktısını vaat üretmeden çalış." },
    "sources-wiki": { label: "Sources / Wiki", summary: "Kaynak, wiki notu ve citation dayanağını birlikte gör." },
    notebook: { label: "Notebook Studio", summary: "Kaynaklı özet, quiz veya sesli çıktıyı bağlam içinde üret." },
    code: { label: "Code IDE", summary: "Kodu çalıştır, hatayı gör ve Tutor'a bağla." },
    progress: { label: "İlerleme", summary: "Zayıf kavram, bellek ve sıradaki güvenli adımı gör." },
    home: { label: "Ana Kokpit", summary: "Bugünün en doğru çalışma adımına dön." },
  };
  return map[view] ?? { label: card.label || labelize(card.moduleKey), summary: fallbackSummary };
}

function StatusPill({ label, tone }: { label?: string | null; tone?: "status" | "priority" }) {
  return (
    <span className={`inline-flex max-w-full items-center rounded-md border px-2 py-1 text-[11px] font-bold ${tone === "priority" ? priorityTone(label) : statusTone(label)}`}>
      <span className="truncate">{labelize(label)}</span>
    </span>
  );
}

function ReasonChips({ items, limit = 4 }: { items?: string[]; limit?: number }) {
  const visible = (items ?? []).filter(Boolean).slice(0, limit);
  if (visible.length === 0) return null;
  return (
    <div className="flex flex-wrap gap-1.5">
      {visible.map((item) => (
        <span key={item} className="rounded-md border border-white/[0.08] bg-white/[0.045] px-2 py-1 text-[10px] font-semibold text-[#8f9894]">
          {item}
        </span>
      ))}
    </div>
  );
}

/* Per-module accent palette */
const MODULE_ACCENT: Record<string, { color: string; bg: string; border: string }> = {
  home:          { color: "#6ed7ce", bg: "rgba(110,215,206,0.07)", border: "rgba(110,215,206,0.15)" },
  tutor:         { color: "#6ed7ce", bg: "rgba(110,215,206,0.07)", border: "rgba(110,215,206,0.15)" },
  "study-room":  { color: "#a7e879", bg: "rgba(167,232,121,0.07)", border: "rgba(167,232,121,0.15)" },
  review:        { color: "#b4a0f0", bg: "rgba(180,160,240,0.07)", border: "rgba(180,160,240,0.15)" },
  exams:         { color: "#dac17a", bg: "rgba(218,193,122,0.07)", border: "rgba(218,193,122,0.15)" },
  "sources-wiki":{ color: "#6ed7ce", bg: "rgba(110,215,206,0.07)", border: "rgba(110,215,206,0.15)" },
  notebook:      { color: "#a7e879", bg: "rgba(167,232,121,0.07)", border: "rgba(167,232,121,0.15)" },
  code:          { color: "#b4a0f0", bg: "rgba(180,160,240,0.07)", border: "rgba(180,160,240,0.15)" },
  progress:      { color: "#a7e879", bg: "rgba(167,232,121,0.07)", border: "rgba(167,232,121,0.15)" },
  default:       { color: "#6ed7ce", bg: "rgba(110,215,206,0.07)", border: "rgba(110,215,206,0.15)" },
};

function PanelScaffold({
  title,
  eyebrow,
  icon: Icon,
  summary,
  children,
  aside,
  moduleKey = "default",
}: {
  title: string;
  eyebrow: string;
  icon: typeof MessageSquare;
  summary?: string | null;
  children: ReactNode;
  aside?: ReactNode;
  moduleKey?: string;
}) {
  const accent = MODULE_ACCENT[moduleKey] ?? MODULE_ACCENT.default;
  return (
    <div className="flex h-full flex-col overflow-y-auto" style={{ background: "#07090a", color: "#f4f6f3" }}>
      {/* Module header with accent top border */}
      <header
        className="shrink-0 border-b px-6 py-5"
        style={{
          borderBottomColor: "rgba(255,255,255,0.07)",
          background: "rgba(10,12,14,0.95)",
          borderTop: `2px solid ${accent.color}`,
        }}
      >
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div className="min-w-0">
            <div
              className="mb-2 flex items-center gap-2 text-[11px] font-semibold uppercase tracking-[0.18em]"
              style={{ color: accent.color }}
            >
              <span
                className="inline-flex h-5 w-5 items-center justify-center rounded-md"
                style={{ background: accent.bg, border: `1px solid ${accent.border}` }}
              >
                <Icon className="h-3 w-3" />
              </span>
              {eyebrow}
            </div>
            <h1 className="text-[24px] font-bold tracking-tight text-white">{title}</h1>
            {summary && <p className="mt-2 max-w-3xl text-[13px] leading-6" style={{ color: "#8f9894" }}>{summary}</p>}
          </div>
          {aside && <div className="shrink-0">{aside}</div>}
        </div>
      </header>
      <main className="flex-1 overflow-y-auto">
        <div className="grid gap-4 px-6 py-5">{children}</div>
      </main>
    </div>
  );
}

function LoadingBlock({ label = "Orka çalışma yüzeyini hazırlıyor" }: { label?: string }) {
  return (
    <div className="flex min-h-[180px] items-center justify-center rounded-xl border border-white/[0.08] bg-white/[0.035]">
      <div className="flex items-center gap-2 text-sm font-semibold text-[#9aa3a0]">
        <Loader2 className="h-4 w-4 animate-spin" />
        {label}
      </div>
    </div>
  );
}

function EmptyBlock({ title = "Henüz yeterli sinyal yok", detail = EMPTY_STATE }: { title?: string; detail?: string }) {
  return (
    <div className="rounded-xl border border-[#dac17a]/25 bg-[#dac17a]/10 p-4">
      <div className="flex items-start gap-3">
        <AlertTriangle className="mt-0.5 h-4 w-4 text-[#dac17a]" />
        <div>
          <h3 className="text-sm font-semibold text-[#f4f6f3]">{title}</h3>
          <p className="mt-1 text-sm leading-6 text-[#9aa3a0]">{detail}</p>
        </div>
      </div>
    </div>
  );
}

function MetricGrid({ items }: { items: Array<{ label: string; value?: string | number | null; detail?: string | null }> }) {
  return (
    <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
      {items.map((item) => (
        <div key={item.label} className="rounded-xl border border-white/[0.08] bg-white/[0.04] p-3">
          <div className="text-[11px] font-semibold uppercase tracking-[0.14em] text-[#8f9894]">{item.label}</div>
          <div className="mt-2 text-lg font-semibold text-[#f4f6f3]">{item.value ?? "yok"}</div>
          {item.detail && <div className="mt-1 text-xs leading-5 text-[#9aa3a0]">{item.detail}</div>}
        </div>
      ))}
    </div>
  );
}

function WarningList({ warnings, title = "Uyarılar" }: { warnings: WarningLike[] | null | undefined; title?: string }) {
  const visible = safeList(warnings).filter((warning) => warning.label || warning.warningCode);
  if (visible.length === 0) return null;
  return (
    <section className="rounded-xl border border-[#ff7b7b]/20 bg-[#ff7b7b]/10 p-4">
      <h3 className="flex items-center gap-2 text-sm font-semibold text-[#ffb0b0]">
        <AlertTriangle className="h-4 w-4" />
        {title}
      </h3>
      <div className="mt-3 grid gap-2">
        {visible.slice(0, 5).map((warning, index) => (
          <div key={`${warning.warningCode ?? "warning"}-${index}`} className="rounded-lg border border-white/[0.08] bg-white/[0.04] p-3">
            <div className="flex flex-wrap items-center gap-2">
              <StatusPill label={warning.severity ?? "warning"} />
              <span className="text-sm font-semibold text-[#f4f6f3]">{warning.label || labelize(warning.warningCode)}</span>
            </div>
            <ReasonChips items={warning.reasonCodes} />
          </div>
        ))}
      </div>
    </section>
  );
}

function ActionButton({ action, onViewChange, compact = false }: { action: ActionLike; onViewChange: (view: string) => void; compact?: boolean }) {
  const view = toView(action.targetRoute, action.entryPoint, action.actionType ?? action.handoffType);
  return (
    <button
      type="button"
      onClick={() => onViewChange(view)}
      className={`inline-flex items-center justify-between gap-2 rounded-xl border border-white/[0.08] bg-white/[0.04] px-3 py-2 text-left text-sm font-semibold text-[#f4f6f3] shadow-sm transition hover:border-[#6ed7ce]/35 hover:bg-white/[0.065] ${
        compact ? "min-w-[160px]" : "w-full"
      }`}
    >
      <span className="min-w-0">
        <span className="block truncate">{action.label || labelize(action.actionType ?? action.handoffType)}</span>
        {!compact && action.reason && <span className="mt-0.5 block text-xs font-medium leading-5 text-[#9aa3a0]">{action.reason}</span>}
      </span>
      <ArrowRight className="h-4 w-4 shrink-0 text-[#8f9894]" />
    </button>
  );
}

function ActionList({ actions, onViewChange, title = "Önerilen adımlar" }: { actions: ActionLike[] | null | undefined; onViewChange: (view: string) => void; title?: string }) {
  const visible = safeList(actions).filter((action) => action.label || action.actionType || action.handoffType);
  if (visible.length === 0) return <EmptyBlock title="Sırada zorunlu adım yok" detail="Bu yüzey için acil bir aksiyon görünmüyor." />;
  return (
    <section className="rounded-xl border border-white/[0.08] bg-white/[0.035] p-4">
      <h3 className="mb-3 text-sm font-semibold text-[#f4f6f3]">{title}</h3>
      <div className="grid gap-2 lg:grid-cols-2">
        {visible.slice(0, 6).map((action, index) => (
          <ActionButton key={`${action.actionType ?? action.handoffType ?? "action"}-${index}`} action={action} onViewChange={onViewChange} />
        ))}
      </div>
    </section>
  );
}

function ModuleCardGrid({ cards, onViewChange }: { cards: OrkaMissionModuleCardDto[]; onViewChange: (view: string) => void }) {
  const fallback: OrkaMissionModuleCardDto[] = [
    { moduleKey: "tutor", label: "Tutor", status: "ready", entryPoint: "ask_tutor", targetRoute: "chat", priority: "normal", userSafeSummary: "Ask for explanation, examples, or a quick check without leaving the learning flow.", actionCount: 1, warningCount: 0, reasonCodes: [] },
    { moduleKey: "study_room", label: "Study Room", status: "limited", entryPoint: "open_study_room", targetRoute: "classroom", priority: "normal", userSafeSummary: "Chatten sıkıldığında sınıf ortamı gibi akan kısa ders anlatımı.", actionCount: 1, warningCount: 0, reasonCodes: [] },
    { moduleKey: "review", label: "Review / Quiz", status: "ready", entryPoint: "review_due_concept", targetRoute: "review", priority: "normal", userSafeSummary: "Collect due review, micro questions, and short practice into one loop.", actionCount: 1, warningCount: 0, reasonCodes: [] },
    { moduleKey: "exam", label: "Exam War Room", status: "limited", entryPoint: "practice_exam_outcome", targetRoute: "central-exams", priority: "normal", userSafeSummary: "Inspect weak outcomes and practice handoffs without score promises.", actionCount: 1, warningCount: 0, reasonCodes: [] },
    { moduleKey: "sources", label: "Sources / Wiki", status: "limited", entryPoint: "source_review", targetRoute: "sources", priority: "normal", userSafeSummary: "Review source readiness, Wiki links, citations, and repair needs.", actionCount: 1, warningCount: 0, reasonCodes: [] },
    { moduleKey: "notebook", label: "Notebook Studio", status: "limited", entryPoint: "open_notebook_pack", targetRoute: "notebook-studio", priority: "low", userSafeSummary: "Create evidence-backed study artifacts only after a source or lesson context exists.", actionCount: 1, warningCount: 0, reasonCodes: [] },
    { moduleKey: "code", label: "Code IDE", status: "limited", entryPoint: "code_learning", targetRoute: "code-learning", priority: "normal", userSafeSummary: "Practice code with runtime state, repeated errors, and Tutor handoff context.", actionCount: 1, warningCount: 0, reasonCodes: [] },
    { moduleKey: "progress", label: "Progress", status: "ready", entryPoint: "progress", targetRoute: "progress", priority: "low", userSafeSummary: "Review durable progress and memory status.", actionCount: 1, warningCount: 0, reasonCodes: [] },
  ];
  const inputCards = safeList(cards);
  const list = (inputCards.length > 0 ? inputCards : fallback).map((card) => {
    const view = toView(card.targetRoute, card.entryPoint, card.moduleKey);
    const display = missionModuleDisplay(card, view);
    return { ...card, label: display.label, userSafeSummary: display.summary };
  });
  return (
    <section className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
      {list.map((card) => {
        const view = toView(card.targetRoute, card.entryPoint, card.moduleKey);
        const Icon = moduleIconMap[view] ?? Layers;
        const display = missionModuleDisplay(card, view);
        return (
          <button
            key={`${card.moduleKey}-${card.label}`}
            type="button"
            onClick={() => onViewChange(view)}
            className="min-h-[148px] rounded-xl border border-white/[0.08] bg-white/[0.04] p-4 text-left shadow-sm transition hover:border-[#6ed7ce]/35 hover:bg-white/[0.065]"
          >
            <div className="flex items-start justify-between gap-3">
              <div className="rounded-lg border border-white/[0.08] bg-white/[0.055] p-2">
                <Icon className="h-4 w-4 text-[#c8cfca]" />
              </div>
            </div>
            <h3 className="mt-3 text-sm font-semibold text-[#f4f6f3]">{display.label}</h3>
            <p className="mt-1 line-clamp-2 text-xs leading-5 text-[#9aa3a0]">{card.userSafeSummary || "Orka çalışma bağlamına bağlı."}</p>
          </button>
        );
      })}
    </section>
  );
}

function SectionList({ mission, onViewChange }: { mission: OrkaMissionControlDto; onViewChange: (view: string) => void }) {
  const sections = safeList(mission.sections);
  if (sections.length === 0) return null;
  return (
    <section className="grid gap-3 lg:grid-cols-2">
      {sections.slice(0, 8).map((section) => {
        const actions = safeList(section.actions);
        return (
        <div key={section.sectionKey} className="rounded-xl border border-white/[0.08] bg-white/[0.035] p-4">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h3 className="text-sm font-semibold text-[#f4f6f3]">{section.label || labelize(section.sectionKey)}</h3>
            <StatusPill label={section.status} />
          </div>
          <ReasonChips items={section.reasonCodes} />
          <div className="mt-3 grid gap-2">
            {actions.slice(0, 2).map((action, index) => (
              <ActionButton key={`${section.sectionKey}-${index}`} action={action} onViewChange={onViewChange} />
            ))}
            {actions.length === 0 && <p className="text-xs font-semibold text-[#667085]">No visible action in this section yet.</p>}
          </div>
        </div>
      );
      })}
    </section>
  );
}

export function MissionControlHome({ activeTopic, sessionId, topics, onViewChange }: HomePanelProps) {
  const [state, setState] = useState<{
    loading: boolean;
    today: DashboardTodayDto | null;
    mission: OrkaMissionControlDto | null;
    coach: OrkaStudyCoachDto | null;
    learningState: OrkaLearningStateDto | null;
    error: boolean;
  }>({ loading: true, today: null, mission: null, coach: null, learningState: null, error: false });

  const params = useMemo(() => compactParams(activeTopic, sessionId), [activeTopic, sessionId]);

  useEffect(() => {
    let cancelled = false;
    setState((prev) => ({ ...prev, loading: true, error: false }));
    Promise.allSettled([
      DashboardAPI.getToday(),
      LearningAPI.getMissionControl(params),
      LearningAPI.getStudyCoach(params),
      LearningAPI.getOrkaState(params),
    ]).then(([todayResult, missionResult, coachResult, learningStateResult]) => {
      if (cancelled) return;
      const today = todayResult.status === "fulfilled" ? todayResult.value.data : null;
      const mission = missionResult.status === "fulfilled" ? missionResult.value : today?.missionControl ?? null;
      const coach = coachResult.status === "fulfilled" ? coachResult.value : today?.studyCoach ?? null;
      const learningState = learningStateResult.status === "fulfilled" ? learningStateResult.value : today?.orkaLearningState ?? null;
      setState({
        loading: false,
        today,
        mission,
        coach,
        learningState,
        error: !today && !mission && !coach && !learningState,
      });
    });
    return () => {
      cancelled = true;
    };
  }, [params]);

  const mission = state.mission ?? state.today?.missionControl ?? null;
  const coach = state.coach ?? state.today?.studyCoach ?? null;
  const primary = mission?.primaryMission;

  /* Human-readable readiness label */
  function readinessLabel(load?: string | null) {
    const k = (load ?? "").toLowerCase();
    if (k.includes("thin") || k.includes("limited") || k.includes("unknown")) return "Başlamaya hazır";
    if (k.includes("ready") || k.includes("stable") || k.includes("passed")) return "Güçlü";
    if (k.includes("warning") || k.includes("stale")) return "Dikkat";
    return "İnceleniyor";
  }

  const moduleGrid = [
    { key: "tutor",        label: "Tutor",      desc: "Bir konuyu sor, yanlışını açtır, kavramı kır.",              icon: MessageSquare,  accent: "#6ed7ce" },
    { key: "study-room",   label: "Ders Odası", desc: "Konu anlatımı, örnek ve mini kontrol akışı.",               icon: GraduationCap,  accent: "#a7e879" },
    { key: "review",       label: "Quiz & Tekrar",desc: "Diagnostic quiz, flashcard ve telafi pratiği.",            icon: ClipboardCheck, accent: "#b4a0f0" },
    { key: "exams",        label: "Sınav Modu", desc: "Deneme analizi ve merkezi sınav hazırlığı.",               icon: GraduationCap,  accent: "#dac17a" },
    { key: "sources-wiki", label: "Kaynaklar",  desc: "Kaynak defteri, Wiki notları ve citation izleri.",          icon: BookOpen,       accent: "#6ed7ce" },
    { key: "notebook",     label: "Stüdyo",     desc: "Özet, slayt, zihin haritası ve sesli anlatım üret.",       icon: FileText,       accent: "#a7e879" },
    { key: "code",         label: "Kod IDE",    desc: "Kodu çalıştır, hatayı öğrenme sinyaline dönüştür.",        icon: Code2,          accent: "#b4a0f0" },
    { key: "progress",     label: "İlerleme",   desc: "Mastery durumu, bellek ve zayıf kavramlar.",               icon: BrainCircuit,   accent: "#a7e879" },
  ] as const;

  return (
    <PanelScaffold
      moduleKey="home"
      title="Kontrol Paneli"
      eyebrow="Bugünün çalışma yönü"
      icon={Sparkles}
      summary={undefined}
    >
      {state.loading && <LoadingBlock />}
      {!state.loading && state.error && (
        <EmptyBlock title="Kontrol paneli yükleniyor" detail="Uygulama hazır. Öğrenme durumu alınıyor — ya da henüz hiç ders başlatılmadı." />
      )}
      {!state.loading && !state.error && (
        <>
          {/* ── Bugünün odak kartı ── */}
          <section
            className="relative overflow-hidden rounded-2xl p-6"
            style={{
              background: "linear-gradient(135deg, rgba(110,215,206,0.08) 0%, rgba(13,16,20,0.95) 100%)",
              border: "1px solid rgba(110,215,206,0.14)",
            }}
          >
            <div className="relative z-10">
              <p className="text-[11px] font-semibold uppercase tracking-[0.16em]" style={{ color: "#6ed7ce" }}>Bugün en iyi adım</p>
              <h2 className="mt-3 max-w-3xl text-[28px] font-bold leading-tight tracking-tight text-white">
                {primary?.label ?? state.today?.dailyFocusTitle ?? "Kısa bir tekrar ile başla"}
              </h2>
              <p className="mt-3 max-w-2xl text-[14px] leading-7" style={{ color: "#8f9894" }}>
                {primary?.reason ?? state.today?.dailyFocusReason ?? "Orka çalışma sinyalini toparlıyor — en risksiz başlangıç kısa bir tekrardır."}
              </p>
              <div className="mt-5 flex flex-wrap gap-2.5">
                {primary && (
                  <button
                    type="button"
                    onClick={() => onViewChange(toView(primary.targetRoute, primary.entryPoint, primary.actionType))}
                    className="inline-flex items-center gap-2 rounded-xl px-4 py-2.5 text-[13px] font-semibold transition hover:-translate-y-0.5"
                    style={{ background: "#6ed7ce", color: "#041210", boxShadow: "0 0 24px rgba(110,215,206,0.22)" }}
                  >
                    {primary.label || "Başla"}
                    <ArrowRight className="h-4 w-4" />
                  </button>
                )}
                {mission?.studyRoomSuggestion && (
                  <button
                    type="button"
                    onClick={() => onViewChange("study-room")}
                    className="inline-flex items-center gap-2 rounded-xl border px-4 py-2.5 text-[13px] font-medium text-white transition hover:bg-white/6"
                    style={{ borderColor: "rgba(255,255,255,0.12)", background: "rgba(255,255,255,0.05)" }}
                  >
                    Ders Odası'na geç
                  </button>
                )}
                {!primary && (
                  <button
                    type="button"
                    onClick={() => onViewChange("tutor")}
                    className="inline-flex items-center gap-2 rounded-xl px-4 py-2.5 text-[13px] font-semibold transition hover:-translate-y-0.5"
                    style={{ background: "#6ed7ce", color: "#041210" }}
                  >
                    Tutor'ı aç
                    <ArrowRight className="h-4 w-4" />
                  </button>
                )}
              </div>
            </div>
            {/* bg accent */}
            <div className="pointer-events-none absolute right-0 top-0 h-full w-1/3 rounded-r-2xl"
              style={{ background: "linear-gradient(to left, rgba(110,215,206,0.05), transparent)" }} />
          </section>

          {/* ── Hazırlık durumu şeridi ── */}
          <section className="grid grid-cols-2 gap-2 sm:grid-cols-4">
            {[
              { label: "Tekrar yükü",  value: readinessLabel(mission?.reviewLoad),          accent: "#6ed7ce" },
              { label: "Telafi",        value: readinessLabel(mission?.repairLoad),          accent: "#a7e879" },
              { label: "Sınav",         value: readinessLabel(mission?.examLoad),            accent: "#dac17a" },
              { label: "Kaynak",        value: readinessLabel(mission?.sourceWikiLoad),      accent: "#b4a0f0" },
            ].map((item) => (
              <div
                key={item.label}
                className="rounded-xl border px-4 py-3"
                style={{ borderColor: "rgba(255,255,255,0.07)", background: "rgba(255,255,255,0.03)" }}
              >
                <p className="text-[10px] font-semibold uppercase tracking-widest" style={{ color: "#3a403d" }}>{item.label}</p>
                <p className="mt-1.5 text-[14px] font-semibold" style={{ color: item.accent }}>{item.value}</p>
              </div>
            ))}
          </section>

          {/* ── Modül grid ── */}
          <section>
            <p className="mb-3 px-1 text-[10px] font-semibold uppercase tracking-[0.14em]" style={{ color: "#3a403d" }}>Modüller</p>
            <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-4">
              {moduleGrid.map((mod) => {
                const Icon = mod.icon;
                return (
                  <button
                    key={mod.key}
                    type="button"
                    onClick={() => onViewChange(mod.key)}
                    className="group relative rounded-xl border p-4 text-left transition hover:-translate-y-0.5"
                    style={{
                      borderColor: "rgba(255,255,255,0.07)",
                      background: "rgba(255,255,255,0.03)",
                    }}
                  >
                    <div
                      className="mb-3 inline-flex h-8 w-8 items-center justify-center rounded-lg"
                      style={{
                        background: `${mod.accent}14`,
                        border: `1px solid ${mod.accent}28`,
                      }}
                    >
                      <Icon className="h-4 w-4" style={{ color: mod.accent }} />
                    </div>
                    <p className="text-[13px] font-semibold text-white">{mod.label}</p>
                    <p className="mt-1 text-[11px] leading-5" style={{ color: "#5a6360" }}>{mod.desc}</p>
                    <ArrowRight
                      className="absolute right-3 top-3 h-3.5 w-3.5 opacity-0 transition group-hover:opacity-100"
                      style={{ color: mod.accent }}
                    />
                  </button>
                );
              })}
            </div>
          </section>
        </>
      )}
    </PanelScaffold>
  );
}

export function StudyRoomPanel({ activeTopic, sessionId, onViewChange }: BasePanelProps) {
  const [state, setState] = useState<LoadState<OrkaStudyRoomDto>>({ loading: true, data: null, error: false });
  const params = useMemo(() => compactParams(activeTopic, sessionId), [activeTopic, sessionId]);

  const load = useCallback(() => {
    setState((prev) => ({ ...prev, loading: true, error: false }));
    ClassroomAPI.getStudyRoom(params)
      .then((data) => setState({ loading: false, data, error: false }))
      .catch(() => setState({ loading: false, data: null, error: true }));
  }, [params]);

  useEffect(() => load(), [load]);

  const startSession = async () => {
    try {
      const data = await ClassroomAPI.startStudyRoom({ ...params, mode: state.data?.studyRoomMode });
      setState({ loading: false, data, error: false });
      toast.success("Çalışma akışı hazırlandı.");
    } catch {
      toast.error("Çalışma akışı güvenli şekilde başlatılamadı.");
    }
  };

  const submitCheckpoint = async () => {
    if (!state.data?.classroomSessionId) return;
    try {
      const data = await ClassroomAPI.submitStudyRoomCheckpoint({
        classroomSessionId: state.data.classroomSessionId,
        responseSignal: "needs_review",
        skipped: true,
        conceptKey: state.data.selectedConcept ?? undefined,
      });
      setState({ loading: false, data, error: false });
      toast.success("Checkpoint recorded as needs review.");
    } catch {
      toast.error("Checkpoint could not be recorded safely.");
    }
  };

  const data = state.data;
  const lessonPlan = data?.lessonPlan ?? {
    title: data?.selectedTopic ?? activeTopic?.title ?? "Bugünkü ders",
    objective: "Konuyu chat yerine daha sakin bir ders akışıyla anlamak.",
    steps: ["Kısa anlatımı dinle", "Örneği beraber çöz", "Mini kontrolle takıldığın yeri işaretle"],
    durationBand: "10-15 dakika",
    stopCondition: "Mini kontrol tamamlanınca",
  };
  const checkpointPlan = data?.checkpointPlan ?? {
    checkpointStatus: "ready",
    keyVisible: false,
    prompt: "Bu anlatımdan sonra en çok hangi noktada takıldığını seç.",
    responseSignal: "needs_review",
    postSubmitFeedback: "",
    reasonCodes: [],
  };
  const roles = safeList(data?.roles).length
    ? safeList(data?.roles)
    : [
        { roleKey: "teacher", label: "Hoca", responsibility: "Konuyu sade ve sıralı anlatır." },
        { roleKey: "assistant", label: "Asistan", responsibility: "Örneği açar ve takıldığın yeri yakalar." },
        { roleKey: "note_taker", label: "Not tutucu", responsibility: "Ders sonunda 3 kısa madde bırakır." },
      ];
  const turn = data?.currentTurn ?? {
    turnStatus: "ready",
    speakerRole: "teacher",
    userSafeSummary: "Hoca konuyu kısa bir ana fikirle açar; sonra örnek ve mini kontrol gelir.",
    responseSignal: "listening",
    reasonCodes: [],
  };
  const visibleSteps = safeList(lessonPlan.steps).slice(0, 4);
  const studyActions = [
    ...safeList(data?.nextActions),
    ...safeList(data?.tutorHandoffs),
    ...safeList(data?.quizHandoffs),
    ...safeList(data?.reviewHandoffs),
    ...safeList(data?.sourceWikiHandoffs),
    ...safeList(data?.notebookHandoffs),
  ].filter((action) => action.label || action.actionType);
  return (
    <PanelScaffold
      moduleKey="study-room"
      title="Ders Odası"
      eyebrow="Rehberli ders akışı"
      icon={GraduationCap}
      summary={data?.safeStudentSummary ?? "Chat yorucu geldiğinde konu kısa bir anlatıma, örneğe ve mini kontrole bölünür."}
      aside={<StatusPill label={data?.sessionReadiness ?? (state.error ? "blocked" : "thin_evidence")} />}
    >
      {state.loading && <LoadingBlock label="Ders odası hazırlanıyor" />}
      {!state.loading && state.error && <EmptyBlock title="Ders odası sınırlı" detail="Ders için güvenli konu bağlamı henüz hazır değil. Tutor'dan bir konuya başla." />}
      {!state.loading && data && (
        <>
          <section className="grid items-start gap-4 xl:grid-cols-[minmax(0,1fr)_340px]">
            <article className="overflow-hidden rounded-[20px] border shadow-[0_16px_60px_rgba(0,0,0,0.4)]"
              style={{ background: "#0e1318", borderColor: "rgba(167,232,121,0.2)" }}>
              <div className="flex flex-wrap items-center justify-between gap-3 border-b px-4 py-3 min-[480px]:px-5"
                style={{ background: "rgba(167,232,121,0.05)", borderBottomColor: "rgba(167,232,121,0.15)" }}>
                <div className="flex min-w-0 items-center gap-3">
                  <span className="grid h-9 w-9 shrink-0 place-items-center rounded-2xl text-white shadow-sm"
                    style={{ background: "rgba(167,232,121,0.15)", border: "1px solid rgba(167,232,121,0.25)" }}>
                    <GraduationCap className="h-4 w-4" style={{ color: "#a7e879" }} />
                  </span>
                  <div className="min-w-0">
                    <p className="text-[11px] font-bold uppercase tracking-[0.18em]" style={{ color: "#a7e879" }}>Ders Tahtası</p>
                    <p className="truncate text-xs font-medium" style={{ color: "#5a6360" }}>{labelize(data.studyRoomMode ?? "guided lesson")} · {lessonPlan.durationBand}</p>
                  </div>
                </div>
                <span className="rounded-full border px-3 py-1 text-xs font-semibold" style={{ borderColor: "rgba(167,232,121,0.2)", color: "#a7e879", background: "rgba(167,232,121,0.08)" }}>
                  {labelize(data.recommendedPace ?? "sakin tempo")}
                </span>
              </div>

              <div className="bg-[linear-gradient(to_bottom,rgba(255,255,255,0.025)_1px,transparent_1px)] bg-[length:100%_44px] px-3 py-4 min-[480px]:px-5 min-[480px]:py-6 md:px-7 md:py-7">
                <div className="max-w-3xl">
                  <p className="text-[11px] font-bold uppercase tracking-[0.18em]" style={{ color: "#a7e879" }}>Bugünkü konu</p>
                  <h2 className="mt-2 text-[22px] font-bold leading-tight tracking-tight text-white min-[480px]:text-[28px] md:text-[32px]">{lessonPlan.title}</h2>
                  <p className="mt-3 max-w-2xl text-sm leading-6" style={{ color: "#8f9894" }}>{lessonPlan.objective}</p>
                </div>

                <div className="mt-5 rounded-2xl border p-3 min-[480px]:mt-6 min-[480px]:p-4" style={{ borderColor: "rgba(167,232,121,0.15)", background: "rgba(167,232,121,0.05)" }}>
                  <div className="flex items-start gap-3">
                    <span className="grid h-9 w-9 shrink-0 place-items-center rounded-2xl" style={{ background: "rgba(110,215,206,0.12)" }}>
                      <BookOpen className="h-4 w-4" style={{ color: "#6ed7ce" }} />
                    </span>
                    <div className="min-w-0">
                      <p className="text-xs font-bold uppercase tracking-[0.14em]" style={{ color: "#5a6360" }}>{labelize(turn.speakerRole)}</p>
                      <p className="mt-1 text-sm leading-6" style={{ color: "#c8cfca" }}>{turn.userSafeSummary}</p>
                    </div>
                  </div>
                </div>

                <div className="mt-4 grid gap-2 min-[480px]:mt-5 min-[480px]:gap-3">
                  {visibleSteps.map((step, index) => (
                    <div key={`${step}-${index}`} className="group flex items-center gap-3 rounded-2xl border px-3 py-3 text-sm transition min-[480px]:px-4"
                      style={{ borderColor: "rgba(167,232,121,0.12)", background: "rgba(255,255,255,0.03)", color: "#c8cfca" }}>
                      <span className="grid h-8 w-8 shrink-0 place-items-center rounded-full text-xs font-bold text-white shadow-sm" style={{ background: "rgba(167,232,121,0.18)", color: "#a7e879" }}>{index + 1}</span>
                      <span className="leading-6">{step}</span>
                    </div>
                  ))}
                </div>

                <div className="mt-5 flex flex-wrap items-center gap-2 min-[480px]:mt-6">
                  <button type="button" onClick={startSession} className="inline-flex items-center gap-2 rounded-2xl px-4 py-2.5 text-sm font-semibold text-white shadow-sm transition hover:-translate-y-0.5"
                    style={{ background: "#a7e879", color: "#0a1a05" }}>
                    <Play className="h-4 w-4" />
                    Dersi başlat
                  </button>
                  <button type="button" onClick={() => onViewChange("tutor")} className="inline-flex items-center gap-2 rounded-2xl border px-4 py-2.5 text-sm font-medium text-white shadow-sm transition hover:bg-white/6"
                    style={{ borderColor: "rgba(255,255,255,0.12)", background: "rgba(255,255,255,0.05)" }}>
                    <MessageSquare className="h-4 w-4" />
                    Chat'e dön
                  </button>
                  <span className="text-xs font-medium" style={{ color: "#5a6360" }}>{lessonPlan.stopCondition}</span>

                  {studyActions[0] && (
                    <button
                      type="button"
                      onClick={() => onViewChange(toView(studyActions[0].targetRoute, studyActions[0].entryPoint, studyActions[0].actionType))}
                      className="inline-flex max-w-full items-center gap-2 rounded-2xl border border-[#172033]/12 bg-white/70 px-3 py-2 text-xs font-black text-[#344054] shadow-sm transition hover:border-[#172033]/24 hover:bg-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#6ed7ce]"
                    >
                      <span className="truncate">Sonraki: {studyActions[0].label || labelize(studyActions[0].actionType)}</span>
                      <ArrowRight className="h-3.5 w-3.5 shrink-0" />
                    </button>
                  )}
                </div>
              </div>
            </article>

            <aside className="grid gap-3 xl:sticky xl:top-5">
              <div className="rounded-2xl border border-white/[0.08] bg-white/[0.035] p-4">
                <div className="flex items-center justify-between gap-3">
                  <h3 className="text-sm font-semibold text-[#f4f6f3]">Ders ekibi</h3>
                  <span className="text-[11px] font-semibold text-[#8f9894]">Canlı ders değil</span>
                </div>
                <div className="mt-3 grid gap-2">
                  {roles.slice(0, 4).map((role) => (
                    <div key={role.roleKey} className="flex gap-3 rounded-2xl border border-white/[0.08] bg-white/[0.045] p-3 transition hover:bg-white/[0.06]">
                      <span className="grid h-8 w-8 shrink-0 place-items-center rounded-full border border-white/[0.08] bg-[#111518] text-xs font-black text-[#dfe6e1]">
                        {role.label.slice(0, 1)}
                      </span>
                      <div className="min-w-0">
                        <div className="text-sm font-semibold text-[#f4f6f3]">{role.label}</div>
                        <p className="mt-1 text-xs leading-5 text-[#9aa3a0]">{role.responsibility}</p>
                      </div>
                    </div>
                  ))}
                </div>
              </div>

              <div className="rounded-2xl border border-[#a7e879]/24 bg-[#162614] p-4 shadow-[0_18px_50px_rgba(0,0,0,0.22)]">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <h3 className="text-sm font-semibold text-[#f4f6f3]">Mini kontrol</h3>
                  <StatusPill label={checkpointPlan.keyVisible ? "cevap açık" : "cevap gizli"} />
                </div>
                <p className="mt-2 text-sm leading-6 text-[#c8cfca]">{checkpointPlan.prompt}</p>
                {checkpointPlan.postSubmitFeedback && <p className="mt-3 rounded-xl border border-[#a7e879]/18 bg-white/[0.055] p-3 text-xs leading-5 text-[#b6c9b2]">{checkpointPlan.postSubmitFeedback}</p>}
                <button type="button" onClick={submitCheckpoint} disabled={!data.classroomSessionId} className="mt-3 inline-flex w-full items-center justify-center gap-2 rounded-2xl border border-white/[0.12] bg-white/[0.09] px-3 py-2.5 text-sm font-semibold text-[#f4f6f3] transition hover:bg-white/[0.13] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#a7e879] disabled:cursor-not-allowed disabled:opacity-50">
                  <CheckCircle2 className="h-4 w-4" />
                  Takıldım olarak işaretle
                </button>
              </div>

              <div className="rounded-2xl border border-white/[0.08] bg-white/[0.035] p-4">
                <h3 className="text-sm font-semibold text-[#f4f6f3]">Kaynak dayanağı</h3>
                <div className="mt-3 grid gap-2">
                  <div className="flex items-center justify-between gap-3 rounded-xl border border-white/[0.08] bg-white/[0.045] px-3 py-2">
                    <span className="text-xs font-semibold text-[#9aa3a0]">Kaynak</span>
                    <span className="text-xs font-black text-[#dfe6e1]">{labelize(data.sourceReadiness)}</span>
                  </div>
                  <div className="flex items-center justify-between gap-3 rounded-xl border border-white/[0.08] bg-white/[0.045] px-3 py-2">
                    <span className="text-xs font-semibold text-[#9aa3a0]">Wiki</span>
                    <span className="text-xs font-black text-[#dfe6e1]">{labelize(data.wikiReadiness)}</span>
                  </div>
                </div>
                <button type="button" onClick={() => onViewChange("sources-wiki")} className="mt-3 inline-flex w-full items-center justify-center gap-2 rounded-2xl border border-white/[0.1] bg-white/[0.055] px-3 py-2 text-sm font-semibold text-[#f4f6f3] transition hover:border-[#6ed7ce]/35 hover:bg-white/[0.08] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#6ed7ce]">
                  <BookOpen className="h-4 w-4" />
                  Kaynağı kontrol et
                </button>
              </div>
            </aside>
          </section>

          <WarningList warnings={data.warnings} />
        </>
      )}
    </PanelScaffold>
  );
}

export function ExamWarRoomPanel({ activeTopic, sessionId, onViewChange }: BasePanelProps) {
  const [state, setState] = useState<LoadState<OrkaExamWarRoomDto>>({ loading: true, data: null, error: false });
  const params = useMemo(() => compactParams(activeTopic, sessionId, { examCode: "kpss" }), [activeTopic, sessionId]);

  useEffect(() => {
    let cancelled = false;
    setState((prev) => ({ ...prev, loading: true, error: false }));
    CentralExamsAPI.getWarRoom(params.examCode ?? "kpss", params)
      .then((data) => !cancelled && setState({ loading: false, data, error: false }))
      .catch(() => !cancelled && setState({ loading: false, data: null, error: true }));
    return () => {
      cancelled = true;
    };
  }, [params]);

  const data = state.data;
  const warnings: ExamWarRoomWarningDto[] = data ? [...safeList(data.sourceWikiWarnings), ...safeList(data.curriculumCoverageWarnings), ...safeList(data.conflictWarnings)] : [];
  return (
    <PanelScaffold
      moduleKey="exams"
      title="Sınav Modu"
      eyebrow="Merkezi sınav hazırlığı"
      icon={GraduationCap}
      summary={data?.userSafeSummary ?? "Zayıf çıktıları, deneme hatalarını ve hazırlık durumunu skor vaadi yapmadan çalış."}
      aside={<StatusPill label={data?.readinessStatus ?? (state.error ? "blocked" : "thin_exam_evidence")} />}
    >
      {state.loading && <LoadingBlock label="Sınav verileri yükleniyor" />}
      {!state.loading && state.error && <EmptyBlock title="Sınav modu sınırlı" detail="Sınav hazırlık verisi henüz hazır değil. Önce bir konu üzerinde çalışmaya başla." />}
      {!state.loading && data && (
        <>
          <section
            className="rounded-2xl p-5"
            style={{ background: "rgba(218,193,122,0.07)", border: "1px solid rgba(218,193,122,0.16)" }}
          >
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <p className="text-[11px] font-semibold uppercase tracking-widest" style={{ color: "#dac17a" }}>
                  {data.activeExam.displayName || data.activeExam.examCode}
                </p>
                <h2 className="mt-2 text-[22px] font-bold text-white">{data.todayExamMission.label}</h2>
                <p className="mt-2 max-w-xl text-[13px] leading-6" style={{ color: "#8f9894" }}>{data.todayExamMission.reason}</p>
              </div>
              <ActionButton action={data.todayExamMission} onViewChange={onViewChange} compact />
            </div>
          </section>

          <MetricGrid
            items={[
              { label: "Zayıf konu",       value: safeList(data.weakOutcomes).length },
              { label: "Tekrar gereken",   value: safeList(data.dueOutcomes).length },
              { label: "Deneme hata kümesi", value: safeList(data.denemeMistakeClusters).length },
              { label: "Önerilen adım",    value: safeList(data.recommendedPracticeQueue).length },
            ]}
          />

          <WarningList warnings={warnings} title="Kaynak ve müfredat uyarıları" />

          <section className="grid gap-4 xl:grid-cols-3">
            <CompactList title="Zayıf konular" items={safeList(data.weakOutcomes).map((item) => ({ title: item.label, detail: item.userSafeSummary || item.recommendedAction, status: item.readinessStatus }))} />
            <CompactList title="Deneme hata örüntüsü" items={safeList(data.denemeMistakeClusters).map((item) => ({ title: item.label, detail: item.recommendedAction, status: `${item.mistakeCount} hata` }))} />
            <CompactList title="Soru tipi eksikleri" items={safeList(data.weakQuestionTypes).map((item) => ({ title: labelize(item.questionType), detail: item.recommendedAction, status: item.readinessStatus }))} />
          </section>

          <ActionList actions={[...safeList(data.recommendedPracticeQueue), ...safeList(data.tutorRepairHandoffs), ...safeList(data.studyRoomHandoffs)]} onViewChange={onViewChange} title="Önerilen adımlar" />
        </>
      )}
    </PanelScaffold>
  );
}

function CompactList({ title, items }: { title: string; items: Array<{ title: string; detail?: string | null; status?: string | null }> }) {
  return (
    <section className="rounded-lg border border-[#526d82]/12 bg-white/78 p-4">
      <h3 className="text-sm font-black text-[#172033]">{title}</h3>
      <div className="mt-3 grid gap-2">
        {items.slice(0, 5).map((item, index) => (
          <div key={`${title}-${item.title}-${index}`} className="rounded-md border border-[#526d82]/10 bg-[#f8fbfb] p-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="min-w-0 flex-1 truncate text-sm font-black text-[#172033]">{item.title}</div>
              <StatusPill label={item.status ?? "observed"} />
            </div>
            {item.detail && <p className="mt-1 text-xs leading-5 text-[#667085]">{item.detail}</p>}
          </div>
        ))}
        {items.length === 0 && <p className="text-sm leading-6 text-[#667085]">No item is urgent from current evidence.</p>}
      </div>
    </section>
  );
}

export function SourceWikiProPanel({ activeTopic, sessionId, onViewChange, workspace }: BasePanelProps) {
  const [state, setState] = useState<LoadState<OrkaSourceWikiProDto>>({ loading: true, data: null, error: false });
  const params = useMemo(() => compactParams(activeTopic, sessionId), [activeTopic, sessionId]);

  useEffect(() => {
    let cancelled = false;
    setState((prev) => ({ ...prev, loading: true, error: false }));
    SourcesAPI.getWikiPro(params)
      .then((data) => !cancelled && setState({ loading: false, data, error: false }))
      .catch(() => !cancelled && setState({ loading: false, data: null, error: true }));
    return () => {
      cancelled = true;
    };
  }, [params]);

  const data = state.data;
  const warnings: SourceWikiProWarningDto[] = data ? [...safeList(data.citationWarnings), ...safeList(data.examWarRoomWarnings), ...safeList(data.missionControlWarnings), ...safeList(data.conflictWarnings)] : [];
  const sourceRows = data
    ? [
        ...safeList(data.sourceReadinessItems),
        ...safeList(data.staleSources),
        ...safeList(data.insufficientSources),
        ...safeList(data.degradedSources),
      ].filter((source, index, list) => list.findIndex((item) => item.sourceId === source.sourceId) === index)
    : [];
  const wikiRows = data
    ? [
        ...safeList(data.wikiReadinessItems),
        ...safeList(data.wikiRepairPages),
        ...safeList(data.sourceBackedPages),
      ].filter((page, index, list) => list.findIndex((item) => item.wikiPageId === page.wikiPageId) === index)
    : [];
  const activeSource = sourceRows[0];
  const activePage = wikiRows[0];
  const conceptRows = safeList(data?.sourceBackedConcepts).length ? safeList(data?.sourceBackedConcepts) : safeList(data?.linkedConcepts);
  return (
    <PanelScaffold
      moduleKey="sources-wiki"
      title="Kaynaklar & Wiki"
      eyebrow="Kaynak defteri ve bilgi ağı"
      icon={Network}
      summary={data?.userSafeSummary ?? "Kaynak hazırlığı, Wiki sayfaları, citation izleri ve bağlantılar tek çalışma alanında."}
      aside={<StatusPill label={data?.readinessStatus ?? (state.error ? "blocked" : "thin_evidence")} />}
    >
      {state.loading && <LoadingBlock label="Kaynak ve Wiki verileri yükleniyor" />}
      {!state.loading && state.error && <EmptyBlock title="Kaynak alanı sınırlı" detail="Kaynak ve Wiki verisi henüz hazır değil. İlk adım olarak bir dosya veya not yükle." />}
      {!state.loading && data && (
        <>
          <section className="grid min-h-[560px] overflow-hidden rounded-2xl border border-white/[0.08] bg-[#0c0f10] shadow-[0_24px_80px_rgba(0,0,0,0.22)] xl:grid-cols-[260px_minmax(0,1fr)_300px]">
            <aside className="border-b border-white/[0.08] bg-[#080a0b] p-4 xl:border-b-0 xl:border-r">
              <div className="flex items-center justify-between gap-3">
                <div>
                  <p className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[#6ed7ce]">Kaynaklar</p>
                  <h3 className="mt-1 text-sm font-semibold text-[#f4f6f3]">{data.evidenceMap.readySourceCount}/{data.evidenceMap.uploadedSourceCount} hazır</h3>
                </div>
                <button type="button" onClick={() => onViewChange("notebook")} className="rounded-lg border border-white/[0.1] px-2 py-1 text-xs font-semibold text-[#c8cfca] transition hover:border-[#6ed7ce]/40 hover:text-[#f4f6f3]">
                  Ekle
                </button>
              </div>
              <div className="mt-4 grid max-h-[430px] gap-2 overflow-y-auto pr-1">
                {sourceRows.slice(0, 8).map((source) => (
                  <button key={source.sourceId} type="button" className="rounded-xl border border-white/[0.08] bg-white/[0.035] p-3 text-left transition hover:border-[#6ed7ce]/35 hover:bg-white/[0.055]">
                    <div className="flex items-center justify-between gap-2">
                      <span className="truncate text-sm font-semibold text-[#f4f6f3]">{source.title}</span>
                      <span className="text-[11px] text-[#8f9894]">{source.pageCount} syf</span>
                    </div>
                    <p className="mt-1 truncate text-xs text-[#9aa3a0]">{labelize(source.evidenceStatus)} · {source.linkedConceptCount} kavram</p>
                  </button>
                ))}
                {sourceRows.length === 0 && <p className="rounded-xl border border-white/[0.08] bg-white/[0.035] p-3 text-sm leading-6 text-[#9aa3a0]">Henüz seçilecek kaynak yok. İlk iş kaynak eklemek.</p>}
              </div>
            </aside>

            <section className="min-w-0 p-5">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <p className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[#8f9894]">Aktif wiki alanı</p>
                  <h2 className="mt-2 text-2xl font-semibold tracking-[-0.01em] text-[#f4f6f3]">{activePage?.title ?? activeSource?.title ?? "Kaynak seç ve wiki notunu aç"}</h2>
                  <p className="mt-2 max-w-2xl text-sm leading-6 text-[#9aa3a0]">
                    {activePage?.nextAction ?? "Kart vitrini yerine seçili kaynağın notu, citation izleri ve ilişkili kavramları burada çalışılır."}
                  </p>
                </div>
                <ActionButton action={data.todaySourceWikiMission} onViewChange={onViewChange} compact />
              </div>

              <div className="mt-5 grid gap-3 lg:grid-cols-[minmax(0,1fr)_220px]">
                <div className="rounded-2xl border border-white/[0.08] bg-[#111517] p-4">
                  <div className="flex items-center justify-between gap-3">
                    <h3 className="text-sm font-semibold text-[#f4f6f3]">Belge ve citation izi</h3>
                    <StatusPill label={data.citationReadiness} />
                  </div>
                  <div className="mt-4 grid gap-3">
                    {safeList(data.citationReadinessItems).slice(0, 3).map((citation) => (
                      <div key={citation.citationCheckId} className="rounded-xl border border-white/[0.08] bg-white/[0.035] p-3">
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <span className="text-sm font-semibold text-[#f4f6f3]">{citation.sourceTitle || citation.citationId}</span>
                          <StatusPill label={citation.citationStatus} />
                        </div>
                        <p className="mt-1 text-xs leading-5 text-[#9aa3a0]">{citation.userSafeWarning || labelize(citation.evidenceStatus)}</p>
                      </div>
                    ))}
                    {safeList(data.citationReadinessItems).length === 0 && (
                      <div className="rounded-xl border border-dashed border-white/[0.1] bg-white/[0.025] p-4 text-sm leading-6 text-[#9aa3a0]">
                        Citation izi yoksa cevap üretilmez; önce kaynak veya wiki dayanağı hazırlanır.
                      </div>
                    )}
                  </div>
                </div>

                <div className="rounded-2xl border border-white/[0.08] bg-[#111517] p-4">
                  <h3 className="text-sm font-semibold text-[#f4f6f3]">Wiki sayfaları</h3>
                  <div className="mt-3 grid gap-2">
                    {wikiRows.slice(0, 5).map((page) => (
                      <div key={page.wikiPageId} className="rounded-xl border border-white/[0.08] bg-white/[0.035] p-3">
                        <div className="truncate text-sm font-semibold text-[#f4f6f3]">{page.title}</div>
                        <p className="mt-1 text-xs text-[#9aa3a0]">{labelize(page.curationStatus)} · {page.blockCount} blok</p>
                      </div>
                    ))}
                    {wikiRows.length === 0 && <p className="text-sm leading-6 text-[#9aa3a0]">Wiki notu henüz oluşmadı.</p>}
                  </div>
                </div>
              </div>
            </section>

            <aside className="border-t border-white/[0.08] bg-[#080a0b] p-4 xl:border-l xl:border-t-0">
              <p className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[#8f9894]">Inspector</p>
              <div className="mt-3 grid grid-cols-3 gap-2 xl:grid-cols-1">
                <div className="rounded-xl border border-white/[0.08] bg-white/[0.035] p-3">
                  <div className="text-xs text-[#8f9894]">Wiki</div>
                  <div className="mt-1 text-lg font-semibold text-[#f4f6f3]">{data.evidenceMap.wikiPageCount}</div>
                </div>
                <div className="rounded-xl border border-white/[0.08] bg-white/[0.035] p-3">
                  <div className="text-xs text-[#8f9894]">Kavram</div>
                  <div className="mt-1 text-lg font-semibold text-[#f4f6f3]">{data.evidenceMap.linkedConceptCount}</div>
                </div>
                <div className="rounded-xl border border-white/[0.08] bg-white/[0.035] p-3">
                  <div className="text-xs text-[#8f9894]">Uyarı</div>
                  <div className="mt-1 text-lg font-semibold text-[#f4f6f3]">{warnings.length}</div>
                </div>
              </div>
              <div className="mt-4">
                <h3 className="text-sm font-semibold text-[#f4f6f3]">Kaynaklı kavramlar</h3>
                <div className="mt-3 grid gap-2">
                  {conceptRows.slice(0, 5).map((concept) => (
                    <div key={`${concept.conceptKey}-${concept.sourceId ?? concept.wikiPageId ?? concept.sourceTitle}`} className="rounded-xl border border-white/[0.08] bg-white/[0.035] p-3">
                      <div className="truncate text-sm font-semibold text-[#f4f6f3]">{concept.conceptTitle || concept.conceptKey}</div>
                      <p className="mt-1 truncate text-xs text-[#9aa3a0]">{concept.sourceTitle || concept.basis}</p>
                    </div>
                  ))}
                  {conceptRows.length === 0 && <p className="text-sm leading-6 text-[#9aa3a0]">Bu konu için kaynaklı kavram bekleniyor.</p>}
                </div>
              </div>
              <WarningList warnings={warnings.slice(0, 2)} title="Dikkat" />
            </aside>
          </section>
        </>
      )}
      {workspace && (
        <details className="overflow-hidden rounded-xl border border-white/[0.08] bg-[#050607]">
          <summary className="cursor-pointer px-4 py-3 text-sm font-semibold text-[#c8cfca]">Gelişmiş Wiki çalışma alanını aç</summary>
          <section className="min-h-[680px] overflow-hidden border-t border-white/[0.08]">{workspace}</section>
        </details>
      )}
    </PanelScaffold>
  );
}

export function NotebookStudioProPanel({ activeTopic, sessionId, onViewChange, workspace }: BasePanelProps) {
  const [state, setState] = useState<LoadState<OrkaNotebookStudioProDto>>({ loading: true, data: null, error: false });
  const params = useMemo(() => compactParams(activeTopic, sessionId), [activeTopic, sessionId]);

  useEffect(() => {
    let cancelled = false;
    setState((prev) => ({ ...prev, loading: true, error: false }));
    NotebookStudioAPI.getPro(params)
      .then((data) => !cancelled && setState({ loading: false, data, error: false }))
      .catch(() => !cancelled && setState({ loading: false, data: null, error: true }));
    return () => {
      cancelled = true;
    };
  }, [params]);

  const data = state.data;
  const actions: NotebookStudioPackActionDto[] = data
    ? [
        ...safeList(data.tutorHandoffs),
        ...safeList(data.reviewHandoffs),
        ...safeList(data.sourceWikiHandoffs),
        ...safeList(data.examWarRoomHandoffs),
        ...safeList(data.studyRoomHandoffs),
        ...safeList(data.recommendedPacks).flatMap((pack) => safeList(pack.actions)),
      ]
    : [];
  const warnings: NotebookStudioPackWarningDto[] = data ? [...safeList(data.missionControlWarnings), ...safeList(data.warnings)] : [];
  const packRows = safeList(data?.recommendedPacks);
  const activePack = data?.activePack ?? packRows[0];
  const evidenceRows = data
    ? [
        ...safeList(data.sourceEvidenceLinks),
        ...safeList(data.wikiEvidenceLinks),
        ...safeList(data.conceptLinks),
        ...safeList(data.studyRoomTraceLinks),
      ]
    : [];

  return (
    <PanelScaffold
      moduleKey="notebook"
      title="Stüdyo"
      eyebrow="Çalışma paketi ve çıktı alanı"
      icon={FileText}
      summary={data?.userSafeSummary ?? "Kaynak ve Wiki dayanağından özet, quiz, slayt, zihin haritası veya sesli anlatım üret."}
      aside={<StatusPill label={data?.packReadiness ?? (state.error ? "blocked" : "thin_evidence")} />}
    >
      {state.loading && <LoadingBlock label="Stüdyo hazırlanıyor" />}
      {!state.loading && state.error && <EmptyBlock title="Stüdyo henüz sınırlı" detail="Çıktı üretmek için önce bir kaynak veya Wiki sayfası bağla." />}
      {!state.loading && data && (
        <>
          <section className="grid min-h-[560px] overflow-hidden rounded-2xl border border-white/[0.08] bg-[#0c0f10] shadow-[0_24px_80px_rgba(0,0,0,0.22)] xl:grid-cols-[260px_minmax(0,1fr)_300px]">
            <aside className="border-b border-white/[0.08] bg-[#080a0b] p-4 xl:border-b-0 xl:border-r">
              <p className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[#6ed7ce]">Kaynak seçimi</p>
              <h3 className="mt-1 text-sm font-semibold text-[#f4f6f3]">{evidenceRows.length} dayanak bağlı</h3>
              <div className="mt-4 grid max-h-[430px] gap-2 overflow-y-auto pr-1">
                {evidenceRows.slice(0, 9).map((link, index) => (
                  <label key={`${link.linkType}-${link.label}-${index}`} className="flex cursor-pointer gap-3 rounded-xl border border-white/[0.08] bg-white/[0.035] p-3 transition hover:border-[#6ed7ce]/35 hover:bg-white/[0.055]">
                    <input type="checkbox" className="mt-1 h-4 w-4 rounded border-white/[0.2] bg-[#0c0f10] accent-[#6ed7ce]" defaultChecked />
                    <span className="min-w-0">
                      <span className="block truncate text-sm font-semibold text-[#f4f6f3]">{link.label}</span>
                      <span className="mt-1 block truncate text-xs text-[#9aa3a0]">{labelize(link.linkType)} · {labelize(link.status)}</span>
                    </span>
                  </label>
                ))}
                {evidenceRows.length === 0 && <p className="rounded-xl border border-white/[0.08] bg-white/[0.035] p-3 text-sm leading-6 text-[#9aa3a0]">Önce Sources/Wiki içinde kaynak veya wiki dayanağı seçilmeli.</p>}
              </div>
            </aside>

            <section className="min-w-0 p-5">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <p className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[#8f9894]">Aktif çıktı</p>
                  <h2 className="mt-2 text-2xl font-semibold tracking-[-0.01em] text-[#f4f6f3]">{activePack?.title ?? "Çıktı üretimi için paket seç"}</h2>
                  <p className="mt-2 max-w-2xl text-sm leading-6 text-[#9aa3a0]">{activePack?.summary ?? "Notebook Studio tüm özellikleri aynı anda göstermeden, seçili kaynaklardan tek güvenilir çalışma çıktısı üretir."}</p>
                </div>
                {actions[0] && <ActionButton action={actions[0]} onViewChange={onViewChange} compact />}
              </div>

              <div className="mt-5 rounded-2xl border border-white/[0.08] bg-[#111517] p-4">
                <div className="flex items-center justify-between gap-3">
                  <h3 className="text-sm font-semibold text-[#f4f6f3]">Önizleme</h3>
                  <StatusPill label={activePack?.status ?? data.packReadiness} />
                </div>
                <div className="mt-4 grid gap-3 md:grid-cols-2">
                  {safeList(data.artifactQueue).slice(0, 4).map((artifact) => (
                    <div key={artifact.artifactId ?? `${artifact.artifactType}-${artifact.title}`} className="rounded-xl border border-white/[0.08] bg-white/[0.035] p-3">
                      <div className="flex items-center justify-between gap-2">
                        <span className="truncate text-sm font-semibold text-[#f4f6f3]">{artifact.title}</span>
                        <StatusPill label={artifact.status} />
                      </div>
                      <p className="mt-1 text-xs leading-5 text-[#9aa3a0]">{labelize(artifact.artifactType)} · {artifact.previewOnly ? "önizleme" : labelize(artifact.renderFormat)}</p>
                    </div>
                  ))}
                  {safeList(data.artifactQueue).length === 0 && (
                    <div className="rounded-xl border border-dashed border-white/[0.1] bg-white/[0.025] p-4 text-sm leading-6 text-[#9aa3a0] md:col-span-2">
                      Üretilecek çıktı seçildiğinde özet, quiz, audio veya çalışma paketi burada tek önizleme olarak açılır.
                    </div>
                  )}
                </div>
              </div>
            </section>

            <aside className="border-t border-white/[0.08] bg-[#080a0b] p-4 xl:border-l xl:border-t-0">
              <p className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[#8f9894]">Üretim kuyruğu</p>
              <div className="mt-3 grid gap-2">
                {packRows.slice(0, 4).map((pack) => (
                  <div key={pack.packId ?? `${pack.packType}-${pack.title}`} className="rounded-xl border border-white/[0.08] bg-white/[0.035] p-3">
                    <div className="truncate text-sm font-semibold text-[#f4f6f3]">{pack.title}</div>
                    <p className="mt-1 truncate text-xs text-[#9aa3a0]">{labelize(pack.packType)} · {labelize(pack.priority)}</p>
                  </div>
                ))}
                {packRows.length === 0 && <p className="text-sm leading-6 text-[#9aa3a0]">Şimdilik önerilen paket yok.</p>}
              </div>
              <div className="mt-4 rounded-xl border border-white/[0.08] bg-white/[0.035] p-3">
                <div className="text-xs text-[#8f9894]">Dışa aktarım</div>
                <div className="mt-2 grid gap-2">
                  {safeList(data.exportPreviews).slice(0, 3).map((preview) => (
                    <div key={`${preview.previewType}-${preview.packId ?? preview.artifactId ?? preview.readinessStatus}`} className="flex items-center justify-between gap-2 text-sm">
                      <span className="truncate text-[#f4f6f3]">{labelize(preview.previewType)}</span>
                      <StatusPill label={preview.readinessStatus} />
                    </div>
                  ))}
                  {safeList(data.exportPreviews).length === 0 && <span className="text-sm text-[#9aa3a0]">Hazır önizleme yok.</span>}
                </div>
              </div>
              <WarningList warnings={warnings.map((item) => ({ ...item, targetRoute: "notebook" })).slice(0, 2)} title="Dikkat" />
            </aside>
          </section>
        </>
      )}
      {workspace && (
        <details className="overflow-hidden rounded-xl border border-white/[0.08] bg-[#050607]">
          <summary className="cursor-pointer px-4 py-3 text-sm font-semibold text-[#c8cfca]">Gelişmiş Notebook araçlarını aç</summary>
          <section className="min-h-[680px] overflow-hidden border-t border-white/[0.08]">{workspace}</section>
        </details>
      )}
    </PanelScaffold>
  );
}

export function CodeLearningIdePanel({
  activeTopic,
  sessionId,
  onViewChange,
  pendingMessage,
  onPendingMessageConsumed,
  onSendToTutor,
  quizQuestion,
  onCloseQuiz,
}: BasePanelProps & {
  pendingMessage?: string | null;
  onPendingMessageConsumed?: () => void;
  onSendToTutor: (message: string) => void;
  quizQuestion?: string | null;
  onCloseQuiz?: () => void;
}) {
  const [state, setState] = useState<LoadState<OrkaCodeLearningIdeDto>>({ loading: true, data: null, error: false });
  const params = useMemo(() => compactParams(activeTopic, sessionId, { language: "csharp" }), [activeTopic, sessionId]);

  useEffect(() => {
    let cancelled = false;
    setState((prev) => ({ ...prev, loading: true, error: false }));
    CodeAPI.getLearningIde(params)
      .then((data) => !cancelled && setState({ loading: false, data, error: false }))
      .catch(() => !cancelled && setState({ loading: false, data: null, error: true }));
    return () => {
      cancelled = true;
    };
  }, [params]);

  const data = state.data;
  const warnings: CodeLearningWarningDto[] = data ? [...safeList(data.missionControlWarnings), ...safeList(data.runtimeWarnings)] : [];
  const handoffs: Array<CodeLearningActionDto | CodeLearningHandoffDto> = data
    ? [...safeList(data.recommendedActions), ...safeList(data.tutorHandoffs), ...safeList(data.quizHandoffs), ...safeList(data.reviewHandoffs), ...safeList(data.wikiHandoffs), ...safeList(data.notebookHandoffs)]
    : [];

  return (
    <PanelScaffold
      moduleKey="code"
      title="Kod IDE"
      eyebrow="Kod pratiği ve öğrenme"
      icon={Code2}
      summary={data?.userSafeSummary ?? "Kodu çalıştır, hatayı öğrenme sinyaline dönüştür ve Tutor'a bağla."}
      aside={<StatusPill label={data?.runtimeReadiness?.status ?? (state.error ? "blocked" : "limited")} />}
    >
      {state.loading && <LoadingBlock label="IDE hazırlanıyor" />}
      {!state.loading && state.error && <EmptyBlock title="IDE sınırlı" detail="Editör kullanılabilir; öğrenme bağlamı henüz yüklenmedi." />}
      {data && (
        <>
          <MetricGrid
            items={[
              { label: "Mod",          value: labelize(data.mode),                          detail: data.activeSkill ?? data.activeTopic ?? activeTopic?.title },
              { label: "Runtime",      value: labelize(data.runtimeReadiness.status),       detail: data.runtimeReadiness.decision },
              { label: "Son deneme",   value: labelize(data.lastAttemptSummary.status),     detail: data.lastAttemptSummary.safeTutorSummary },
              { label: "Tekrar hata",  value: data.repeatedErrorSummary.repetitionCount,   detail: data.repeatedErrorSummary.repairSuggestion },
            ]}
          />
          <WarningList warnings={warnings} title="Çalışma ortamı uyarıları" />
          <ActionList actions={handoffs} onViewChange={onViewChange} title="Önerilen adımlar" />
        </>
      )}
      <section
        className="min-h-[560px] overflow-hidden rounded-2xl border"
        style={{ borderColor: "rgba(180,160,240,0.18)", background: "#07090a" }}
      >
        <Suspense fallback={<LoadingBlock label="Loading code editor" />}>
          <InteractiveIDE
            topicTitle={activeTopic?.title}
            topicId={activeTopic?.id}
            sessionId={sessionId ?? undefined}
            quizQuestion={quizQuestion ?? undefined}
            onSendToChat={onSendToTutor}
            onClose={onCloseQuiz}
          />
        </Suspense>
      </section>
      {pendingMessage && (
        <button type="button" onClick={onPendingMessageConsumed} className="hidden">
          Bekleyen sohbet mesajını temizle
        </button>
      )}
    </PanelScaffold>
  );
}
