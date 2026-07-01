/*
 * ChatMessage — AI ve kullanıcı mesajlarını render eder.
 * AI mesajı "quiz" tipindeyse react-markdown ÇALIŞMAZ; QuizCard gösterilir.
 * Diğer AI mesajları prose-invert + react-markdown + syntax highlighting ile gösterilir.
 */

import { useState, useCallback, useEffect, useRef, memo } from "react";
import { motion } from "framer-motion";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import remarkMath from "remark-math";
import rehypeKatex from "rehype-katex";
import { Prism as SyntaxHighlighter } from "react-syntax-highlighter";
import { vscDarkPlus } from "react-syntax-highlighter/dist/esm/styles/prism";
import "katex/dist/katex.min.css";
import { Check, Copy, BookOpen, CheckCircle, Volume2, Wrench, AlertTriangle, Link2, Image as ImageIcon } from "lucide-react";
import type { ChatMessage as ChatMessageType, CitationDto, TeachingArtifact, TutorTraceTimelineEvent } from "@/lib/types";
import { TutorAPI } from "@/services/api";
import { tryParseQuiz } from "@/lib/quizParser";
import { userSafeStatus } from "@/lib/userSafeStatus";
import { citationDisplayTitle, citationPrimaryLabel, citationScopeSummary, evidenceQualityDetail, evidenceQualityLabel } from "@/lib/citationDisplay";
import {
  BlockedImagePlaceholder,
  displayHost,
  isAllowedRemoteImage,
  isMermaidTooLarge,
  isSafeMarkdownUrl,
  safeMarkdownComponents,
  safeMarkdownUrlTransform,
  sanitizeMermaidSvg,
} from "@/lib/contentSafety";
import QuizCard from "./QuizCard";
import OrcaLogo from "./OrcaLogo";
import ClassroomAudioPlayer from "./ClassroomAudioPlayer";

interface ChatMessageProps {
  message: ChatMessageType;
  topicId?: string;
  sessionId?: string;
  onPlanComplete?: (completion: {
    planGenerated: boolean;
    generatedPlanRootTopicId?: string;
    generatedTopicIds?: string[];
    message?: string;
    score?: number;
    total?: number;
    skipped?: boolean;
  }) => void;
  /** Kullanıcının gerçek adı (API'den alınır). */
  userName?: string;
  /** Konu tamamlama kartındaki wiki butonu için. */
  onOpenWiki?: (topicId: string) => void;
  /** IDE'yi quiz sorusuyla açma tetikleyicisi */
  onOpenIDE?: (question?: string) => void;
}

function formatTime(date: Date): string {
  return date.toLocaleTimeString("tr-TR", {
    hour: "2-digit",
    minute: "2-digit",
  });
}

// ── Code block with syntax highlighting + copy button ──────────────────────

function CodeBlock({
  children,
  className,
}: {
  children: React.ReactNode;
  className?: string;
}) {
  const [copied, setCopied] = useState(false);
  const language = className?.replace("language-", "") || "text";
  const codeText =
    typeof children === "string"
      ? children.replace(/\n$/, "")
      : String(children).replace(/\n$/, "");

  const handleCopy = useCallback(() => {
    navigator.clipboard.writeText(codeText).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  }, [codeText]);

  return (
    <div className="relative my-4 rounded-xl overflow-hidden border border-[#2b2b2b] bg-[#1e1e1e] shadow-md">
      {/* Header bar */}
      <div className="flex items-center justify-between px-4 py-2 bg-[#2d2d2d] border-b border-[#3e3e3e]">
        <span className="text-[11px] font-mono text-[#a0a0a0] uppercase tracking-wider">
          {language}
        </span>
        <button
          onClick={handleCopy}
          className="flex items-center gap-1.5 text-[11px] text-[#a0a0a0] hover:text-white transition-colors duration-150 px-2 py-0.5 rounded hover:bg-[#3e3e3e]"
        >
          {copied ? (
            <>
              <Check className="w-3.5 h-3.5 text-emerald-400" />
              <span className="text-emerald-400">Kopyalandı ✔</span>
            </>
          ) : (
            <>
              <Copy className="w-3.5 h-3.5" />
              <span>Kopyala</span>
            </>
          )}
        </button>
      </div>
      {/* Syntax highlighted code */}
      <SyntaxHighlighter
        language={language}
        style={vscDarkPlus}
        customStyle={{
          margin: 0,
          padding: "1rem",
          background: "#1e1e1e",
          fontSize: "13px",
          lineHeight: "1.6",
          borderRadius: 0,
        }}
        codeTagProps={{ style: { fontFamily: "'JetBrains Mono', 'Fira Code', Consolas, monospace" } }}
        wrapLongLines={false}
      >
        {codeText}
      </SyntaxHighlighter>
    </div>
  );
}

function InlineCode({ children }: { children: React.ReactNode }) {
  return (
    <code className="text-[#8ce6df] bg-white/[0.06] border border-white/[0.08] px-1.5 py-0.5 rounded-md text-[13px] font-mono">
      {children}
    </code>
  );
}

// ── Mermaid diyagram render (lazy modül init) ──────────────────────────────
let mermaidInitialized = false;
async function getMermaid() {
  const m = (await import("mermaid")).default;
  if (!mermaidInitialized) {
    m.initialize({
      startOnLoad: false,
      theme: "dark",
      securityLevel: "strict",
      htmlLabels: false,
      fontFamily: "ui-sans-serif, system-ui, sans-serif",
      themeVariables: {
        primaryColor: "#10b981",
        primaryTextColor: "#e4e4e7",
        primaryBorderColor: "#3f3f46",
        lineColor: "#52525b",
        secondaryColor: "#27272a",
        tertiaryColor: "#18181b",
        background: "#09090b",
        mainBkg: "#18181b",
        secondBkg: "#27272a",
      },
    });
    mermaidInitialized = true;
  }
  return m;
}

function sanitizeMermaid(code: string) {
  return code
    .replace(/([A-Za-z0-9_]+)\[([^\]\n"]*[\(\):.;,][^\]\n"]*)\]/g, (_match, node, label) => {
      const escaped = String(label).replace(/"/g, '\\"');
      return `${node}["${escaped}"]`;
    })
    .replace(/([A-Za-z0-9_]+)\(([^\)\n"]*[\[\]:.;,][^\)\n"]*)\)/g, (_match, node, label) => {
      const escaped = String(label).replace(/"/g, '\\"');
      return `${node}("${escaped}")`;
    });
}

function looksLikeMermaidFailure(svg: string) {
  return /syntax error|parse error|mermaid version|error-icon|flowchart-v2-pointEnd/i.test(svg);
}

function MermaidFallback({ code }: { code: string }) {
  return (
    <div className="my-4 rounded-xl p-4 text-xs" style={{ background: "var(--orka-surface)", border: "1px solid var(--orka-border)", color: "var(--orka-text-3)" }}>
      <div className="mb-2 font-semibold" style={{ color: "var(--orka-text-2)" }}>Diyagram metin olarak gosteriliyor</div>
      <div className="mb-3 leading-5">Mermaid bu ciktiyi guvenli sekilde cizemedi. Icerik kaybolmadi; kod blogu olarak birakildi.</div>
      <pre className="max-h-80 overflow-auto rounded-lg p-3 text-[11px] leading-5" style={{ background: "var(--orka-surface-2)", color: "var(--orka-text-2)" }}>{code}</pre>
    </div>
  );
}

