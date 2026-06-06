import { useEffect, useState, type ReactNode } from "react";
import {
  Activity,
  BookOpen,
  CheckCircle2,
  Code2,
  FileText,
  Image as ImageIcon,
  Layers3,
  Link2,
  ListChecks,
  Loader2,
  MessageSquareText,
  Network,
  Sparkles,
  Target,
  Wrench,
} from "lucide-react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import remarkMath from "remark-math";
import rehypeKatex from "rehype-katex";
import { safeMarkdownComponents, safeMarkdownUrlTransform } from "@/lib/contentSafety";
import type { ChatResponseMetadata, LearningArtifactDto, LearningWorkspaceState, TeachingArtifact, TutorTraceTimelineEvent } from "@/lib/types";
import { TutorAPI } from "@/services/api";
import { statusTone, userSafeStatus } from "@/lib/userSafeStatus";

type Tone = "good" | "watch" | "bad" | "neutral";

const toneClass: Record<Tone, string> = {
  good: "border-[#8fb7a2]/35 bg-[#f2faf5]/80 text-[#47725d]",
  watch: "border-[#e8c46f]/35 bg-[#fff8ee]/86 text-[#8a641f]",
  bad: "border-[#d9a9a0]/45 bg-[#fff3f0]/86 text-[#9a4b3f]",
  neutral: "border-[#526d82]/12 bg-white/64 text-[#344054]",
};

export function WorkspaceHeader({
  eyebrow,
  title,
  description,
  action,
  meta,
}: {
  eyebrow: string;
  title: string;
  description?: string;
  action?: ReactNode;
  meta?: ReactNode;
}) {
  return (
    <div className="flex flex-shrink-0 flex-col gap-4 border-b border-[#526d82]/10 bg-white/38 px-5 py-4 backdrop-blur-xl lg:flex-row lg:items-center lg:justify-between">
      <div>
        <p className="flex items-center gap-2 text-[10px] font-black uppercase tracking-[0.18em] text-[#52768a]">
          <Sparkles className="h-3.5 w-3.5" />
          {eyebrow}
        </p>
        <h1 className="mt-1 text-2xl font-black tracking-tight text-[#172033]">{title}</h1>
        {description && <p className="mt-1 max-w-3xl text-sm leading-6 text-[#667085]">{description}</p>}
        {meta && <div className="mt-3 flex flex-wrap gap-2">{meta}</div>}
      </div>
      {action && <div className="flex flex-wrap items-center gap-2">{action}</div>}
    </div>
  );
}

