import { useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
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
import InteractiveIDE from "./InteractiveIDE";

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

const EMPTY_STATE = "Orka needs a little real learning evidence before it can personalize this surface.";

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
    return "border-[#c46a6a]/30 bg-[#fff1f1] text-[#8a3f3f]";
  }
  if (key.includes("warning") || key.includes("limited") || key.includes("thin") || key.includes("stale") || key.includes("degraded")) {
    return "border-[#d7ad54]/35 bg-[#fff8e8] text-[#7a5a1f]";
  }
  if (key.includes("ready") || key.includes("stable") || key.includes("clean") || key.includes("passed")) {
    return "border-[#75a884]/30 bg-[#f1faf3] text-[#3f6f52]";
  }
  return "border-[#526d82]/16 bg-white/70 text-[#52606d]";
}

function priorityTone(priority?: string | null) {
  const key = normalizeKey(priority);
  if (key.includes("urgent") || key.includes("high")) return "border-[#c46a6a]/30 bg-[#fff1f1] text-[#8a3f3f]";
  if (key.includes("medium") || key.includes("normal")) return "border-[#d7ad54]/35 bg-[#fff8e8] text-[#7a5a1f]";
  return "border-[#526d82]/16 bg-white/70 text-[#52606d]";
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
        <span key={item} className="rounded-md border border-[#526d82]/12 bg-[#eef4f5]/70 px-2 py-1 text-[10px] font-bold text-[#60707c]">
          {item}
        </span>
      ))}
    </div>
  );
}

function PanelScaffold({
  title,
  eyebrow,
  icon: Icon,
  summary,
  children,
  aside,
}: {
  title: string;
  eyebrow: string;
  icon: typeof MessageSquare;
  summary?: string | null;
  children: ReactNode;
  aside?: ReactNode;
}) {
  return (
    <div className="flex h-full flex-col overflow-y-auto bg-[#f6f8f8] text-[#172033]">
      <header className="border-b border-[#526d82]/10 bg-white/72 px-5 py-4">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div className="min-w-0">
            <div className="flex items-center gap-2 text-[11px] font-black uppercase tracking-[0.18em] text-[#667085]">
              <Icon className="h-4 w-4" />
              {eyebrow}
            </div>
            <h1 className="mt-2 text-2xl font-black tracking-normal text-[#172033]">{title}</h1>
            {summary && <p className="mt-2 max-w-3xl text-sm leading-6 text-[#52606d]">{summary}</p>}
          </div>
          {aside}
        </div>
      </header>
      <main className="grid gap-4 px-5 py-4">{children}</main>
    </div>
  );
}

function LoadingBlock({ label = "Loading learning OS state" }: { label?: string }) {
  return (
    <div className="flex min-h-[180px] items-center justify-center rounded-lg border border-[#526d82]/12 bg-white/72">
      <div className="flex items-center gap-2 text-sm font-bold text-[#667085]">
        <Loader2 className="h-4 w-4 animate-spin" />
        {label}
      </div>
    </div>
  );
}

function EmptyBlock({ title = "Thin evidence", detail = EMPTY_STATE }: { title?: string; detail?: string }) {
  return (
    <div className="rounded-lg border border-[#d7ad54]/24 bg-[#fff8e8]/70 p-4">
      <div className="flex items-start gap-3">
        <AlertTriangle className="mt-0.5 h-4 w-4 text-[#8a641f]" />
        <div>
          <h3 className="text-sm font-black text-[#172033]">{title}</h3>
          <p className="mt-1 text-sm leading-6 text-[#5f6f7b]">{detail}</p>
        </div>
      </div>
    </div>
  );
}