function MermaidBlock({ code }: { code: string }) {
  const ref = useRef<HTMLDivElement>(null);
  const idRef = useRef("m_" + Math.random().toString(36).slice(2, 9));
  const [fallback, setFallback] = useState(false);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        if (isMermaidTooLarge(code)) {
          setFallback(true);
          return;
        }

        const m = await getMermaid();
        const renderOrThrow = async (id: string, source: string) => {
          const rendered = await m.render(id, source);
          if (looksLikeMermaidFailure(rendered.svg)) {
            throw new Error("Mermaid returned an error SVG.");
          }
          return sanitizeMermaidSvg(rendered.svg);
        };
        let svg: string;
        try {
          svg = await renderOrThrow(idRef.current, code.trim());
        } catch {
          svg = await renderOrThrow(`${idRef.current}_safe`, sanitizeMermaid(code.trim()));
        }
        if (!svg) throw new Error("Mermaid SVG did not pass sanitization.");
        if (!cancelled && ref.current) {
          ref.current.innerHTML = svg;
          setFallback(false);
        }
      } catch (err) {
        if (!cancelled) {
          console.warn("Mermaid render fallback:", err);
          setFallback(true);
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [code]);

  return fallback ? (
    <MermaidFallback code={code} />
  ) : (
    <div
      ref={ref}
      className="my-4 p-4 rounded-xl overflow-x-auto"
      style={{ background: "var(--orka-surface-2)", border: "1px solid var(--orka-border)" }}
    />
  );
}

// ── Inline citation link → favicon + host preview ──────────────────────────
function CitationAnchor({ href, children }: { href?: string; children: React.ReactNode }) {
  if (href?.startsWith("orka-source://")) {
    return (
      <span className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded-full bg-amber-500/10 border border-amber-500/25 text-[11px] font-mono text-amber-300 align-baseline">
        {children}
      </span>
    );
  }
  if (href === "orka-wiki://local" || href === "orka-web://local") {
    return (
      <span className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded-full bg-emerald-500/10 border border-emerald-500/25 text-[11px] font-mono text-emerald-300 align-baseline">
        {children}
      </span>
    );
  }
  if (!isSafeMarkdownUrl(href)) {
    return <span>{children}</span>;
  }
  const host = displayHost(href);
  return (
    <a
      href={href}
      target="_blank"
      rel="noopener noreferrer nofollow"
      title={href}
      className="inline-flex items-center gap-1 text-emerald-400 hover:text-emerald-300 underline decoration-emerald-500/40 decoration-dotted underline-offset-2 transition"
    >
      {children}
      <span
        className="inline-flex items-center gap-1 ml-0.5 px-1.5 py-0.5 rounded-full bg-emerald-500/10 border border-emerald-500/20 text-[10px] font-mono text-emerald-300/80 align-text-bottom"
        aria-hidden="true"
      >
        {host}
      </span>
    </a>
  );
}

function withSourceLinks(content: string): string {
  return content
    .replace(/\[doc:([0-9a-fA-F-]{36}):p(\d+)\]/g, (_m, sourceId, page) =>
      `[doc:p${page}](orka-source://${sourceId}/page/${page})`
    )
    .replace(/\[wiki(?::[^\]]+)?\]/g, "[wiki](orka-wiki://local)")
    .replace(/\[web(?::[^\]]+)?\]/g, "[web](orka-web://local)");
}

function sanitizeVisibleChatContent(content: string) {
  return content
    .replace(/\[SKIP_QUIZ\]/gi, "")
    .replace(/\[PLAN_READY\]/gi, "")
    .replace(/\[IDE_OPEN\]/gi, "")
    .replace(/\[TOPIC_COMPLETE:[^\]]+\]/gi, "")
    .replace(/\*\*Quiz Cevab(?:ı|\u00c4\u00b1)m:\*\*[\s\S]*$/i, "")
    .trim();
}

const TECHNICAL_LABELS: Record<string, string> = {
  fallback: "güvenli mod",
  degraded: "sınırlı veri",
  degraded_fallback: "güvenli mod",
  provider_missing: "araç hazır değil",
  provider_disabled: "araç kapalı",
  planned_optional: "gerektiğinde kullanılacak",
  planned_user_visible: "önerildi",
  needs_input: "bilgi gerekiyor",
  blocked: "engellendi",
  timeout: "zaman aşımı",
  skipped: "atlanmış",
  ready: "hazır",
  success: "hazır",
  source_grounded: "kaynaklı",
  document_grounded: "belge kaynaklı",
  source_grounded_answer: "kaynaklı cevap",
  model_fallback: "genel açıklama",
  real_world_evidence: "gerçek hayat kanıtı",
  explain: "anlatım",
  remediate: "eksik kapatma",
  guided_practice: "rehberli pratik",
  challenge: "zorlayıcı pratik",
  review: "tekrar",
  code_lab: "kod pratiği",
  visualize: "görselleştirme",
  summarize: "özet",
  concise: "kısa yanıt",
  standard: "standart anlatım",
  deep: "derin anlatım",
  recovery: "telafi modu",
  evidence_limited: "kanıt sınırlı",
  evidence_limited_caution: "kaynak sınırlı",
  evidence_weak_verify: "kaynağı kontrol et",
  model_ok_no_source_claim: "kaynak iddiası yok",
  unknown_source_caution: "kaynak durumu belirsiz",
  beginner: "temel anlatım",
  intermediate: "orta seviye",
  advanced: "ileri seviye",
  mastery_probability: "ilerleme sinyali",
  weak_concept_signals: "zayıf kavram sinyali",
  low_confidence: "düşük güven",
  step_by_step: "adım adım",
  example_first: "örnekle başla",
  visual: "görsel",
  verbal: "sözel",
  socratic: "sokratik",
};

function formatTechnicalLabel(value?: string | null) {
  return userSafeStatus(value);
}

function TeachingArtifactRenderer({ artifacts }: { artifacts?: TeachingArtifact[] }) {
  useEffect(() => {
    if (!artifacts?.length) return;
    artifacts.forEach((artifact) => {
      if (artifact.renderedAt) return;
      TutorAPI.markArtifactRendered(artifact.id).catch(() => {
        // Best-effort telemetry only.
      });
    });
  }, [artifacts]);

  if (!artifacts?.length) return null;

  return (
    <div className="mt-3 space-y-2">
      {artifacts.map((artifact) => (
        <div
          key={artifact.id}
          className="overflow-hidden rounded-xl"
          style={{ background: "var(--orka-surface)", border: "1px solid var(--orka-border)", color: "var(--orka-text-2)" }}
        >
          <div className="flex flex-wrap items-center justify-between gap-2 px-3 py-2" style={{ borderBottom: "1px solid var(--orka-border-3)" }}>
            <div className="flex items-center gap-2 text-xs font-semibold" style={{ color: "var(--orka-text-2)" }}>
              <ImageIcon className="h-3.5 w-3.5" style={{ color: "var(--orka-teal)" }} />
              <span>{artifact.title || artifact.artifactType}</span>
            </div>
            <span className="rounded-full px-2 py-0.5 text-[10px] font-medium" style={{ background: "var(--orka-surface-3)", color: "var(--orka-text-4)" }}>
              {artifact.artifactType}
            </span>
          </div>
          <div className="px-3 py-3 text-sm leading-6">
            {artifact.renderFormat === "mermaid" ? (
              <MermaidBlock code={artifact.content.replace(/```mermaid|```/g, "").trim()} />
            ) : (
              <ReactMarkdown
                remarkPlugins={[remarkGfm, remarkMath]}
                rehypePlugins={[rehypeKatex]}
                urlTransform={safeMarkdownUrlTransform}
                components={{
                  ...safeMarkdownComponents,
                  code({ className, children }) {
                    const lang = /language-(\w+)/.exec(className || "")?.[1];
                    if (lang === "mermaid") return <MermaidBlock code={String(children)} />;
                    return <CodeBlock className={className}>{children}</CodeBlock>;
                  },
                }}
              >
                {artifact.content}
              </ReactMarkdown>
            )}
            {artifact.renderError && (
              <div className="mt-2 rounded-lg border border-amber-500/20 bg-amber-50 px-3 py-2 text-xs font-semibold text-amber-700">
                Gösterim güvenli moda geçti: {formatTechnicalLabel(artifact.renderError)}
              </div>
            )}
          </div>
        </div>
      ))}
    </div>
  );
}