export function StatusPill({ label, value, tone }: { label?: string; value: string; tone?: Tone }) {
  const resolvedTone = tone ?? statusTone(value);
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-[11px] font-black ${toneClass[resolvedTone]}`}>
      {label && <span className="text-current/65">{label}</span>}
      {userSafeStatus(value)}
    </span>
  );
}

export function NextActionCard({
  title,
  reason,
  primaryLabel,
  onPrimary,
  secondary,
}: {
  title: string;
  reason: string;
  primaryLabel: string;
  onPrimary: () => void;
  secondary?: ReactNode;
}) {
  return (
    <section className="rounded-[1.75rem] border border-[#526d82]/12 bg-[#f7f4ec]/78 p-5 shadow-sm">
      <div className="grid gap-5 lg:grid-cols-[1.35fr_0.8fr] lg:items-center">
        <div>
          <p className="mb-2 flex items-center gap-2 text-[10px] font-black uppercase tracking-[0.18em] text-[#52768a]">
            <Target className="h-3.5 w-3.5" />
            Sıradaki en iyi adım
          </p>
          <h2 className="text-2xl font-black tracking-tight text-[#172033]">{title}</h2>
          <p className="mt-2 max-w-2xl text-sm leading-6 text-[#5f6f7b]">{reason}</p>
        </div>
        <div className="flex flex-col gap-2">
          <button
            onClick={onPrimary}
            className="inline-flex items-center justify-center gap-2 rounded-xl bg-[#172033] px-4 py-3 text-xs font-black text-white shadow-sm transition hover:bg-[#243044]"
          >
            <MessageSquareText className="h-4 w-4" />
            {primaryLabel}
          </button>
          {secondary}
        </div>
      </div>
    </section>
  );
}

export function SourceHealthStrip({
  status,
  label,
  detail,
  citationCoverage,
  unsupportedCitationCount,
}: {
  status?: string;
  label: string;
  detail?: string;
  citationCoverage?: number;
  unsupportedCitationCount?: number;
}) {
  return (
    <div className="rounded-2xl border border-[#526d82]/12 bg-white/62 p-4">
      <div className="mb-2 flex items-center justify-between gap-2">
        <p className="flex items-center gap-2 text-[10px] font-black uppercase tracking-[0.16em] text-[#667085]">
          <Link2 className="h-3.5 w-3.5 text-[#52768a]" />
          Kaynak sağlığı
        </p>
        {status && <StatusPill value={status} />}
      </div>
      <p className="text-sm font-black text-[#172033]">{label}</p>
      {detail && <p className="mt-1 text-xs leading-5 text-[#667085]">{detail}</p>}
      {(typeof citationCoverage === "number" || typeof unsupportedCitationCount === "number") && (
        <div className="mt-3 grid grid-cols-2 gap-2 text-[11px]">
          <div className="rounded-xl bg-[#dcecf3]/58 px-3 py-2">
            <span className="block font-black text-[#172033]">%{Math.round((citationCoverage ?? 0) * 100)}</span>
            <span className="text-[#667085]">citation kapsamı</span>
          </div>
          <div className="rounded-xl bg-[#fff8ee]/82 px-3 py-2">
            <span className="block font-black text-[#172033]">{unsupportedCitationCount ?? 0}</span>
            <span className="text-[#667085]">desteksiz citation</span>
          </div>
        </div>
      )}
    </div>
  );
}

export function AgentStatusRail({
  metadata,
  sessionId,
  topicTitle,
  isThinking,
  workspaceState,
}: {
  metadata?: ChatResponseMetadata | null;
  sessionId?: string | null;
  topicTitle?: string | null;
  isThinking?: boolean;
  workspaceState?: LearningWorkspaceState | null;
}) {
  const [events, setEvents] = useState<TutorTraceTimelineEvent[]>([]);
  const [lastId, setLastId] = useState("0-0");
  const enabled = Boolean(sessionId && (metadata?.tutorTurnStateId || metadata?.tutorActionTraceId || metadata?.toolStatuses?.length || metadata?.usedTools?.length || isThinking));

  useEffect(() => {
    if (!sessionId || !enabled) return;
    let cancelled = false;
    let timer: number | undefined;
    const load = async () => {
      try {
        const timeline = await TutorAPI.getSessionTimeline(sessionId, lastId, 20);
        if (cancelled) return;
        if (timeline.events?.length) {
          setEvents((prev) => {
            const known = new Set(prev.map((item) => item.streamId));
            const next = timeline.events.filter((item) => !known.has(item.streamId));
            return [...prev, ...next].slice(-8);
          });
          setLastId(timeline.lastEventId || lastId);
        }
      } catch {
        // Trace is best-effort; learning flow stays usable.
      } finally {
        if (!cancelled) timer = window.setTimeout(load, 3500);
      }
    };
    load();
    return () => {
      cancelled = true;
      if (timer) window.clearTimeout(timer);
    };
  }, [sessionId, enabled, lastId]);

  const tools = metadata?.toolStatuses ?? [];
  const sourceCount = metadata?.evidenceSummary?.sourceCount ?? metadata?.citations?.length ?? 0;
  const currentPlanStep = workspaceState?.currentPlanStep;
  const activeConcept =
    currentPlanStep?.conceptLabel ||
    currentPlanStep?.conceptKey ||
    workspaceState?.activeLessonSnapshot?.activeConceptLabel ||
    workspaceState?.activeLessonSnapshot?.activeConceptKey ||
    metadata?.activeConceptKey ||
    topicTitle ||
    "Henuz kavram secilmedi";
  const sourceReadiness =
    workspaceState?.sourceReadiness ||
    metadata?.sourceReadiness ||
    metadata?.planSourceReadiness ||
    metadata?.evidenceSummary?.groundingStatus ||
    null;
  const nextAction = workspaceState?.nextActions?.[0]?.userSafeLabel ?? metadata?.tutorNextLearningActions?.[0] ?? null;
  const recentArtifacts = workspaceState?.recentArtifacts ?? [];
  const runtimeHealth = workspaceState?.runtimeHealth ?? null;
  const degradedToolCount = workspaceState?.toolGovernanceSummary
    ? (workspaceState.toolGovernanceSummary.deniedCount ?? 0) + (workspaceState.toolGovernanceSummary.degradedCount ?? 0)
    : 0;

  return (
    <aside className="hidden min-h-0 w-[320px] flex-shrink-0 flex-col border-l border-white/[0.08] bg-[#0b0e10]/84 p-4 xl:flex">
      <div className="mb-3">
        <p className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[#6ed7ce]">Bu turda Orka</p>
        <h2 className="mt-1 text-base font-semibold text-[#f4f6f3]">Kısa çalışma özeti</h2>
      </div>

      <div className="space-y-3 overflow-y-auto pr-1">
        <RailCard icon={<Target className="h-4 w-4" />} label="Odak" value={activeConcept} />
        <RailCard icon={<BookOpen className="h-4 w-4" />} label="Kaynaklı açıklama" value={sourceCount > 0 ? `${sourceCount} kaynak sinyali kullanıldı` : "Kaynak bekleniyor"} />
        <RailCard icon={<Activity className="h-4 w-4" />} label="Kısa kontrol" value={metadata?.evidenceSummary?.learnerEvidenceStatus ? userSafeStatus(metadata.evidenceSummary.learnerEvidenceStatus) : "Bir mikro soru iyi olur"} />
        {currentPlanStep && (
          <RailCard
            icon={<ListChecks className="h-4 w-4" />}
            label="Sıradaki adım"
            value={currentPlanStep.title || currentPlanStep.objective || "Plan adımı hazırlanıyor"}
          />
        )}
        {sourceReadiness && (
          <RailCard icon={<Network className="h-4 w-4" />} label="Kaynak durumu" value={userSafeStatus(sourceReadiness)} />
        )}
        {nextAction && (
          <RailCard icon={<Sparkles className="h-4 w-4" />} label="Öneri" value={nextAction} />
        )}
        {(recentArtifacts.length > 0 || degradedToolCount > 0 || (workspaceState?.staleWarnings?.length ?? 0) > 0) && (
          <div className="rounded-2xl border border-white/[0.08] bg-white/[0.035] p-3">
            <p className="mb-2 flex items-center gap-2 text-[11px] font-semibold uppercase tracking-[0.14em] text-[#8f9894]">
              <Layers3 className="h-3.5 w-3.5" />
              Oturum özeti
            </p>
            <div className="grid grid-cols-3 gap-2 text-[11px]">
              <div className="rounded-xl bg-white/[0.045] px-2 py-2 text-center">
                <span className="block text-sm font-semibold text-[#f4f6f3]">{recentArtifacts.length}</span>
                <span className="text-[#8f9894]">çıktı</span>
              </div>
              <div className="rounded-xl bg-white/[0.045] px-2 py-2 text-center">
                <span className="block text-sm font-semibold text-[#f4f6f3]">{workspaceState?.staleWarnings?.length ?? 0}</span>
                <span className="text-[#8f9894]">uyarı</span>
              </div>
              <div className="rounded-xl bg-white/[0.045] px-2 py-2 text-center">
                <span className="block text-sm font-semibold text-[#f4f6f3]">{degradedToolCount}</span>
                <span className="text-[#8f9894]">kontrol</span>
              </div>
            </div>
          </div>
        )}

        <ReasoningTimeline events={events} isThinking={isThinking} />
      </div>
    </aside>
  );
}

function RailCard({ icon, label, value }: { icon: ReactNode; label: string; value: string }) {
  return (
    <div className="rounded-2xl border border-white/[0.08] bg-white/[0.035] p-3 shadow-[inset_0_1px_0_rgba(255,255,255,0.035)]">
      <p className="mb-1 flex items-center gap-2 text-[10px] font-semibold uppercase tracking-[0.14em] text-[#8f9894]">
        {icon}
        {label}
      </p>
      <p className="text-sm font-semibold leading-5 text-[#eef2ee]">{value}</p>
    </div>
  );
}

export function ReasoningTimeline({ events, isThinking }: { events?: TutorTraceTimelineEvent[]; isThinking?: boolean }) {
  const visible = events?.slice(-6) ?? [];
  return (
    <div className="rounded-2xl border border-white/[0.08] bg-white/[0.03] p-3">
      <p className="mb-3 flex items-center gap-2 text-[11px] font-semibold uppercase tracking-[0.14em] text-[#8f9894]">
        <Activity className="h-3.5 w-3.5" />
        Canlı iz
      </p>
      {isThinking && visible.length === 0 && (
        <div className="flex items-center gap-2 rounded-xl bg-[#6ed7ce]/10 px-3 py-2 text-xs font-semibold text-[#9adfd9]">
          <Loader2 className="h-3.5 w-3.5 animate-spin" />
          Orka hazırlanıyor
        </div>
      )}
      {!isThinking && visible.length === 0 && (
        <p className="text-xs leading-5 text-[#8f9894]">Kaynak veya kalite sinyali oluştuğunda burada sade iz görünür.</p>
      )}
      <div className="space-y-2">
        {visible.map((event) => (
          <div key={`${event.streamId}-${event.id}`} className="rounded-xl bg-white/[0.045] px-3 py-2 text-xs">
            <div className="flex items-center justify-between gap-2">
              <span className="font-semibold text-[#eef2ee]">{event.userSafeLabel}</span>
              <span className="rounded-full bg-white/[0.06] px-2 py-0.5 text-[10px] font-semibold text-[#8f9894]">
                {userSafeStatus(event.eventGroup)}
              </span>
            </div>
            <p className="mt-1 leading-5 text-[#9aa3a0]">{event.userSafeDetail}</p>
          </div>
        ))}
      </div>
    </div>
  );
}

export function ArtifactCanvas({
  artifacts,
  learningArtifacts,
  compact = false,
}: {
  artifacts?: TeachingArtifact[];
  learningArtifacts?: LearningArtifactDto[];
  compact?: boolean;
}) {
  useEffect(() => {
    artifacts?.forEach((artifact) => {
      if (!artifact.renderedAt) TutorAPI.markArtifactRendered(artifact.id).catch(() => undefined);
    });
  }, [artifacts]);

  const hasLegacyArtifacts = Boolean(artifacts?.length);
  const hasLearningArtifacts = Boolean(learningArtifacts?.length);

  if (!hasLegacyArtifacts && !hasLearningArtifacts) {
    return (
      <div className="rounded-2xl border border-dashed border-[#526d82]/16 bg-white/46 p-5 text-center">
        <Layers3 className="mx-auto h-5 w-5 text-[#8ba8b5]" />
        <p className="mt-2 text-sm font-black text-[#172033]">Çalışma alanı hazır</p>
        <p className="mt-1 text-xs leading-5 text-[#667085]">Tutor diagram, tablo, kaynak kartı veya mikro quiz ürettiğinde burada kalıcı görünür.</p>
      </div>
    );
  }

  return (
    <div className={compact ? "space-y-3" : "space-y-4"}>
      {learningArtifacts?.map((artifact) => (
        <div key={`learning-${artifact.id}`} className="overflow-hidden rounded-2xl border border-[#526d82]/12 bg-white/72 shadow-sm">
          <div className="flex flex-wrap items-center justify-between gap-2 border-b border-[#526d82]/10 px-4 py-3">
            <div className="flex items-center gap-2 text-xs font-black text-[#172033]">
              {artifact.renderFormat.includes("code") ? <Code2 className="h-4 w-4 text-[#52768a]" /> : artifact.sourceBasis.includes("source") || artifact.sourceBasis.includes("wiki") ? <Network className="h-4 w-4 text-[#52768a]" /> : <ImageIcon className="h-4 w-4 text-[#52768a]" />}
              <span>{artifact.title || userSafeStatus(artifact.artifactType)}</span>
            </div>
            <div className="flex flex-wrap gap-1.5">
              <StatusPill value={artifact.sourceBasis} />
              <StatusPill value={artifact.artifactStatus} />
            </div>
          </div>
          <div className="px-4 py-4 text-sm leading-6 text-[#344054]">
            <ReactMarkdown
              remarkPlugins={[remarkGfm, remarkMath]}
              rehypePlugins={[rehypeKatex]}
              urlTransform={safeMarkdownUrlTransform}
              components={safeMarkdownComponents}
            >
              {artifact.safeContent || artifact.accessibility?.summary || "Bu artifact guvenli ozet bekliyor."}
            </ReactMarkdown>
            {(artifact.safety?.warnings?.length > 0 || artifact.accessibility?.issues?.length > 0) && (
              <div className="mt-3 rounded-xl border border-[#e8c46f]/30 bg-[#fff8ee] px-3 py-2 text-xs font-bold text-[#8a641f]">
                {[...(artifact.safety?.warnings ?? []), ...(artifact.accessibility?.issues ?? [])].slice(0, 2).map(userSafeStatus).join(" · ")}
              </div>
            )}
          </div>
        </div>
      ))}
      {artifacts?.map((artifact) => (
        <div key={artifact.id} className="overflow-hidden rounded-2xl border border-[#526d82]/12 bg-white/72 shadow-sm">
          <div className="flex flex-wrap items-center justify-between gap-2 border-b border-[#526d82]/10 px-4 py-3">
            <div className="flex items-center gap-2 text-xs font-black text-[#172033]">
              {artifact.artifactType.includes("code") ? <Code2 className="h-4 w-4 text-[#52768a]" /> : artifact.artifactType.includes("source") || artifact.artifactType.includes("evidence") ? <Network className="h-4 w-4 text-[#52768a]" /> : <ImageIcon className="h-4 w-4 text-[#52768a]" />}
              <span>{artifact.title || userSafeStatus(artifact.artifactType)}</span>
            </div>
            <StatusPill value={artifact.status || artifact.artifactType} />
          </div>
          <div className="px-4 py-4 text-sm leading-6 text-[#344054]">
            <ReactMarkdown
              remarkPlugins={[remarkGfm, remarkMath]}
              rehypePlugins={[rehypeKatex]}
              urlTransform={safeMarkdownUrlTransform}
              components={safeMarkdownComponents}
            >
              {artifact.content}
            </ReactMarkdown>
            {artifact.renderError && (
              <div className="mt-3 rounded-xl border border-[#e8c46f]/30 bg-[#fff8ee] px-3 py-2 text-xs font-bold text-[#8a641f]">
                Gösterim güvenli moda geçti: {userSafeStatus(artifact.renderError)}
              </div>
            )}
          </div>
        </div>
      ))}
    </div>
  );
}

export function WorkspaceMetric({ icon, label, value, detail, tone = "neutral" }: { icon?: ReactNode; label: string; value: string | number; detail?: string; tone?: Tone }) {
  return (
    <div className={`rounded-2xl border p-4 ${toneClass[tone]}`}>
      <div className="mb-3 flex items-center gap-2 text-[10px] font-black uppercase tracking-[0.14em] opacity-75">
        {icon ?? <CheckCircle2 className="h-3.5 w-3.5" />}
        {label}
      </div>
      <p className="text-2xl font-black text-[#172033]">{value}</p>
      {detail && <p className="mt-1 text-xs leading-5 text-[#667085]">{detail}</p>}
    </div>
  );
}

export function WorkspaceEmpty({ title, body, action }: { title: string; body: string; action?: ReactNode }) {
  return (
    <div className="rounded-[1.5rem] border border-dashed border-[#526d82]/16 bg-white/48 px-6 py-10 text-center">
      <FileText className="mx-auto h-6 w-6 text-[#8ba8b5]" />
      <h3 className="mt-3 text-base font-black text-[#172033]">{title}</h3>
      <p className="mx-auto mt-2 max-w-md text-sm leading-6 text-[#667085]">{body}</p>
      {action && <div className="mt-5 flex justify-center">{action}</div>}
    </div>
  );
}