function MetricGrid({ items }: { items: Array<{ label: string; value?: string | number | null; detail?: string | null }> }) {
  return (
    <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
      {items.map((item) => (
        <div key={item.label} className="rounded-lg border border-[#526d82]/12 bg-white/78 p-3">
          <div className="text-[11px] font-black uppercase tracking-[0.14em] text-[#7b8a95]">{item.label}</div>
          <div className="mt-2 text-lg font-black text-[#172033]">{item.value ?? "none"}</div>
          {item.detail && <div className="mt-1 text-xs leading-5 text-[#667085]">{item.detail}</div>}
        </div>
      ))}
    </div>
  );
}

function WarningList({ warnings, title = "Warnings" }: { warnings: WarningLike[]; title?: string }) {
  const visible = warnings.filter((warning) => warning.label || warning.warningCode);
  if (visible.length === 0) return null;
  return (
    <section className="rounded-lg border border-[#c46a6a]/20 bg-[#fff6f4] p-4">
      <h3 className="flex items-center gap-2 text-sm font-black text-[#8a3f3f]">
        <AlertTriangle className="h-4 w-4" />
        {title}
      </h3>
      <div className="mt-3 grid gap-2">
        {visible.slice(0, 5).map((warning, index) => (
          <div key={`${warning.warningCode ?? "warning"}-${index}`} className="rounded-md border border-[#c46a6a]/14 bg-white/70 p-3">
            <div className="flex flex-wrap items-center gap-2">
              <StatusPill label={warning.severity ?? "warning"} />
              <span className="text-sm font-bold text-[#172033]">{warning.label || labelize(warning.warningCode)}</span>
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
      className={`inline-flex items-center justify-between gap-2 rounded-lg border border-[#526d82]/12 bg-white px-3 py-2 text-left text-sm font-black text-[#172033] shadow-sm transition hover:border-[#87a9b5]/50 hover:bg-[#f8fbfb] ${
        compact ? "min-w-[160px]" : "w-full"
      }`}
    >
      <span className="min-w-0">
        <span className="block truncate">{action.label || labelize(action.actionType ?? action.handoffType)}</span>
        {!compact && action.reason && <span className="mt-0.5 block text-xs font-semibold leading-5 text-[#667085]">{action.reason}</span>}
      </span>
      <ArrowRight className="h-4 w-4 shrink-0 text-[#60707c]" />
    </button>
  );
}

function ActionList({ actions, onViewChange, title = "Recommended actions" }: { actions: ActionLike[]; onViewChange: (view: string) => void; title?: string }) {
  const visible = actions.filter((action) => action.label || action.actionType || action.handoffType);
  if (visible.length === 0) return <EmptyBlock title="No queued action" detail="This module has no urgent action from the current Learning OS evidence." />;
  return (
    <section className="rounded-lg border border-[#526d82]/12 bg-white/78 p-4">
      <h3 className="mb-3 text-sm font-black text-[#172033]">{title}</h3>
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
    { moduleKey: "tutor", label: "Tutor", status: "ready", entryPoint: "ask_tutor", targetRoute: "chat", priority: "normal", userSafeSummary: "Ask for a guided explanation or repair step.", actionCount: 1, warningCount: 0, reasonCodes: [] },
    { moduleKey: "study_room", label: "Study Room", status: "limited", entryPoint: "open_study_room", targetRoute: "classroom", priority: "normal", userSafeSummary: "Use a personal AI study room when topic context exists.", actionCount: 1, warningCount: 0, reasonCodes: [] },
    { moduleKey: "review", label: "Review / Quiz", status: "ready", entryPoint: "review_due_concept", targetRoute: "review", priority: "normal", userSafeSummary: "Review due concepts and checkpoint progress.", actionCount: 1, warningCount: 0, reasonCodes: [] },
    { moduleKey: "exam", label: "Exam War Room", status: "limited", entryPoint: "practice_exam_outcome", targetRoute: "central-exams", priority: "normal", userSafeSummary: "Inspect exam weak outcomes without success claims.", actionCount: 1, warningCount: 0, reasonCodes: [] },
    { moduleKey: "sources", label: "Sources / Wiki Pro", status: "limited", entryPoint: "source_review", targetRoute: "sources", priority: "normal", userSafeSummary: "Check source, Wiki, and citation readiness.", actionCount: 1, warningCount: 0, reasonCodes: [] },
    { moduleKey: "notebook", label: "Notebook Studio", status: "limited", entryPoint: "open_notebook_pack", targetRoute: "notebook-studio", priority: "low", userSafeSummary: "Create bounded study packs and previews.", actionCount: 1, warningCount: 0, reasonCodes: [] },
    { moduleKey: "code", label: "Code Learning IDE", status: "limited", entryPoint: "code_learning", targetRoute: "code-learning", priority: "normal", userSafeSummary: "Practice code with runtime safety status visible.", actionCount: 1, warningCount: 0, reasonCodes: [] },
    { moduleKey: "progress", label: "Progress / Memory", status: "ready", entryPoint: "progress", targetRoute: "progress", priority: "low", userSafeSummary: "Review durable progress and memory snapshots.", actionCount: 1, warningCount: 0, reasonCodes: [] },
  ];
  const list = cards.length > 0 ? cards : fallback;
  return (
    <section className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
      {list.map((card) => {
        const view = toView(card.targetRoute, card.entryPoint, card.moduleKey);
        const Icon = moduleIconMap[view] ?? Layers;
        return (
          <button
            key={`${card.moduleKey}-${card.label}`}
            type="button"
            onClick={() => onViewChange(view)}
            className="min-h-[148px] rounded-lg border border-[#526d82]/12 bg-white/80 p-4 text-left shadow-sm transition hover:border-[#87a9b5]/60 hover:bg-[#fbfdfd]"
          >
            <div className="flex items-start justify-between gap-3">
              <div className="rounded-lg border border-[#526d82]/12 bg-[#eef4f5]/80 p-2">
                <Icon className="h-4 w-4 text-[#344054]" />
              </div>
              <StatusPill label={card.status} />
            </div>
            <h3 className="mt-3 text-sm font-black text-[#172033]">{card.label || labelize(card.moduleKey)}</h3>
            <p className="mt-1 line-clamp-2 text-xs leading-5 text-[#667085]">{card.userSafeSummary || "Connected to the Learning OS."}</p>
            <div className="mt-3 flex items-center justify-between gap-2 text-[11px] font-bold text-[#667085]">
              <span>{card.actionCount} actions</span>
              <span>{card.warningCount} warnings</span>
            </div>
          </button>
        );
      })}
    </section>
  );
}

function SectionList({ mission, onViewChange }: { mission: OrkaMissionControlDto; onViewChange: (view: string) => void }) {
  if (!mission.sections?.length) return null;
  return (
    <section className="grid gap-3 lg:grid-cols-2">
      {mission.sections.slice(0, 8).map((section) => (
        <div key={section.sectionKey} className="rounded-lg border border-[#526d82]/12 bg-white/78 p-4">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h3 className="text-sm font-black text-[#172033]">{section.label || labelize(section.sectionKey)}</h3>
            <StatusPill label={section.status} />
          </div>
          <ReasonChips items={section.reasonCodes} />
          <div className="mt-3 grid gap-2">
            {section.actions.slice(0, 2).map((action, index) => (
              <ActionButton key={`${section.sectionKey}-${index}`} action={action} onViewChange={onViewChange} />
            ))}
            {section.actions.length === 0 && <p className="text-xs font-semibold text-[#667085]">No visible action in this section yet.</p>}
          </div>
        </div>
      ))}
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
  const learningState = state.learningState ?? state.today?.orkaLearningState ?? null;
  const primary = mission?.primaryMission;
  const signal = learningState?.signalSummary;
  const warnings = [
    ...(mission?.urgentWarnings ?? []),
    ...(coach?.warnings ?? []),
    ...(learningState?.conflictWarnings ?? []).map((item: OrkaLearningStateConflictDto) => ({
      warningCode: item.conflictCode,
      severity: item.severity,
      label: item.userSafeSummary,
      targetRoute: "home",
      reasonCodes: item.reasonCodes,
    })),
  ];

  return (
    <PanelScaffold
      title="Home / Mission Control"
      eyebrow="Orka Learning OS"
      icon={Sparkles}
      summary={mission?.userSafeSummary ?? "Open Orka and see the safest next learning move before choosing a module."}
      aside={
        <div className="flex flex-wrap gap-2">
          <StatusPill label={mission?.evidenceConfidence ?? (state.error ? "blocked" : "thin_evidence")} />
          <StatusPill label={coach?.rhythmStatus ?? "thin_evidence"} />
        </div>
      }
    >
      {state.loading && <LoadingBlock />}
      {!state.loading && state.error && (
        <EmptyBlock title="Mission Control is unavailable" detail="The app shell is ready, but the local backend did not return the product contracts yet." />
      )}
      {!state.loading && !state.error && (
        <>
          <section className="grid gap-4 xl:grid-cols-[1.35fr_0.65fr]">
            <div className="rounded-lg border border-[#526d82]/12 bg-white/82 p-5">
              <div className="flex flex-wrap items-center gap-2">
                <StatusPill label={primary?.priority ?? "normal"} tone="priority" />
                <StatusPill label={mission?.primaryEntryPoint ?? primary?.entryPoint ?? "ask_tutor"} />
              </div>
              <h2 className="mt-4 text-3xl font-black tracking-normal text-[#172033]">{primary?.label ?? state.today?.dailyFocusTitle ?? "Start with a short diagnostic"}</h2>
              <p className="mt-3 max-w-3xl text-sm leading-6 text-[#52606d]">{primary?.reason ?? state.today?.dailyFocusReason ?? EMPTY_STATE}</p>
              <div className="mt-4 flex flex-wrap gap-2">
                {primary && <ActionButton action={primary} onViewChange={onViewChange} compact />}
                {(mission?.studyRoomSuggestion ?? null) && <ActionButton action={mission!.studyRoomSuggestion!} onViewChange={onViewChange} compact />}
              </div>
              <div className="mt-4">
                <ReasonChips items={[...(mission?.reasonCodes ?? []), ...(primary?.reasonCodes ?? [])]} />
              </div>
            </div>

            <div className="rounded-lg border border-[#526d82]/12 bg-white/82 p-5">
              <h3 className="text-sm font-black text-[#172033]">Study rhythm</h3>
              <p className="mt-2 text-sm leading-6 text-[#52606d]">{coach?.todayPlan ?? "Use a light start until Orka has enough evidence."}</p>
              <div className="mt-4 grid grid-cols-2 gap-2">
                <StatusPill label={coach?.recommendedPace ?? "light"} />
                <StatusPill label={coach?.focusPlan?.durationBand ?? "short"} />
                <StatusPill label={coach?.focusPlan?.focusMode ?? "quick_start"} />
                <StatusPill label={coach?.comebackPlan?.comebackStatus ?? "thin_evidence"} />
              </div>
            </div>
          </section>

          <MetricGrid
            items={[
              { label: "Review load", value: mission?.reviewLoad ?? coach?.workload?.reviewLoad ?? "none", detail: signal ? `${signal.dueReviewCount} due` : undefined },
              { label: "Repair load", value: mission?.repairLoad ?? coach?.workload?.repairLoad ?? "none", detail: signal ? `${signal.wrongAttemptCount} wrong signals` : undefined },
              { label: "Exam load", value: mission?.examLoad ?? coach?.workload?.examLoad ?? "none" },
              { label: "Source/Wiki", value: mission?.sourceWikiLoad ?? coach?.workload?.sourceWikiLoad ?? "none", detail: signal ? `${signal.readySourceCount}/${signal.sourceCount} sources ready` : undefined },
            ]}
          />

          <WarningList warnings={warnings} title="Urgent warnings" />

          <ActionList actions={mission?.secondaryActions ?? []} onViewChange={onViewChange} title="Secondary actions" />

          <ModuleCardGrid cards={mission?.moduleCards ?? []} onViewChange={onViewChange} />

          {mission && <SectionList mission={mission} onViewChange={onViewChange} />}

          <section className="rounded-lg border border-[#526d82]/12 bg-white/78 p-4">
            <h3 className="text-sm font-black text-[#172033]">Progress snapshot</h3>
            <div className="mt-3 grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
              <StatusPill label={signal?.hasRealLearningData ? "real evidence" : "thin_evidence"} />
              <StatusPill label={`${signal?.quizAttemptCount ?? 0} quiz attempts`} />
              <StatusPill label={`${signal?.learningSignalCount ?? 0} learning signals`} />
              <StatusPill label={`${topics.length} topics`} />
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
      toast.success("Study Room session prepared.");
    } catch {
      toast.error("Study Room could not start safely.");
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
  return (
    <PanelScaffold
      title="Study Room"
      eyebrow="Personal AI study room"
      icon={GraduationCap}
      summary={data?.safeStudentSummary ?? "Use a structured personal study session when Orka has safe topic or lesson context."}
      aside={<StatusPill label={data?.sessionReadiness ?? (state.error ? "blocked" : "thin_evidence")} />}
    >
      {state.loading && <LoadingBlock label="Loading Study Room readiness" />}
      {!state.loading && state.error && <EmptyBlock title="Study Room is limited" detail="The backend did not return a safe study session contract yet." />}
      {!state.loading && data && (
        <>
          <MetricGrid
            items={[
              { label: "Mode", value: labelize(data.studyRoomMode), detail: data.selectedTopic ?? activeTopic?.title ?? undefined },
              { label: "Pace", value: labelize(data.recommendedPace), detail: labelize(data.rhythmStatus) },
              { label: "Source", value: labelize(data.sourceReadiness) },
              { label: "Wiki", value: labelize(data.wikiReadiness) },
            ]}
          />

          <section className="grid gap-4 lg:grid-cols-[1.1fr_0.9fr]">
            <div className="rounded-lg border border-[#526d82]/12 bg-white/78 p-4">
              <h3 className="text-sm font-black text-[#172033]">{data.lessonPlan.title}</h3>
              <p className="mt-2 text-sm leading-6 text-[#52606d]">{data.lessonPlan.objective}</p>
              <ol className="mt-3 grid gap-2">
                {(data.lessonPlan.steps.length ? data.lessonPlan.steps : ["Confirm topic context", "Run one guided checkpoint", "Choose the next safe action"]).slice(0, 5).map((step, index) => (
                  <li key={`${step}-${index}`} className="flex gap-2 rounded-md border border-[#526d82]/10 bg-[#f8fbfb] p-2 text-sm text-[#52606d]">
                    <span className="font-black text-[#172033]">{index + 1}.</span>
                    <span>{step}</span>
                  </li>
                ))}
              </ol>
              <div className="mt-3 flex flex-wrap gap-2">
                <StatusPill label={data.lessonPlan.durationBand} />
                <StatusPill label={data.lessonPlan.stopCondition} />
              </div>
            </div>

            <div className="rounded-lg border border-[#526d82]/12 bg-white/78 p-4">
              <h3 className="text-sm font-black text-[#172033]">Roles and checkpoint</h3>
              <div className="mt-3 grid gap-2">
                {data.roles.map((role) => (
                  <div key={role.roleKey} className="rounded-md border border-[#526d82]/10 bg-[#f8fbfb] p-2">
                    <div className="font-black text-[#172033]">{role.label}</div>
                    <p className="text-xs leading-5 text-[#667085]">{role.responsibility}</p>
                  </div>
                ))}
              </div>
              <div className="mt-3 rounded-md border border-[#526d82]/10 bg-[#f8fbfb] p-3">
                <div className="flex flex-wrap items-center gap-2">
                  <StatusPill label={data.checkpointPlan.checkpointStatus} />
                  <StatusPill label={data.checkpointPlan.keyVisible ? "submitted feedback" : "answer key hidden"} />
                </div>
                <p className="mt-2 text-sm leading-6 text-[#52606d]">{data.checkpointPlan.prompt}</p>
              </div>
              <div className="mt-3 flex flex-wrap gap-2">
                <button type="button" onClick={startSession} className="inline-flex items-center gap-2 rounded-lg bg-[#172033] px-3 py-2 text-sm font-black text-white">
                  <Play className="h-4 w-4" />
                  Start session
                </button>
                <button type="button" onClick={submitCheckpoint} disabled={!data.classroomSessionId} className="inline-flex items-center gap-2 rounded-lg border border-[#526d82]/14 bg-white px-3 py-2 text-sm font-black text-[#172033] disabled:opacity-50">
                  <CheckCircle2 className="h-4 w-4" />
                  Record checkpoint
                </button>
              </div>
            </div>
          </section>

          <WarningList warnings={data.warnings} />
          <ActionList actions={[...data.nextActions, ...data.tutorHandoffs, ...data.quizHandoffs, ...data.reviewHandoffs, ...data.sourceWikiHandoffs, ...data.notebookHandoffs]} onViewChange={onViewChange} />
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
  const warnings: ExamWarRoomWarningDto[] = data ? [...data.sourceWikiWarnings, ...data.curriculumCoverageWarnings, ...data.conflictWarnings] : [];
  return (
    <PanelScaffold
      title="Exam War Room"
      eyebrow="Exam preparation"
      icon={GraduationCap}
      summary={data?.userSafeSummary ?? "Inspect exam weak outcomes, deneme patterns, due outcomes, and practice handoffs without score or success promises."}
      aside={<StatusPill label={data?.readinessStatus ?? (state.error ? "blocked" : "thin_exam_evidence")} />}
    >
      {state.loading && <LoadingBlock label="Loading exam evidence" />}
      {!state.loading && state.error && <EmptyBlock title="Exam War Room is limited" detail="The exam prep command center did not return a safe contract yet." />}
      {!state.loading && data && (
        <>
          <section className="rounded-lg border border-[#526d82]/12 bg-white/82 p-5">
            <div className="flex flex-wrap items-center gap-2">
              <StatusPill label={data.activeExam.displayName || data.activeExam.examCode} />
              <StatusPill label={data.activeExam.canClaimOfficial ? data.activeExam.verificationStatus : "verification limited"} />
            </div>
            <h2 className="mt-4 text-2xl font-black text-[#172033]">{data.todayExamMission.label}</h2>
            <p className="mt-2 text-sm leading-6 text-[#52606d]">{data.todayExamMission.reason}</p>
            <div className="mt-3">
              <ActionButton action={data.todayExamMission} onViewChange={onViewChange} compact />
            </div>
          </section>

          <MetricGrid
            items={[
              { label: "Weak outcomes", value: data.weakOutcomes.length },
              { label: "Due outcomes", value: data.dueOutcomes.length },
              { label: "Deneme clusters", value: data.denemeMistakeClusters.length },
              { label: "Practice queue", value: data.recommendedPracticeQueue.length },
            ]}
          />

          <WarningList warnings={warnings} title="Source and curriculum warnings" />

          <section className="grid gap-4 xl:grid-cols-3">
            <CompactList title="Weak outcomes" items={data.weakOutcomes.map((item) => ({ title: item.label, detail: item.userSafeSummary || item.recommendedAction, status: item.readinessStatus }))} />
            <CompactList title="Deneme patterns" items={data.denemeMistakeClusters.map((item) => ({ title: item.label, detail: item.recommendedAction, status: `${item.mistakeCount} mistakes` }))} />
            <CompactList title="Question type gaps" items={data.weakQuestionTypes.map((item) => ({ title: labelize(item.questionType), detail: item.recommendedAction, status: item.readinessStatus }))} />
          </section>

          <ActionList actions={[...data.recommendedPracticeQueue, ...data.tutorRepairHandoffs, ...data.studyRoomHandoffs]} onViewChange={onViewChange} />
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

export function SourceWikiProPanel({ activeTopic, sessionId, onViewChange }: BasePanelProps) {
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
  const warnings: SourceWikiProWarningDto[] = data ? [...data.citationWarnings, ...data.examWarRoomWarnings, ...data.missionControlWarnings, ...data.conflictWarnings] : [];
  return (
    <PanelScaffold
      title="Sources / Wiki Pro"
      eyebrow="Evidence workspace"
      icon={Network}
      summary={data?.userSafeSummary ?? "Review source readiness, Wiki repair needs, citation limits, and safe evidence handoffs."}
      aside={<StatusPill label={data?.readinessStatus ?? (state.error ? "blocked" : "thin_evidence")} />}
    >
      {state.loading && <LoadingBlock label="Loading source and Wiki evidence" />}
      {!state.loading && state.error && <EmptyBlock title="Evidence workspace is limited" detail="The Source / Wiki Pro contract did not return yet." />}
      {!state.loading && data && (
        <>
          <MetricGrid
            items={[
              { label: "Sources", value: `${data.evidenceMap.readySourceCount}/${data.evidenceMap.uploadedSourceCount}`, detail: labelize(data.sourceReadiness) },
              { label: "Wiki pages", value: data.evidenceMap.wikiPageCount, detail: labelize(data.wikiReadiness) },
              { label: "Citations", value: data.evidenceMap.citationWarningCount, detail: labelize(data.citationReadiness) },
              { label: "Notebook pack", value: labelize(data.notebookPackReadiness) },
            ]}
          />
          <WarningList warnings={warnings} title="Evidence warnings" />
          <section className="grid gap-4 xl:grid-cols-3">
            <CompactList title="Source attention" items={[...data.staleSources, ...data.deletedSources, ...data.insufficientSources, ...data.degradedSources].map((item) => ({ title: item.title, detail: `${item.pageCount} pages, ${item.linkedConceptCount} links`, status: item.sourceReadiness }))} />
            <CompactList title="Wiki repair" items={data.wikiRepairPages.map((item) => ({ title: item.title, detail: item.nextAction, status: item.curationStatus }))} />
            <CompactList title="Source-backed concepts" items={data.sourceBackedConcepts.map((item) => ({ title: item.conceptTitle || item.conceptKey, detail: item.sourceTitle, status: item.evidenceStatus }))} />
          </section>
          <ActionList actions={[data.todaySourceWikiMission, ...data.recommendedActions, ...data.tutorHandoffs, ...data.studyRoomHandoffs, ...data.notebookHandoffs]} onViewChange={onViewChange} />
        </>
      )}
    </PanelScaffold>
  );
}

export function NotebookStudioProPanel({ activeTopic, sessionId, onViewChange }: BasePanelProps) {
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
        ...data.tutorHandoffs,
        ...data.reviewHandoffs,
        ...data.sourceWikiHandoffs,
        ...data.examWarRoomHandoffs,
        ...data.studyRoomHandoffs,
        ...data.recommendedPacks.flatMap((pack) => pack.actions ?? []),
      ]
    : [];
  const warnings: NotebookStudioPackWarningDto[] = data ? [...data.missionControlWarnings, ...data.warnings] : [];

  return (
    <PanelScaffold
      title="Notebook Studio"
      eyebrow="Artifact Pro Pack"
      icon={FileText}
      summary={data?.userSafeSummary ?? "Turn safe learning evidence into study packs, previews, and next handoffs."}
      aside={<StatusPill label={data?.packReadiness ?? (state.error ? "blocked" : "thin_evidence")} />}
    >
      {state.loading && <LoadingBlock label="Loading artifact workspace" />}
      {!state.loading && state.error && <EmptyBlock title="Notebook Studio Pro is limited" detail="The artifact pack contract did not return yet." />}
      {!state.loading && data && (
        <>
          <MetricGrid
            items={[
              { label: "Recommended packs", value: data.recommendedPacks.length },
              { label: "Artifacts", value: data.artifactQueue.length },
              { label: "Export previews", value: data.exportPreviews.length, detail: "Preview-only unless the backend says otherwise" },
              { label: "Evidence links", value: data.sourceEvidenceLinks.length + data.wikiEvidenceLinks.length + data.conceptLinks.length },
            ]}
          />
          <WarningList warnings={warnings.map((item) => ({ ...item, targetRoute: "notebook" }))} title="Pack warnings" />
          <section className="grid gap-4 xl:grid-cols-3">
            <CompactList title="Recommended packs" items={data.recommendedPacks.map((pack) => ({ title: pack.title, detail: pack.summary, status: pack.packType }))} />
            <CompactList title="Artifact queue" items={data.artifactQueue.map((artifact) => ({ title: artifact.title, detail: artifact.previewOnly ? "Preview only" : artifact.renderFormat, status: artifact.sourceBasis }))} />
            <CompactList title="Export previews" items={data.exportPreviews.map((preview) => ({ title: labelize(preview.previewType), detail: preview.exportLimitations.join(", ") || "No limitation reported", status: preview.readinessStatus }))} />
          </section>
          <ActionList actions={actions} onViewChange={onViewChange} />
        </>
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
  const warnings: CodeLearningWarningDto[] = data ? [...data.missionControlWarnings, ...data.runtimeWarnings] : [];
  const handoffs: Array<CodeLearningActionDto | CodeLearningHandoffDto> = data
    ? [...data.recommendedActions, ...data.tutorHandoffs, ...data.quizHandoffs, ...data.reviewHandoffs, ...data.wikiHandoffs, ...data.notebookHandoffs]
    : [];

  return (
    <PanelScaffold
      title="Code Learning IDE"
      eyebrow="Learning-aware runtime"
      icon={Code2}
      summary={data?.userSafeSummary ?? "Practice code while Orka tracks safe runtime readiness, repeated errors, and learning handoffs."}
      aside={<StatusPill label={data?.runtimeReadiness?.status ?? (state.error ? "blocked" : "limited")} />}
    >
      {state.loading && <LoadingBlock label="Loading code learning state" />}
      {!state.loading && state.error && <EmptyBlock title="Code IDE learning state is limited" detail="The editor remains available, but the Learning OS contract did not return yet." />}
      {data && (
        <>
          <MetricGrid
            items={[
              { label: "Mode", value: labelize(data.mode), detail: data.activeSkill ?? data.activeTopic ?? activeTopic?.title },
              { label: "Runtime", value: labelize(data.runtimeReadiness.status), detail: data.runtimeReadiness.decision },
              { label: "Last attempt", value: labelize(data.lastAttemptSummary.status), detail: data.lastAttemptSummary.safeTutorSummary },
              { label: "Repeated errors", value: data.repeatedErrorSummary.repetitionCount, detail: data.repeatedErrorSummary.repairSuggestion },
            ]}
          />
          <WarningList warnings={warnings} title="Runtime warnings" />
          <ActionList actions={handoffs} onViewChange={onViewChange} />
        </>
      )}
      <section className="min-h-[560px] overflow-hidden rounded-lg border border-[#526d82]/12 bg-white">
        <InteractiveIDE
          topicTitle={activeTopic?.title}
          topicId={activeTopic?.id}
          sessionId={sessionId ?? undefined}
          quizQuestion={quizQuestion ?? undefined}
          onSendToChat={onSendToTutor}
          onClose={onCloseQuiz}
        />
      </section>
      {pendingMessage && (
        <button type="button" onClick={onPendingMessageConsumed} className="hidden">
          Clear pending Tutor message
        </button>
      )}
    </PanelScaffold>
  );
}