function ChatMetadataChips({ metadata }: { metadata: ChatMessageType["metadata"] }) {
  const tools = metadata?.usedTools ?? [];
  const citations = metadata?.citations ?? [];
  const warnings = metadata?.providerWarnings ?? [];
  const toolDecision = metadata?.tutorToolDecision;
  const lessonDelivery = metadata?.tutorLessonDelivery;
  const adaptiveDiagnostic = metadata?.adaptiveDiagnostic;
  const coursePlanQuality = metadata?.coursePlanQuality;
  const memoryHygiene = metadata?.learningMemoryHygiene;
  const wikiCuration = metadata?.wikiCuration;
  const hasMeta = false;
  if (!hasMeta) return null;

  return (
    <div className="mt-2 flex flex-wrap items-center gap-1.5">
      {metadata?.groundingMode && (
        <span className="inline-flex items-center gap-1 rounded-full border border-[#526d82]/12 bg-[#eef1f3]/78 px-2 py-1 text-[10px] font-bold text-[#667085]">
          <Link2 className="h-3 w-3" />
          {formatTechnicalLabel(metadata.groundingMode)}
        </span>
      )}
      {metadata?.teachingMode && (
        <span
          title={metadata.nextCheckPrompt ?? undefined}
          className="inline-flex items-center gap-1 rounded-full border border-emerald-500/20 bg-emerald-50 px-2 py-1 text-[10px] font-bold text-emerald-700"
        >
          <BookOpen className="h-3 w-3" />
          {formatTechnicalLabel(metadata.teachingMode)}
        </span>
      )}
      {toolDecision?.selectedAction && (
        <span
          title={[toolDecision.studentVisibleSummary, ...(toolDecision.reasonCodes ?? []), ...(toolDecision.safetyWarnings ?? [])].filter(Boolean).map(formatTechnicalLabel).join(" | ") || undefined}
          className={`inline-flex items-center gap-1 rounded-full border px-2 py-1 text-[10px] font-bold ${
            (toolDecision.blockedTools?.length ?? 0) > 0 || (toolDecision.safetyWarnings?.length ?? 0) > 0
              ? "border-amber-500/25 bg-amber-50 text-amber-700"
              : "border-[#8fb7a2]/28 bg-[#f2faf5]/70 text-[#47725d]"
          }`}
        >
          <Wrench className="h-3 w-3" />
          tool: {formatTechnicalLabel(toolDecision.selectedAction)}
        </span>
      )}
      {lessonDelivery?.deliveryMode && (
        <span
          title={[lessonDelivery.studentVisibleSummary, lessonDelivery.structure?.goal, ...(lessonDelivery.warnings ?? [])].filter(Boolean).map(formatTechnicalLabel).join(" | ") || undefined}
          className={`inline-flex items-center gap-1 rounded-full border px-2 py-1 text-[10px] font-bold ${
            (lessonDelivery.warnings?.length ?? 0) > 0
              ? "border-amber-500/25 bg-amber-50 text-amber-700"
              : "border-sky-500/20 bg-sky-50 text-sky-700"
          }`}
        >
          <BookOpen className="h-3 w-3" />
          ders: {formatTechnicalLabel(lessonDelivery.deliveryMode)}
        </span>
      )}
      {(adaptiveDiagnostic?.planReadiness || coursePlanQuality?.readinessStatus) && (
        <span
          title={[
            adaptiveDiagnostic?.placement?.userSafeLabel,
            adaptiveDiagnostic?.nextAction,
            coursePlanQuality?.recommendedNextAction,
            ...(adaptiveDiagnostic?.warnings ?? []),
            ...(coursePlanQuality?.warnings ?? []),
          ].filter(Boolean).map(formatTechnicalLabel).join(" | ") || undefined}
          className={`inline-flex items-center gap-1 rounded-full border px-2 py-1 text-[10px] font-bold ${
            (coursePlanQuality?.overclaimRisk === "high") || (adaptiveDiagnostic?.planReadiness && adaptiveDiagnostic.planReadiness !== "ready")
              ? "border-amber-500/25 bg-amber-50 text-amber-700"
              : "border-[#8fb7a2]/28 bg-[#f2faf5]/70 text-[#47725d]"
          }`}
        >
          <CheckCircle className="h-3 w-3" />
          plan: {formatTechnicalLabel(coursePlanQuality?.readinessStatus ?? adaptiveDiagnostic?.planReadiness ?? "unknown")}
          {adaptiveDiagnostic?.learnerLevel && <span>- {formatTechnicalLabel(adaptiveDiagnostic.learnerLevel)}</span>}
        </span>
      )}
      {metadata?.styleMode && (
        <span
          title={[metadata.affectiveState, metadata.cognitiveLoad].filter(Boolean).join(" · ") || undefined}
          className="inline-flex items-center gap-1 rounded-full border border-[#9ec7d9]/45 bg-[#dcecf3]/72 px-2 py-1 text-[10px] font-bold text-[#2d5870]"
        >
          <CheckCircle className="h-3 w-3" />
          {formatTechnicalLabel(metadata.styleMode)}
        </span>
      )}
      {metadata?.tutorResponseMode && (
        <span
          title={metadata.evidencePolicy ? formatTechnicalLabel(metadata.evidencePolicy) : undefined}
          className="inline-flex items-center gap-1 rounded-full border border-[#e8c46f]/32 bg-[#fff8ee]/75 px-2 py-1 text-[10px] font-bold text-[#8a5f12]"
        >
          <CheckCircle className="h-3 w-3" />
          {formatTechnicalLabel(metadata.tutorResponseMode)}
        </span>
      )}
      {metadata?.tutorTeachingMove && (
        <span
          title={metadata.tutorRemediationPolicy ? formatTechnicalLabel(metadata.tutorRemediationPolicy) : undefined}
          className="inline-flex items-center gap-1 rounded-full border border-indigo-500/18 bg-indigo-50 px-2 py-1 text-[10px] font-bold text-indigo-700"
        >
          <BookOpen className="h-3 w-3" />
          {formatTechnicalLabel(metadata.tutorTeachingMove)}
        </span>
      )}
      {metadata?.tutorGroundingPolicy && (
        <span
          title={metadata.sourceReadiness ? formatTechnicalLabel(metadata.sourceReadiness) : undefined}
          className="inline-flex items-center gap-1 rounded-full border border-[#526d82]/12 bg-white/75 px-2 py-1 text-[10px] font-bold text-[#667085]"
        >
          <Link2 className="h-3 w-3" />
          {formatTechnicalLabel(metadata.tutorGroundingPolicy)}
        </span>
      )}
      {metadata?.personalizationMode && metadata.personalizationMode !== "unknown" && (
        <span
          title={metadata.masteryBasis ? formatTechnicalLabel(metadata.masteryBasis) : undefined}
          className="inline-flex items-center gap-1 rounded-full border border-[#8fb7a2]/28 bg-[#f2faf5]/70 px-2 py-1 text-[10px] font-bold text-[#47725d]"
        >
          <BookOpen className="h-3 w-3" />
          {formatTechnicalLabel(metadata.personalizationMode)}
        </span>
      )}
      {memoryHygiene?.memoryStatus && (
        <span
          title={memoryHygiene.studentVisibleSummary}
          className="inline-flex items-center gap-1 rounded-full border border-[#8fb7a2]/24 bg-[#f2faf5]/72 px-2 py-1 text-[10px] font-bold text-[#47725d]"
        >
          <BookOpen className="h-3 w-3" />
          hafiza {formatTechnicalLabel(memoryHygiene.memoryStatus)}
        </span>
      )}
      {wikiCuration?.curationStatus && (
        <span
          title={wikiCuration.studentVisibleSummary}
          className="inline-flex items-center gap-1 rounded-full border border-[#e8c46f]/28 bg-[#fff8ee]/75 px-2 py-1 text-[10px] font-bold text-[#8a5f12]"
        >
          <CheckCircle className="h-3 w-3" />
          wiki {formatTechnicalLabel(wikiCuration.curationStatus)}
        </span>
      )}
      {tools.slice(0, 6).map((tool, index) => (
        <span
          key={`${tool.toolId ?? tool.name ?? "tool"}-${index}`}
          title={[tool.safeMessage, tool.fallbackReason, tool.evidence].filter(Boolean).join(" · ")}
          className={`inline-flex items-center gap-1 rounded-full border px-2 py-1 text-[10px] font-bold ${
            tool.success === false || tool.fallbackUsed
              ? "border-[#e8c46f]/35 bg-[#fff8ee]/85 text-[#8a641f]"
              : "border-[#9ec7d9]/45 bg-[#dcecf3]/72 text-[#2d5870]"
          }`}
        >
          <Wrench className="h-3 w-3" />
          {tool.toolId ?? tool.name ?? "tool"}
          {tool.provider && <span className="font-medium opacity-70">{tool.provider}</span>}
        </span>
      ))}
      {citations.length > 0 && (
        <span className="inline-flex items-center gap-1 rounded-full border border-emerald-500/20 bg-emerald-50 px-2 py-1 text-[10px] font-bold text-emerald-700">
          <Link2 className="h-3 w-3" />
          {citations.length} kaynak
        </span>
      )}
      {citations.slice(0, 3).map((citation: CitationDto, index) => {
        const summary = citationScopeSummary(citation);
        if (!summary) return null;
        return (
          <span
            key={`${citation.citationId ?? citation.label ?? "citation"}-${index}-scope`}
            title={citationDisplayTitle(citation)}
            className="inline-flex items-center gap-1 rounded-full border border-[#9ec7d9]/45 bg-white/75 px-2 py-1 text-[10px] font-bold text-[#2d5870]"
          >
            <Link2 className="h-3 w-3" />
            {citationPrimaryLabel(citation)} · {summary}
          </span>
        );
      })}
      {(metadata?.fallbackReason || warnings.length > 0) && (
        <span
          title={[metadata?.fallbackReason, ...warnings].filter(Boolean).map(formatTechnicalLabel).join(" · ")}
          className="inline-flex items-center gap-1 rounded-full border border-amber-500/25 bg-amber-50 px-2 py-1 text-[10px] font-bold text-amber-700"
        >
          <AlertTriangle className="h-3 w-3" />
          {formatTechnicalLabel(metadata?.fallbackReason ?? warnings[0] ?? "fallback")}
        </span>
      )}
    </div>
  );
}

// ── Main component ─────────────────────────────────────────────────────────

function LiveTutorTrace({ sessionId, enabled }: { sessionId?: string; enabled: boolean }) {
  const [events, setEvents] = useState<TutorTraceTimelineEvent[]>([]);
  const [lastId, setLastId] = useState("0-0");
  const [source, setSource] = useState<string>("");

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
            return [...prev, ...next].slice(-12);
          });
          setLastId(timeline.lastEventId || lastId);
        }
        setSource(timeline.source);
      } catch {
        // Trace is an explainability layer; the answer itself should not fail.
      } finally {
        if (!cancelled) timer = window.setTimeout(load, 3000);
      }
    };

    load();
    return () => {
      cancelled = true;
      if (timer) window.clearTimeout(timer);
    };
  }, [sessionId, enabled, lastId]);

  if (!enabled || events.length === 0) return null;

  return (
    <details className="mt-2 rounded-2xl border border-[#526d82]/12 bg-white/60 px-4 py-3 text-xs text-[#344054]">
      <summary className="cursor-pointer select-none font-black text-[#172033]">
        Canlı Orka izi {source === "sql_projection" ? "(geçmiş kayıt)" : ""}
      </summary>
      <div className="mt-3 space-y-2">
        {events.slice(-8).map((event) => (
          <div key={`${event.streamId}-${event.id}`} className="rounded-xl bg-[#f7f9fa] px-3 py-2">
            <div className="flex items-center justify-between gap-2">
              <span className="font-bold text-[#172033]">{event.userSafeLabel}</span>
              <span className="rounded-full bg-white px-2 py-0.5 text-[10px] font-bold text-[#667085]">
                {formatTechnicalLabel(event.eventGroup)}
              </span>
            </div>
            <p className="mt-1 leading-5 text-[#667085]">{event.userSafeDetail}</p>
          </div>
        ))}
      </div>
    </details>
  );
}

type LearningTraceTone = "grounded" | "learning" | "watch";

interface LearningTraceSummaryItem {
  label: string;
  detail: string;
  tone: LearningTraceTone;
}

function traceToolLabel(toolId?: string | null, name?: string | null): string {
  const id = (toolId ?? name ?? "tool").toLowerCase();
  if (id.includes("ide") || id.includes("code")) return "kod/pratik çıktısı";
  if (id.includes("source") || id.includes("rag")) return "ders kaynakları";
  if (id.includes("wiki")) return "Wiki hafızası";
  if (id.includes("quiz") || id.includes("assessment")) return "quiz/pratik kanıtı";
  if (id.includes("youtube")) return "pedagojik kaynak";
  if (id.includes("mermaid")) return "görsel açıklama";
  return "araç bağlamı";
}

function buildLearningTraceSummary(metadata: ChatMessageType["metadata"]): LearningTraceSummaryItem[] {
  const tools = metadata?.usedTools ?? [];
  const toolStatuses = metadata?.toolStatuses ?? [];
  const citations = metadata?.citations ?? [];
  const warnings = metadata?.providerWarnings ?? [];
  const evidenceSummary = metadata?.evidenceSummary;
  const evidenceQuality = metadata?.evidenceQuality;
  const allTools = [...tools, ...toolStatuses];
  const mode = metadata?.groundingMode?.toLowerCase() ?? "";
  const items: LearningTraceSummaryItem[] = [];
  const toolDecision = metadata?.tutorToolDecision;
  const lessonDelivery = metadata?.tutorLessonDelivery;

  if (toolDecision?.selectedAction) {
    items.push({
      label: `Orka aksiyonu: ${formatTechnicalLabel(toolDecision.selectedAction)}`,
      detail: toolDecision.studentVisibleSummary || "Orka bu turda güvenli bir sonraki adımı seçti.",
      tone: (toolDecision.blockedTools?.length ?? 0) > 0 || (toolDecision.safetyWarnings?.length ?? 0) > 0 ? "watch" : "learning",
    });
  }

  if (lessonDelivery?.deliveryMode) {
    items.push({
      label: `Ders modu: ${formatTechnicalLabel(lessonDelivery.deliveryMode)}`,
      detail: lessonDelivery.studentVisibleSummary || lessonDelivery.structure?.goal || "Orka bu turda hedefli bir akış kullandı.",
      tone: (lessonDelivery.warnings?.length ?? 0) > 0 ? "watch" : "learning",
    });
  }

  if (metadata?.learningMemoryHygiene?.memoryStatus && items.length < 3) {
    items.push({
      label: `Ogrenme hafizasi: ${formatTechnicalLabel(metadata.learningMemoryHygiene.memoryStatus)}`,
      detail: metadata.learningMemoryHygiene.studentVisibleSummary || "Orka ham metin yerine güvenli özet hafızayı kullanıyor.",
      tone: (metadata.learningMemoryHygiene.warnings?.length ?? 0) > 0 ? "watch" : "learning",
    });
  }

  if (metadata?.wikiCuration?.curationStatus && items.length < 3) {
    items.push({
      label: `Wiki hygiene: ${formatTechnicalLabel(metadata.wikiCuration.curationStatus)}`,
      detail: metadata.wikiCuration.studentVisibleSummary || "Wiki izleri curasyon durumuyla birlikte tutuluyor.",
      tone: (metadata.wikiCuration.warnings?.length ?? 0) > 0 ? "watch" : "learning",
    });
  }

  if (evidenceQuality?.status && ["partial", "weak", "missing"].includes(evidenceQuality.status.toLowerCase())) {
    const status = evidenceQuality.status.toLowerCase();
    items.push({
      label: evidenceQualityLabel(evidenceQuality),
      detail: status === "partial" || status === "weak"
        ? "Kaynak güveni sınırlı; cevabı kontrol ederek kullan."
        : evidenceQualityDetail(evidenceQuality),
      tone: "watch",
    });
  } else if (citations.length > 0 || (evidenceSummary?.sourceCount ?? 0) > 0 || mode.includes("source") || mode.includes("wiki")) {
    items.push({
      label: "Bu cevap kaynaklarla desteklendi.",
      detail: citations.length > 0
        ? `Orka bu cevabı ${citations.length} kaynak işaretiyle mevcut ders kaynaklarıyla ilişkilendirdi.`
        : "Orka bu cevabı mevcut ders kaynaklarıyla ilişkilendirdi.",
      tone: "grounded",
    });
  } else if (metadata?.fallbackReason || warnings.length > 0 || metadata?.sourceQualityStatus === "degraded" || metadata?.ragQualityStatus === "degraded") {
    items.push({
      label: "Orka sınırı gösterdi; kaynak veya sağlayıcı güveni düşük olabilir.",
      detail: "Cevap güvenli modda tutuldu; kaynak bulunamadığında kesinlik iddiası kurulmadı.",
      tone: "watch",
    });
  } else if (allTools.length > 0) {
    const firstTool = allTools[0];
    items.push({
      label: "Orka bu cevabı mevcut çalışma bağlamıyla ilişkilendirdi.",
      detail: `${traceToolLabel(firstTool.toolId, "name" in firstTool ? firstTool.name : firstTool.toolId)} cevap bağlamına eklendi.`,
      tone: "grounded",
    });
  }

  const hasPracticeEvidence = allTools.some((tool) => {
    const id = (tool.toolId ?? ("name" in tool ? tool.name : "") ?? "").toLowerCase();
    return id.includes("quiz") || id.includes("assessment") || id.includes("ide") || id.includes("code");
  });

  if (hasPracticeEvidence || evidenceSummary?.learnerEvidenceStatus) {
    items.push({
      label: "Quiz/pratik kanıtı güncellendi.",
      detail: evidenceSummary?.learnerEvidenceStatus
        ? `Öğrenen kanıtı: ${formatTechnicalLabel(evidenceSummary.learnerEvidenceStatus)}.`
        : "Bu turdaki pratik çıktısı sonraki çalışma kararlarına bağlanabilir.",
      tone: "learning",
    });
  } else if (metadata?.remediationLesson) {
    const lesson = metadata.remediationLesson;
    items.push({
      label: "Telafi dersi hazir.",
      detail: `${formatTechnicalLabel(lesson.repairType ?? "guided_reteach")} - ${lesson.studentVisibleSummary ?? lesson.lessonShape?.goal ?? "Kisa tekrar, cozumlu ornek ve mikro kontrol hazir."}`,
      tone: "learning",
    });
  } else if (metadata?.misconceptionSignal || metadata?.remediationSeed) {
    const signal = metadata.misconceptionSignal;
    const seed = metadata.remediationSeed;
    const confidence = metadata.learningSignalConfidence?.status ?? seed?.confidenceStatus;
    items.push({
      label: "Yanılgı sinyali güvenli şekilde işlendi.",
      detail: `${signal?.userSafeLabel ?? seed?.userSafeMisconceptionLabel ?? "Kesin teşhis değil; telafi sinyali olarak tutuldu."}${confidence ? ` Kanıt: ${formatTechnicalLabel(confidence)}.` : ""}`,
      tone: "learning",
    });
  } else if (typeof metadata?.masteryProbability === "number") {
    items.push({
      label: "Bu turda öğrenme izi güncellendi.",
      detail: "Orka bunu kesin öğrenildi iddiası olarak göstermez; sonraki adımı daha temkinli seçer.",
      tone: "learning",
    });
  } else if (metadata?.personalizationMode && metadata.personalizationMode !== "unknown") {
    items.push({
      label: "Anlatım seviyesi mevcut ilerlemene göre ayarlandı.",
      detail: metadata.masteryBasis
        ? `Dayanak: ${formatTechnicalLabel(metadata.masteryBasis)}.`
        : "Orka bu turda anlatım seviyesini güvenli varsayılanla tuttu.",
      tone: "learning",
    });
  } else if (metadata?.teachingMode || metadata?.nextCheckPrompt || metadata?.activeConceptKey) {
    items.push({
      label: "Bu turda zayıf kavram / öğrenme sinyali oluşmuş olabilir.",
      detail: metadata.nextCheckPrompt
        ? `Sonraki kontrol: ${metadata.nextCheckPrompt}`
        : `${metadata.activeConceptKey ? `${metadata.activeConceptKey} kavramı` : "Bu konu"} için kısa kontrol sorusu faydalı olabilir.`,
      tone: "learning",
    });
  }

  if (metadata?.currentPlanStepTitle && items.length < 3) {
    items.push({
      label: "Aktif plan adımı bağlandı.",
      detail: `${metadata.currentPlanStepTitle}${metadata.currentPlanTutorMove ? ` - Orka hamlesi: ${formatTechnicalLabel(metadata.currentPlanTutorMove)}.` : "."}`,
      tone: "learning",
    });
  }

  if ((metadata?.sourceReadiness || metadata?.planSourceReadiness) && items.length < 3) {
    items.push({
      label: "Kaynak durumu gorunur tutuldu.",
      detail: `Durum: ${formatTechnicalLabel(metadata.sourceReadiness ?? metadata.planSourceReadiness)}. Kanit zayifsa Orka kaynak iddiasi kurmaz.`,
      tone: "watch",
    });
  }

  if (metadata?.tutorNextLearningActions?.length && items.length < 3) {
    items.push({
      label: "Siradaki aksiyon hazir.",
      detail: metadata.tutorNextLearningActions.slice(0, 2).join(" · "),
      tone: "learning",
    });
  }

  if (items.length === 0 && metadata) {
    items.push({
      label: "Henüz öğrenme izi oluşmadı.",
      detail: "Bu cevapta kaynak, quiz veya pratik sinyali görünmüyor; Orka sadece mevcut bağlamı gösteriyor.",
      tone: "watch",
    });
  }

  return items.slice(0, 3);
}

function LearningTraceSummaryLite({
  metadata,
  sessionId,
  compact = false,
}: {
  metadata: ChatMessageType["metadata"];
  sessionId?: string;
  compact?: boolean;
}) {
  const summary = buildLearningTraceSummary(metadata);
  const citations = metadata?.citations ?? [];
  const toolStatuses = metadata?.toolStatuses ?? [];
  const tools = metadata?.usedTools ?? [];
  if (summary.length === 0) return null;

  const toneClass = {
    grounded: "border-[#6ed7ce]/25 bg-[#6ed7ce]/10",
    learning: "border-[#a7e879]/20 bg-[#a7e879]/10",
    watch: "border-[#dac17a]/25 bg-[#dac17a]/10",
  } satisfies Record<LearningTraceTone, string>;

  return (
    <div className={`${compact ? "mt-3" : "mt-2"} rounded-2xl border border-white/[0.08] bg-white/[0.035] px-4 py-3 text-[#c8cfca] shadow-sm`}>
      <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
        <p className="text-[10px] font-semibold uppercase tracking-[0.16em] text-[#6ed7ce]">
          Orka bu turda
        </p>
        {citations.length > 0 && (
          <span className="rounded-full bg-white/[0.06] px-2.5 py-1 text-[10px] font-semibold text-[#a7e879]">
            {citations.length} kaynak işareti
          </span>
        )}
      </div>
      <div className={`grid gap-2 text-xs leading-5 ${compact ? "" : "md:grid-cols-2"}`}>
        {summary.map((item) => (
          <p key={item.label} className={`rounded-xl border px-3 py-2 ${toneClass[item.tone]}`}>
            <span className="block font-semibold text-[#eef2ee]">{item.label}</span>
            <span className="mt-1 block text-[#9aa3a0]">{item.detail}</span>
          </p>
        ))}
      </div>
      {false && !compact && (
        <LiveTutorTrace sessionId={sessionId} enabled={Boolean(metadata?.tutorTurnStateId || metadata?.tutorActionTraceId || toolStatuses.length > 0 || tools.length > 0)} />
      )}
    </div>
  );
}

function ChatLearningTrace({ metadata = {}, sessionId }: { metadata?: NonNullable<ChatMessageType["metadata"]>; sessionId?: string }) {
  const tools = metadata?.usedTools ?? [];
  const toolStatuses = metadata?.toolStatuses ?? [];
  const citations = metadata?.citations ?? [];
  const warnings = metadata?.providerWarnings ?? [];
  const hasMeta = tools.length > 0 || toolStatuses.length > 0 || citations.length > 0 || warnings.length > 0 || metadata?.fallbackReason || metadata?.groundingMode || metadata?.teachingMode || metadata?.tutorResponseMode || metadata?.personalizationMode || metadata?.misconceptionSignal || metadata?.learningSignalConfidence || metadata?.remediationSeed || metadata?.remediationLesson || metadata?.learningMemoryHygiene?.memoryStatus || metadata?.wikiCuration?.curationStatus || metadata?.tutorActionTraceId || metadata?.tutorPedagogyStatus || metadata?.evidenceSummary || metadata?.currentPlanStepTitle || metadata?.sourceReadiness || metadata?.planSourceReadiness || metadata?.tutorNextLearningActions?.length || metadata?.tutorToolDecision?.selectedAction || metadata?.tutorLessonDelivery?.deliveryMode || typeof metadata?.masteryProbability === "number";
  if (!hasMeta) return null;
  metadata = metadata ?? {};

  return <LearningTraceSummaryLite metadata={metadata} sessionId={sessionId} />;

  const toolLabel = (toolId?: string | null, name?: string | null) => {
    const id = (toolId ?? name ?? "tool").toLowerCase();
    if (id.includes("ide") || id.includes("code")) return "IDE";
    if (id.includes("source")) return "Source/RAG";
    if (id.includes("wiki")) return "Wiki";
    if (id.includes("knowledge_entity")) return "Knowledge";
    if (id.includes("geo_context")) return "Geo evidence";
    if (id.includes("socioeconomic")) return "World data";
    if (id.includes("science_context")) return "Science";
    if (id.includes("research_context")) return "Research";
    if (id.includes("forum_signal")) return "Forum signal";
    if (id.includes("news")) return "News";
  if (id.includes("weather")) return "Geography";
    if (id.includes("crypto") || id.includes("market")) return "Crypto";
    if (id.includes("wolfram")) return "Wolfram";
    if (id.includes("youtube")) return "YouTube pedagogy";
    if (id.includes("mermaid")) return "Mermaid";
    return toolId ?? name ?? "tool";
  };

  const firstTool = tools[0] ?? toolStatuses.find(t => t.success) ?? toolStatuses[0];
  const mode = metadata?.groundingMode?.toLowerCase() ?? "";
  const groundingLabel = mode.includes("document") || mode.includes("source")
    ? "Kaynaklarına dayandı"
    : toolStatuses.some((tool) => ["knowledge_entity", "geo_context", "socioeconomic_context", "science_context", "research_context", "forum_signal"].includes((tool.toolId ?? "").toLowerCase()))
      ? "Gerçek hayat kanıt kartıyla desteklendi"
    : mode.includes("wiki")
      ? "Wiki hafızasını kullandı"
      : mode.includes("youtube")
        ? "YouTube'u pedagojik referans olarak kullandı"
        : mode.includes("web") || mode.includes("news") || mode.includes("provider")
          ? "Sağlayıcı verisiyle desteklendi"
          : mode.includes("code") || mode.includes("ide")
            ? "Kod çıktısını bağlam olarak kullandı"
            : formatTechnicalLabel(metadata?.groundingMode) || "Genel açıklama";

  const learningHint = [...tools, ...toolStatuses].some((tool) => (tool.toolId ?? ("name" in tool ? tool.name : "") ?? "").toLowerCase().includes("ide"))
    ? "Kod çıktısı cevap bağlamına girdi. Kalıcı tekrar gerekiyorsa bunu review'a dönüştürebilirsin."
    : metadata?.teachingMode
      ? `Orka bu turda ${formatTechnicalLabel(metadata.teachingMode)} akışını seçti; stil ${formatTechnicalLabel(metadata.styleMode ?? "step_by_step")}, yük ${formatTechnicalLabel(metadata.cognitiveLoad ?? "normal")}.`
    : citations.length > 0
      ? "Bu cevap kaynak işaretleriyle ayrıldı; neye dayandığını sonradan kontrol edebilirsin."
      : metadata?.fallbackReason || warnings.length > 0
        ? "Orka sonucu uydurmak yerine sınırı ve güvenli mod durumunu ayrıca göstermeyi tercih etti."
        : "Bu araç ve bağlam bilgisi, sonraki çalışma adımını daha net seçmene yardım eder.";

  return (
    <div className="mt-2 rounded-2xl border border-[#526d82]/12 bg-[#f7f4ec]/72 px-4 py-3 text-[#344054] shadow-sm">
      <div className="grid gap-2 text-xs leading-5 md:grid-cols-[1fr_1.1fr]">
        <p className="rounded-xl bg-white/48 px-3 py-2">
          <span className="font-black text-[#172033]">Neye dayandı: </span>
          {firstTool ? `${toolLabel(firstTool.toolId, "name" in firstTool ? firstTool.name : firstTool.toolId)} bağlamı` : groundingLabel}.
        </p>
        <p className="rounded-xl bg-white/48 px-3 py-2">
          <span className="font-black text-[#172033]">Öğrenme etkisi: </span>
          {learningHint}
        </p>
        {metadata?.nextCheckPrompt && (
          <p className="rounded-xl bg-white/48 px-3 py-2 md:col-span-2">
            <span className="font-black text-[#172033]">Kendini kontrol et: </span>
            {metadata.nextCheckPrompt}
          </p>
        )}
        {false && metadata?.tutorPedagogyStatus && (
          <p className="rounded-xl bg-white/48 px-3 py-2 md:col-span-2">
            <span className="font-black text-[#172033]">Öğretim kalitesi: </span>
            {metadata.tutorPedagogyStatus === "healthy"
              ? "Orka bu turda planlanan davranışa uydu."
              : "Orka cevabı izlemeye aldı; sonraki turda daha iyi ipucu, kaynak disiplini veya kontrol sorusu kullanacak."}
          </p>
        )}
        {toolStatuses.length > 0 && (
          <p className="rounded-xl bg-white/48 px-3 py-2 md:col-span-2">
            <span className="font-black text-[#172033]">Araç durumu: </span>
            {toolStatuses.slice(0, 5).map((tool) => `${toolLabel(tool.toolId, tool.toolId)} ${formatTechnicalLabel(tool.status)}`).join(" · ")}
          </p>
        )}
      </div>
      <LiveTutorTrace sessionId={sessionId} enabled={Boolean(metadata?.tutorTurnStateId || metadata?.tutorActionTraceId || toolStatuses.length > 0 || tools.length > 0)} />
    </div>
  );
}

function ChatMessageInner({ message, topicId, sessionId, onPlanComplete, userName = "Sen", onOpenWiki, onOpenIDE }: ChatMessageProps) {
  const isUser = message.role === "user";
  const isTopicComplete = message.type === "topic_complete";

  // Hooks must always be called — conditional returns happen after
  const [displayedContent, setDisplayedContent] = useState(isUser ? sanitizeVisibleChatContent(message.content) : "");
  const [audioOpen, setAudioOpen] = useState(false);

  // Resolve quiz data without strict type checking (AI often forgets type indicator but sends json)
  const quizData = message.quiz ?? tryParseQuiz(message.content);

  // Track previous isStreaming to detect the streaming→done transition
  const prevStreamingRef = useRef(message.isStreaming);
  const citationSourceId = message.metadata?.citations?.find((citation) => citation.sourceId)?.sourceId ?? null;
  const classroomSourceId = message.metadata?.sourceId ?? citationSourceId ?? undefined;
  const classroomSurfaceSignal = (message.metadata?.surface ?? message.metadata?.contextType ?? "").toLowerCase();
  const classroomSurface = classroomSourceId || classroomSurfaceSignal.includes("source") || classroomSurfaceSignal.includes("orkalm")
    ? "orkalm"
    : "wiki";
  const classroomWikiPageId = classroomSurface === "wiki" ? message.metadata?.wikiPageId ?? undefined : undefined;
  const classroomAudioMode = message.metadata?.audioMode ?? "brief";

  // Sync displayed content with message content — no typewriter animation for performance
  useEffect(() => {
    prevStreamingRef.current = message.isStreaming;
    setDisplayedContent(sanitizeVisibleChatContent(message.content));
  }, [message.content, message.isStreaming]);

  // ── Konu Tamamlama Kartı ─────────────────────────────���─────────────────
  if (isTopicComplete && message.completedTopicId) {
    return (
      <motion.div
        initial={{ opacity: 0, y: 6 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.3, ease: "easeOut" }}
        className="py-3"
      >
        <div className="flex items-start gap-3 max-w-full">
          <div className="flex-shrink-0 w-7 h-7 rounded-full bg-emerald-900/60 border border-emerald-700/50 flex items-center justify-center mt-1">
            <CheckCircle className="w-3.5 h-3.5 text-emerald-400" />
          </div>
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 mb-2">
              <span className="text-xs font-medium text-emerald-400">Konu Tamamlandı</span>
            </div>
            <div className="bg-emerald-950/30 border border-emerald-800/40 rounded-xl px-5 py-4">
              <p className="text-[13px] text-zinc-300 leading-relaxed mb-4">
                Bu konuyu başarıyla tamamladınız. İsterseniz bu konunun detaylı özeti ve size özel hazırlanmış quizler için kişisel wikinize gidebilirsiniz.
              </p>
              <button
                onClick={() => onOpenWiki?.(message.completedTopicId!)}
                className="flex items-center gap-2 px-4 py-2 bg-emerald-900/50 hover:bg-emerald-800/60 border border-emerald-700/40 text-emerald-300 hover:text-emerald-100 rounded-lg text-sm font-medium transition-colors duration-150"
              >
                <BookOpen className="w-4 h-4" />
                Wikime Git
              </button>
            </div>
          </div>
        </div>
      </motion.div>
    );
  }

  return (
    <div
      className="py-3"
    >
      {isUser ? (
        // ── Kullanıcı mesajı ─────────────────────────────────────────────
        <div className="flex justify-end items-start gap-3">
          <div className="flex flex-col items-end max-w-lg">
            <div className="flex items-center gap-2 mb-1.5 flex-row-reverse">
              <span className="text-xs font-medium text-[#9aa3a0]">{userName}</span>
              <span className="text-[10px] text-[#6f7774]">
                {formatTime(message.timestamp)}
              </span>
            </div>
            <div className="rounded-2xl rounded-tr-sm border border-white/[0.09] bg-white/[0.075] px-4 py-3 shadow-sm">
              <p className="text-[15px] text-[#f4f6f3] leading-relaxed whitespace-pre-wrap">
                {sanitizeVisibleChatContent(message.content)}
              </p>
            </div>
          </div>
          <div className="flex-shrink-0 w-7 h-7 rounded-full bg-white/[0.08] border border-white/[0.09] flex items-center justify-center mt-1 overflow-hidden">
            <img src="https://api.dicebear.com/7.x/notionists/svg?seed=Felix&backgroundColor=transparent" alt="User" className="w-full h-full object-cover" />
          </div>
        </div>
      ) : displayedContent.length === 0 ? null : (
        // ── AI mesajı (Boşken avatar çizilmez çünkü isThinking animasyonu dönüyor) ──
        <div className="flex items-start gap-3 max-w-full">
          {/* Avatar */}
          <div className="flex-shrink-0 w-7 h-7 rounded-full bg-white/[0.08] border border-white/[0.09] shadow-sm flex items-center justify-center mt-1">
            <OrcaLogo className="w-3.5 h-3.5 text-[#f4f6f3]" />
          </div>

          <div className="flex-1 min-w-0">
            {/* Header */}
            <div className="flex items-center gap-2 mb-2">
              <span className="text-xs font-medium text-[#c8cfca]">Orka AI</span>
              <span className="text-[10px] text-[#98a2b3]">
                {formatTime(message.timestamp)}
              </span>
            </div>

            {/* If there's text OTHER than the Quiz JSON, render it here */}
            {(() => {
              // Strip JSON block
              let cleanedText = displayedContent;
              if (quizData) {
                cleanedText = displayedContent.replace(/```(?:json|quiz)?\s*[\s\S]+?\s*```/i, '').trim();
                const firstBrace = cleanedText.indexOf('{');
                const lastBrace = cleanedText.lastIndexOf('}');
                if (firstBrace !== -1 && lastBrace !== -1 && lastBrace > firstBrace && cleanedText.includes('"question"')) {
                   // If fallback JSON without block was used, just clear it roughly
                   cleanedText = cleanedText.substring(0, firstBrace).trim();
                }
              }
              
              if (!cleanedText && quizData) return null; // Only QuizCard

              return (
                <>
                <div className="bg-white/[0.045] border border-white/[0.08] rounded-[1.25rem] px-5 py-4 mb-3 shadow-[0_18px_50px_rgba(0,0,0,0.22)] backdrop-blur-xl">
                  <div
                    className="prose max-w-none
                      prose-headings:text-[#f4f6f3] prose-headings:font-semibold
                      prose-h2:text-[17px] prose-h2:mt-5 prose-h2:mb-2 prose-h2:pb-1.5 prose-h2:border-b prose-h2:border-white/[0.08]
                      prose-h3:text-[15px] prose-h3:mt-4 prose-h3:mb-2
                      prose-p:text-[#d7ddd8] prose-p:leading-relaxed prose-p:my-2.5 prose-p:text-[15px]
                      prose-strong:text-[#f4f6f3]
                      prose-li:text-[#d7ddd8] prose-li:my-1 prose-li:text-[15px]
                      prose-ul:my-2.5 prose-ol:my-2.5
                      prose-a:text-[#8ce6df] prose-a:underline prose-a:underline-offset-2
                      prose-blockquote:border-l-4 prose-blockquote:border-[#6ed7ce]/45 prose-blockquote:bg-white/[0.04] prose-blockquote:rounded-r-xl prose-blockquote:py-3 prose-blockquote:px-5 prose-blockquote:text-[#c8cfca] prose-blockquote:italic prose-blockquote:my-4 prose-blockquote:shadow-sm
                      prose-table:border-collapse prose-table:my-4 prose-table:w-full prose-table:rounded-xl prose-table:overflow-hidden prose-table:shadow-sm prose-table:border prose-table:border-white/[0.08]
                      prose-thead:bg-white/[0.06]
                      prose-th:border-b prose-th:border-white/[0.08] prose-th:px-4 prose-th:py-3 prose-th:text-left prose-th:text-[12px] prose-th:font-bold prose-th:text-[#8ce6df] prose-th:uppercase prose-th:tracking-wider
                      prose-td:border-b prose-td:border-white/[0.06] prose-td:px-4 prose-td:py-3 prose-td:text-[13px] prose-td:text-[#d7ddd8]
                      prose-pre:!bg-transparent prose-pre:!border-0 prose-pre:!p-0 prose-pre:!m-0
                    "
                  >
                    <ReactMarkdown
                      remarkPlugins={[remarkGfm, remarkMath]}
                      rehypePlugins={[rehypeKatex]}
                      urlTransform={safeMarkdownUrlTransform}
                      components={{
                        code({ className, children }) {
                          const langMatch = /language-(\w+)/.exec(className || "");
                          const lang = langMatch?.[1];

                          // V4: mermaid blo��u özel render
                          if (lang === "mermaid") {
                            return <MermaidBlock code={String(children)} />;
                          }

                          const isBlock =
                            className?.startsWith("language-") ||
                            (typeof children === "string" && children.includes("\n"));
                          return isBlock ? (
                            <CodeBlock className={className}>{children}</CodeBlock>
                          ) : (
                            <InlineCode>{children}</InlineCode>
                          );
                        },
                        pre({ children }) {
                          return <>{children}</>;
                        },
                        a({ href, children }) {
                          return <CitationAnchor href={href}>{children}</CitationAnchor>;
                        },
                        img({ src, alt }) {
                          if (!isAllowedRemoteImage(src)) {
                            return <BlockedImagePlaceholder alt={alt} />;
                          }

                          // Pollinations + diğer görselleri sar — yüklenmezse fallback göster
                          return (
                            <img
                              src={src}
                              alt={alt || ""}
                              loading="lazy"
                              className="my-4 rounded-xl border border-[#dcecf3] max-w-full bg-[#f7f9fa]"
                            />
                          );
                        },
                      }}
                    >
                      {withSourceLinks(cleanedText)}
                    </ReactMarkdown>
                  </div>
                </div>
                  <TeachingArtifactRenderer artifacts={message.artifacts} />
                  <ChatMetadataChips metadata={message.metadata} />
                <ChatLearningTrace metadata={message.metadata ?? undefined} sessionId={sessionId} />
                </>
              );
            })()}

            {/* Render the QuizCard if quiz data exists */}
            {quizData && (
              <QuizCard
                quiz={quizData}
                messageId={message.id}
                topicId={topicId}
                sessionId={sessionId}
                planDiagnostic={message.metadata?.planDiagnostic}
                onPlanComplete={onPlanComplete}
                onOpenWiki={onOpenWiki}
                onOpenIDE={onOpenIDE}
                isBaseline={
                    message.content.includes("akademik") ||
                    message.content.includes("Sıfır Noktası") ||
                    message.content.includes("seviyeni ölçmeli") ||
                    message.content.toLowerCase().includes("baseline") ||
                    (!Array.isArray(quizData) && quizData.topic != null && quizData.topic.toLowerCase().includes("planlama"))
                }
              />
            )}

            {/* V4: Sesli Sınıf butonu — sadece quiz olmayan ve içerik dolu AI mesajlarında */}
            {false && !quizData && displayedContent.trim().length > 40 && !message.isStreaming && (
              <div className="mt-2 flex items-center gap-2">
                <button
                  onClick={() => setAudioOpen(true)}
                  className="flex items-center gap-1.5 px-2.5 py-1 rounded-md text-[11px] text-zinc-500 hover:text-emerald-400 hover:bg-emerald-500/10 transition border border-transparent hover:border-emerald-500/20"
                  title="Bu mesajı sesli dinle"
                >
                  <Volume2 className="w-3 h-3" />
                  Sesli dinle
                </button>
              </div>
            )}
          </div>
        </div>
      )}

      {false && audioOpen && (
        <ClassroomAudioPlayer
          text={displayedContent}
          topicId={topicId}
          sessionId={sessionId}
          surface={classroomSurface}
          wikiPageId={classroomWikiPageId}
          sourceId={classroomSurface === "orkalm" ? classroomSourceId : undefined}
          audioMode={classroomAudioMode}
          onClose={() => setAudioOpen(false)}
        />
      )}
    </div>
  );
}

export default memo(ChatMessageInner, (prev, next) => {
  return (
    prev.message.id === next.message.id &&
    prev.message.content === next.message.content &&
    prev.message.metadata === next.message.metadata &&
    prev.message.artifacts === next.message.artifacts &&
    prev.message.isStreaming === next.message.isStreaming &&
    prev.message.type === next.message.type &&
    prev.topicId === next.topicId &&
    prev.sessionId === next.sessionId &&
    prev.userName === next.userName
  );
});
