/*
 * WikiDrawer — Premium Wiki Copilot Panel
 * Sağdan kayan panel. İçerisinde:
 *   1. Wiki doküman görüntüleme (Mevcut)
 *   2. Wiki Soru-Cevap Ajanı (Mevcut, iyileştirilmiş)
 *   3. Korteks Derin Araştırma (YENİ — internetten araştırma)
 */

import { useState, useEffect, useMemo, useRef } from "react";
import { useAudioOverviewPolling } from "@/hooks/useAudioOverviewPolling";
import { AnimatePresence, motion } from "framer-motion";
import {
  X,
  BookOpen,
  Loader2,
  ChevronDown,
  Sparkles,
  MessageCircle,
  Send,
  Clock,
  Lightbulb,
  ListChecks,
  Search,
  Upload,
  FileText,
  Headphones,
  CalendarDays,
  Tags,
  Network,
  HelpCircle,
  CheckCircle2,
  Zap,
  Trash2,
} from "lucide-react";
import toast from "react-hot-toast";
import { AudioOverviewAPI, LearningAPI, QuestionPracticeAPI, SourcesAPI, TutorAPI, WikiAPI, storage } from "@/services/api";
import { useLearningWorkspaceState } from "@/hooks/useLearningWorkspaceState";
import { tryParseQuiz } from "@/lib/quizParser";
import type { ChatResponseMetadata, CitationDto, CitationReviewResultDto, MultiSourceCompareResultDto, QuestionPracticeSessionDto, QuestionPracticeSubmitResponseDto, SourceConceptGraphDto, SourceConceptLinkSummaryDto, SourceNotebookDto, SourceQualityReportDto, SourceQuestionResponseDto, SourceQuestionThreadDto, SourceStudySummaryDto, TeachingArtifact, WikiCopilotContextDto, WikiGraphDto, WikiGraphPageDto, WikiPageQuestionSetDto } from "@/lib/types";
import { citationDisplayTitle, citationPrimaryLabel, citationScopeSummary, evidenceQualityDetail, evidenceQualityLabel, evidenceQualityTone } from "@/lib/citationDisplay";
import { userSafeStatus } from "@/lib/userSafeStatus";
import QuizCard from "./QuizCard";
import RichMarkdown from "./RichMarkdown";
import NotebookStudioPanel from "./NotebookStudioPanel";
import ClassroomAudioPlayer from "./ClassroomAudioPlayer";

interface WikiPage {
  id: string;
  topicId?: string;
  title: string;
  pageKey?: string;
  pageType?: string;
  conceptKey?: string | null;
  parentConceptKey?: string | null;
  parentWikiPageId?: string | null;
  sourceReadiness?: string;
  evidenceStatus?: string;
  safeSummary?: string | null;
  contentReadiness?: string;
  hasLearningContent?: boolean;
  visibleBlockCount?: number;
  requiredBlockTypesPresent?: boolean;
  curation?: import("@/lib/types").WikiCurationSummaryDto | null;
  learningSystemBinding?: import("@/lib/types").WikiLearningSystemBindingDto | null;
  orderIndex?: number;
  blockCount?: number;
  blocks?: Array<{
    id: string;
    type: string;
    content: string;
    title?: string;
    sourceBasis?: string;
    conceptKey?: string | null;
    misconceptionKey?: string | null;
    visibility?: string;
  }>;
}

interface WikiMainPanelProps {
  topicId: string;
  onClose: () => void;
  mode?: "wiki" | "orkalm";
}

const buildOrkaLmFallbackPage = (topicId: string): WikiPage => ({
  id: `orkalm-source-${topicId}`,
  title: "OrkaLM Source Notebook",
  pageKey: `orkalm-source-${topicId}`,
  pageType: "orkalm_source",
  sourceReadiness: "source_notebook",
  evidenceStatus: "evidence_insufficient",
  safeSummary: "Source notebook view; Wiki pages appear when learning traces exist.",
  orderIndex: 0,
  blockCount: 0,
  blocks: [],
});

interface CopilotMessage {
  role: "user" | "assistant" | "system";
  content: string;
  citations?: CitationDto[];
  artifacts?: TeachingArtifact[];
  metadata?: ChatResponseMetadata | null;
  groundingStatus?: string | null;
}

interface LearningSource {
  id: string;
  title: string;
  fileName: string;
  pageCount: number;
  chunkCount: number;
  status: string;
  createdAt: string;
}

interface SourcePage {
  sourceId: string;
  pageNumber: number;
  title: string;
  chunks: Array<{
    id: string;
    pageNumber: number;
    chunkIndex: number;
    text: string;
    highlightHint?: string;
  }>;
}

interface SourceCitation {
  id: string;
  pageNumber: number;
  chunkIndex: number;
  label?: string;
  supportStatus?: string;
}

interface MindMapNode {
  id: string;
  label: string;
  parentId?: string | null;
  depth: number;
}

const MAX_POLL_ATTEMPTS = 20; // 20 × 3s = 60 saniye maksimum bekleme

const sourceQualityLabel = (status?: string | null) => {
  switch ((status ?? "").toLowerCase()) {
    case "healthy":
    case "grounded":
      return "güçlü";
    case "source_retrieval_empty":
    case "empty":
      return "kaynakta cevap yok";
    case "citation_missing":
      return "citation eksik";
    case "citation_unsupported":
      return "citation desteklenmiyor";
    case "low_confidence":
    case "degraded":
      return "zayıf";
    default:
      return "ölçülmedi";
  }
};

type SourceCoverageCoachTone = "ready" | "watch" | "empty";
type WikiStudySection = "briefing" | "practice" | "reinforcement" | "sources" | "glossary" | "cards";
type WikiVaultFilter = "all" | "weak" | "source_ready" | "evidence_insufficient" | "stale" | "misconception" | "source_pages";

interface WikiStudyPackItem {
  id: string;
  label: string;
  detail: string;
  section: WikiStudySection;
  tone: "ready" | "watch" | "empty";
}
const wikiVaultFilters: Array<{ id: WikiVaultFilter; label: string }> = [
  { id: "all", label: "Tum sayfalar" },
  { id: "weak", label: "Zayif / review" },
  { id: "source_ready", label: "Kaynakli" },
  { id: "evidence_insufficient", label: "Kaniti zayif" },
  { id: "stale", label: "Stale / degraded" },
  { id: "misconception", label: "Takilma / repair" },
  { id: "source_pages", label: "Source pages" },
];

const normalizeVaultText = (value?: string | null) =>
  (value ?? "").toLocaleLowerCase("tr-TR");

const pageMatchesVaultFilter = (page: WikiGraphPageDto, filter: WikiVaultFilter) => {
  const haystack = normalizeVaultText([
    page.title,
    page.pageType,
    page.status,
    page.sourceReadiness,
    page.evidenceStatus,
    page.safeSummary,
    page.conceptKey,
    page.parentConceptKey,
  ].filter(Boolean).join(" "));

  if (filter === "all") return true;
  if (filter === "weak") return /weak|zayif|needs_review|review|developing/.test(haystack);
  if (filter === "source_ready") return /source_grounded|source_ready|wiki_backed|mixed|ready/.test(haystack);
  if (filter === "evidence_insufficient") return /evidence_insufficient|insufficient|unsupported|missing/.test(haystack);
  if (filter === "stale") return /stale|degraded|deleted|outdated/.test(haystack);
  if (filter === "misconception") return /misconception|repair|takil|yanlis|remediation/.test(haystack);
  if (filter === "source_pages") return /source|orkalm/.test(normalizeVaultText(page.pageType));
  return true;
};

const pageBadgeTone = (value?: string | null) => {
  const normalized = normalizeVaultText(value);
  if (/source_grounded|ready|strong|wiki_backed|mixed/.test(normalized)) {
    return "border-[#8fb7a2]/24 bg-[#f2faf5]/85 text-[#47725d]";
  }
  if (/stale|degraded|insufficient|weak|misconception|repair|review/.test(normalized)) {
    return "border-[#e8c46f]/28 bg-[#fff8ee]/85 text-[#8a6a33]";
  }
  return "border-[#526d82]/14 bg-[#f7f9fa]/75 text-[#667085]";
};

const blockGroupFor = (blockType?: string | null) => {
  const type = normalizeVaultText(blockType);
  if (/student_question|question|soru/.test(type)) return { id: "questions", label: "Ogrenci sorulari" };
  if (/source/.test(type)) return { id: "sources", label: "Kaynak notlari" };
  if (/misconception|repair|remediation/.test(type)) return { id: "repair", label: "Takilma ve onarim" };
  if (/quiz|review|checkpoint/.test(type)) return { id: "quiz", label: "Quiz ve tekrar" };
  if (/artifact|flashcard|retrieval/.test(type)) return { id: "artifacts", label: "Artifact ve pratik" };
  if (/example|worked/.test(type)) return { id: "examples", label: "Ornekler" };
  if (/manual|note|summary/.test(type)) return { id: "notes", label: "Notlar ve ozet" };
  return { id: "explanations", label: "Tutor anlatimi" };
};

function buildWikiSourceCoverageCoach(input: {
  sourceCount: number;
  readySources: number;
  totalChunks: number;
  quality: SourceQualityReportDto | null;
}): { title: string; detail: string; tone: SourceCoverageCoachTone; actionLabel: string } {
  if (input.quality?.evidenceQuality) {
    const tone = evidenceQualityTone(input.quality.evidenceQuality);
    return {
      title: evidenceQualityLabel(input.quality.evidenceQuality),
      detail: evidenceQualityDetail(input.quality.evidenceQuality),
      tone,
      actionLabel: tone === "ready" ? "Kanıtları incele" : "Kaynak ekle",
    };
  }

  if (input.sourceCount === 0) {
    return {
      title: "Kaynak durumu için henüz yeterli veri yok.",
      detail: "Bu konuya kaynak eklediğinde Wiki ve RAG yanıtları daha güvenilir hale gelir.",
      tone: "empty",
      actionLabel: "Kaynak ekle",
    };
  }

  const retrievalStatus = (input.quality?.retrievalHealthStatus ?? "").toLowerCase();
  const citationStatus = (input.quality?.citationCoverageStatus ?? "").toLowerCase();
  const hasWeakQuality =
    retrievalStatus === "source_retrieval_empty" ||
    retrievalStatus === "empty" ||
    retrievalStatus === "degraded" ||
    citationStatus === "citation_missing" ||
    citationStatus === "citation_unsupported" ||
    (input.quality?.emptyRunCount ?? 0) > 0 ||
    (input.quality?.unsupportedCitationCount ?? 0) > 0 ||
    (input.quality?.citationMissingCount ?? 0) > 0;

  if (input.readySources === 0 || input.totalChunks === 0) {
    return {
      title: "Bu konuda kaynak eksik olabilir.",
      detail: "RAG yanıtları için yeterli kaynak bulunamayabilir; hazır veya parçalanmış kaynak görünmüyor.",
      tone: "watch",
      actionLabel: "Kaynak ekle",
    };
  }

  if (hasWeakQuality) {
    return {
      title: "Kaynak kalitesi zayıf; yeni kaynak eklemek faydalı olabilir.",
      detail: `${input.quality?.emptyRunCount ?? 0} boş arama izi ve ${(input.quality?.unsupportedCitationCount ?? 0) + (input.quality?.citationMissingCount ?? 0)} citation sorunu görünüyor.`,
      tone: "watch",
      actionLabel: "Kaynakları yenile",
    };
  }

  return {
    title: "Bu konu için kaynaklar hazır.",
    detail: `${input.readySources}/${input.sourceCount} kaynak hazır; Wiki ve RAG yanıtları için yeterli kaynak kapsaması görünüyor.`,
    tone: "ready",
    actionLabel: "Wiki’yi kontrol et",
  };
}

function SourceCoverageCoach({
  title,
  detail,
  tone,
  actionLabel,
  onAction,
}: {
  title: string;
  detail: string;
  tone: SourceCoverageCoachTone;
  actionLabel: string;
  onAction: () => void;
}) {
  const toneClass = {
    ready: "border-[#8fb7a2]/28 bg-[#f2faf5]/80 text-[#47725d]",
    watch: "border-[#e8c46f]/32 bg-[#fff8ee]/82 text-[#8a641f]",
    empty: "border-[#526d82]/14 bg-[#f7f9fa]/70 text-[#667085]",
  } satisfies Record<SourceCoverageCoachTone, string>;

  return (
    <div className={`rounded-2xl border px-3 py-3 ${toneClass[tone]}`}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <div className="text-[10px] font-black uppercase tracking-[0.18em] opacity-80">
            Kaynak kapsaması
          </div>
          <p className="mt-1 text-sm font-black text-[#172033]">{title}</p>
          <p className="mt-1 text-[11px] leading-5 opacity-85">{detail}</p>
        </div>
        <button
          type="button"
          onClick={onAction}
          className="inline-flex shrink-0 items-center gap-1.5 rounded-lg bg-white/75 px-3 py-2 text-[11px] font-black text-[#172033] shadow-sm transition hover:bg-white focus:outline-none focus:ring-2 focus:ring-[#9ec7d9]"
        >
          {actionLabel}
        </button>
      </div>
    </div>
  );
}
function buildWikiStudyPackItems(input: {
  hasBriefing: boolean;
  isBriefingLoading: boolean;
  pagePracticeReadyCount: number;
  glossaryCount: number;
  studyCardCount: number;
  recommendationCount: number;
  weakSkillCount: number;
  sourceCount: number;
  readySources: number;
  totalChunks: number;
  hasSourceQualityConcern: boolean;
}): WikiStudyPackItem[] {
  const items: WikiStudyPackItem[] = [];

  if (input.hasBriefing || input.isBriefingLoading) {
    items.push({
      id: "briefing",
      label: "Özeti oku",
      detail: input.isBriefingLoading ? "Hızlı Bakış hazırlanıyor." : "Konuya başlamadan önce kısa özeti ve ana çıkarımları gözden geçir.",
      section: "briefing",
      tone: input.hasBriefing ? "ready" : "empty",
    });
  }

  if (input.pagePracticeReadyCount > 0) {
    items.push({
      id: "wiki-page-practice",
      label: "Sayfa pratiğini çöz",
      detail: `${input.pagePracticeReadyCount} concept-bound soru bu wiki sayfasına bağlı.`,
      section: "practice",
      tone: "ready",
    });
  }

  if (input.weakSkillCount > 0) {
    items.push({
      id: "weak-skills",
      label: "Zayıf kavramı Wiki’den tekrar et",
      detail: "Çalışma kuyruğundaki zorlanma sinyalini Wiki üzerinden toparla.",
      section: "reinforcement",
      tone: "watch",
    });
  }

  if (input.recommendationCount > 0) {
    items.push({
      id: "recommendations",
      label: "Önerilen adımları izle",
      detail: "Quiz ve öğrenme sinyallerinden gelen kişisel pekiştirme önerilerini aç.",
      section: "reinforcement",
      tone: "ready",
    });
  }

  if (input.glossaryCount > 0) {
    items.push({
      id: "glossary",
      label: "Kavramları gözden geçir",
      detail: `${input.glossaryCount} terimi kısa açıklamalarıyla tekrar et.`,
      section: "glossary",
      tone: "ready",
    });
  }

  if (input.studyCardCount > 0) {
    items.push({
      id: "study-cards",
      label: "Kartlarla çalış",
      detail: `${input.studyCardCount} pekiştirme kartıyla kendini yokla.`,
      section: "cards",
      tone: "ready",
    });
  }

  if (input.sourceCount > 0) {
    items.push({
      id: "sources",
      label: input.hasSourceQualityConcern || input.readySources === 0 || input.totalChunks === 0 ? "Kaynakları kontrol et" : "Kanıtları incele",
      detail: input.hasSourceQualityConcern || input.readySources === 0 || input.totalChunks === 0
        ? "Kaynak kapsaması zayıf olabilir; kaynak panelini ve kanıt durumunu kontrol et."
        : `${input.readySources}/${input.sourceCount} kaynak hazır; kanıt panelinden dayanakları incele.`,
      section: "sources",
      tone: input.hasSourceQualityConcern || input.readySources === 0 || input.totalChunks === 0 ? "watch" : "ready",
    });
  }

  return items;
}

function buildWikiLearningTraceSummary(metadata?: ChatResponseMetadata | null): Array<{ label: string; detail: string; tone: "grounded" | "learning" | "watch" }> {
  if (!metadata) return [];
  const citations = metadata.citations ?? [];
  const warnings = metadata.providerWarnings ?? [];
  const tools = [...(metadata.usedTools ?? []), ...(metadata.toolStatuses ?? [])];
  const evidenceSummary = metadata.evidenceSummary;
  const mode = metadata.groundingMode?.toLowerCase() ?? "";
  const items: Array<{ label: string; detail: string; tone: "grounded" | "learning" | "watch" }> = [];

  if (citations.length > 0 || (evidenceSummary?.sourceCount ?? 0) > 0 || mode.includes("source") || mode.includes("wiki")) {
    items.push({
      label: "Bu cevap kaynaklarla desteklendi.",
      detail: citations.length > 0
        ? `Orka bu cevabı ${citations.length} kaynak işaretiyle mevcut ders kaynaklarıyla ilişkilendirdi.`
        : "Orka bu cevabı mevcut ders kaynaklarıyla ilişkilendirdi.",
      tone: "grounded",
    });
  } else if (metadata.fallbackReason || warnings.length > 0 || metadata.ragQualityStatus === "degraded") {
    items.push({
      label: "Orka sınırı gösterdi; kaynak veya sağlayıcı güveni düşük olabilir.",
      detail: "Cevap güvenli modda tutuldu; kaynak zayıfsa kesinlik iddiası kurulmadı.",
      tone: "watch",
    });
  }

  const hasPracticeEvidence = tools.some((tool) => {
    const id = (tool.toolId ?? ("name" in tool ? tool.name : "") ?? "").toLowerCase();
    return id.includes("quiz") || id.includes("assessment") || id.includes("ide") || id.includes("code");
  });

  if (hasPracticeEvidence || evidenceSummary?.learnerEvidenceStatus) {
    items.push({
      label: "Quiz/pratik kanıtı güncellendi.",
      detail: "Bu turdaki öğrenen kanıtı sonraki çalışma kararlarına bağlanabilir.",
      tone: "learning",
    });
  } else if (metadata.teachingMode || metadata.nextCheckPrompt || metadata.activeConceptKey) {
    items.push({
      label: "Bu turda zayıf kavram / öğrenme sinyali oluşmuş olabilir.",
      detail: metadata.nextCheckPrompt
        ? `Sonraki kontrol: ${metadata.nextCheckPrompt}`
        : "Bu cevap sonraki kısa kontrol sorusuna bağlanabilir.",
      tone: "learning",
    });
  }

  if (items.length === 0) {
    items.push({
      label: "Henüz öğrenme izi oluşmadı.",
      detail: "Bu yanıtta kaynak, quiz veya pratik sinyali görünmüyor.",
      tone: "watch",
    });
  }

  return items.slice(0, 2);
}

function WikiLearningTraceSummary({ metadata }: { metadata?: ChatResponseMetadata | null }) {
  const items = buildWikiLearningTraceSummary(metadata);
  if (!metadata || items.length === 0) return null;

  const toneClass = {
    grounded: "border-[#9ec7d9]/32 bg-[#dcecf3]/48",
    learning: "border-[#8fb7a2]/28 bg-[#f2faf5]/70",
    watch: "border-[#e8c46f]/32 bg-[#fff8ee]/75",
  } satisfies Record<"grounded" | "learning" | "watch", string>;

  return (
    <div className="mt-3 rounded-2xl border border-[#526d82]/12 bg-[#f7f4ec]/70 px-3 py-3 text-[#344054]">
      <p className="mb-2 text-[10px] font-black uppercase tracking-[0.16em] text-[#52768a]">
        Orka bu turda
      </p>
      <div className="space-y-2 text-[11px] leading-5">
        {items.map((item) => (
          <p key={item.label} className={`rounded-xl border px-3 py-2 ${toneClass[item.tone]}`}>
            <span className="block font-black text-[#172033]">{item.label}</span>
            <span className="mt-1 block text-[#667085]">{item.detail}</span>
          </p>
        ))}
      </div>
    </div>
  );
}

export default function WikiMainPanel({ topicId, onClose, mode = "wiki" }: WikiMainPanelProps) {
  const [pages, setPages] = useState<WikiPage[]>([]);
  const [activePage, setActivePage] = useState<WikiPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);
  const [isPolling, setIsPolling] = useState(false);
  const [wikiGraph, setWikiGraph] = useState<WikiGraphDto | null>(null);
  const [wikiGraphLoading, setWikiGraphLoading] = useState(false);
  const [wikiGraphSyncing, setWikiGraphSyncing] = useState(false);
  const [wikiVaultQuery, setWikiVaultQuery] = useState("");
  const [wikiVaultFilter, setWikiVaultFilter] = useState<WikiVaultFilter>("all");
  const pollAttemptsRef = useRef(0);
  const [expandedBlocks, setExpandedBlocks] = useState<
    Record<string, boolean>
  >({});

  // Copilot State
  const [showCopilot, setShowCopilot] = useState(false);
  const [messages, setMessages] = useState<CopilotMessage[]>([]);
  const [input, setInput] = useState("");
  const [isStreaming, setIsStreaming] = useState(false);
  const messagesEndRef = useRef<HTMLDivElement>(null);

  // NotebookLM-tarzı Briefing State
  const [briefing, setBriefing] = useState<{
    tldr: string;
    keyTakeaways: string[];
    suggestedQuestions: string[];
  } | null>(null);
  const [briefingLoading, setBriefingLoading] = useState(false);
  const [sources, setSources] = useState<LearningSource[]>([]);
  const [sourcesLoading, setSourcesLoading] = useState(false);
  const [uploadingSource, setUploadingSource] = useState(false);
  const [activeSource, setActiveSource] = useState<LearningSource | null>(null);
  const [sourceQuestion, setSourceQuestion] = useState("");
  const [sourceAnswer, setSourceAnswer] = useState("");
  const [sourceQuestionResponse, setSourceQuestionResponse] = useState<SourceQuestionResponseDto | null>(null);
  const [sourceQuality, setSourceQuality] = useState<SourceQualityReportDto | null>(null);
  const [sourceNotebook, setSourceNotebook] = useState<SourceNotebookDto | null>(null);
  const [activeSourceNotebook, setActiveSourceNotebook] = useState<SourceNotebookDto | null>(null);
  const [sourceConceptLinks, setSourceConceptLinks] = useState<SourceConceptLinkSummaryDto | null>(null);
  const [sourceConceptGraph, setSourceConceptGraph] = useState<SourceConceptGraphDto | null>(null);
  const [activePageSourceLinks, setActivePageSourceLinks] = useState<SourceConceptLinkSummaryDto | null>(null);
  const [wikiCopilot, setWikiCopilot] = useState<WikiCopilotContextDto | null>(null);
  const [wikiCopilotLoading, setWikiCopilotLoading] = useState(false);
  const [sourceConceptSyncing, setSourceConceptSyncing] = useState(false);
  const [sourceAsking, setSourceAsking] = useState(false);
  const [selectedCompareSourceIds, setSelectedCompareSourceIds] = useState<string[]>([]);
  const [sourceCompare, setSourceCompare] = useState<MultiSourceCompareResultDto | null>(null);
  const [citationReview, setCitationReview] = useState<CitationReviewResultDto | null>(null);
  const [sourceComparing, setSourceComparing] = useState(false);
  const [sourceQuestionThreads, setSourceQuestionThreads] = useState<SourceQuestionThreadDto[]>([]);
  const [activeQuestionThread, setActiveQuestionThread] = useState<SourceQuestionThreadDto | null>(null);
  const [sourceStudySummary, setSourceStudySummary] = useState<SourceStudySummaryDto | null>(null);
  const [threadFollowUp, setThreadFollowUp] = useState("");
  const [threadLoading, setThreadLoading] = useState(false);
  const [sourceCitations, setSourceCitations] = useState<SourceCitation[]>([]);
  const [sourcePage, setSourcePage] = useState<SourcePage | null>(null);
  const [sourcePageLoading, setSourcePageLoading] = useState(false);
  const [focusedChunkId, setFocusedChunkId] = useState<string | null>(null);
  const sourceViewerRef = useRef<HTMLDivElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [glossary, setGlossary] = useState<Array<{ term: string; simpleExplanation: string }>>([]);
  const [timeline, setTimeline] = useState<Array<{ year: string; event: string }>>([]);
  const [mindMap, setMindMap] = useState<{ mermaid: string; nodes: MindMapNode[] } | null>(null);
  const [studyCards, setStudyCards] = useState<Array<{ front: string; back: string; sourceHint?: string }>>([]);
  const [recommendations, setRecommendations] = useState<Array<{
    id: string;
    title: string;
    reason: string;
    skillTag?: string;
    actionPrompt?: string;
  }>>([]);
  const [wikiPageQuestionSet, setWikiPageQuestionSet] = useState<WikiPageQuestionSetDto | null>(null);
  const [wikiPagePractice, setWikiPagePractice] = useState<QuestionPracticeSessionDto | null>(null);
  const [wikiPagePracticeResult, setWikiPagePracticeResult] = useState<QuestionPracticeSubmitResponseDto | null>(null);
  const [wikiPagePracticeLoading, setWikiPagePracticeLoading] = useState(false);
  const [wikiPracticeAnswers, setWikiPracticeAnswers] = useState<Record<string, string>>({});
  const [weakSkills, setWeakSkills] = useState<Array<{
    skillTag: string;
    topicPath: string;
    wrongCount: number;
    totalCount: number;
    accuracy: number;
  }>>([]);
  const [learningCache, setLearningCache] = useState<{
    hit: boolean;
    source: string;
    generatedAt: string;
    cachedAt?: string | null;
  } | null>(null);
  const [flippedCards, setFlippedCards] = useState<Record<number, boolean>>({});
  const [notebookToolsLoading, setNotebookToolsLoading] = useState(false);
  const [notebookRefreshTick, setNotebookRefreshTick] = useState(0);
  const { job: audioJob, loading: audioPolling, error: audioError, startPolling: startAudioPolling, setJob: setAudioJob } = useAudioOverviewPolling();

  const [audioBlobUrl, setAudioBlobUrl] = useState<string | null>(null);
  const [audioClassroomOpen, setAudioClassroomOpen] = useState(false);

  useEffect(() => {
    let currentBlobUrl: string | null = null;
    if (audioJob && (audioJob.status === "ready" || audioJob.status === "completed") && !audioJob.errorMessage) {
      AudioOverviewAPI.fetchBlob(audioJob.id)
        .then(url => {
          currentBlobUrl = url;
          setAudioBlobUrl(url);
        })
        .catch(console.error);
    } else {
      setAudioBlobUrl(null);
    }
    return () => {
      if (currentBlobUrl) URL.revokeObjectURL(currentBlobUrl);
    };
  }, [audioJob]);
  const [audioLoading, setAudioLoading] = useState(false);
  const [audioMode, setAudioMode] = useState<"brief" | "deep_dive" | "critique" | "debate">("brief");
  const [ttsQuality, setTtsQuality] = useState<"draft" | "standard" | "studio">("standard");

  const patchLastAssistantMessage = (patch: Partial<CopilotMessage>) => {
    setMessages((prev) => {
      const updated = [...prev];
      for (let i = updated.length - 1; i >= 0; i -= 1) {
        if (updated[i].role === "assistant") {
          updated[i] = { ...updated[i], ...patch };
          break;
        }
      }
      return updated;
    });
  };

  const addArtifactToLastAssistant = (artifact: TeachingArtifact) => {
    setMessages((prev) => {
      const updated = [...prev];
      for (let i = updated.length - 1; i >= 0; i -= 1) {
        if (updated[i].role === "assistant") {
          const existing = updated[i].artifacts ?? [];
          if (!existing.some((item) => item.id === artifact.id)) {
            updated[i] = { ...updated[i], artifacts: [...existing, artifact] };
          }
          break;
        }
      }
      return updated;
    });
  };

  const toggleBlock = (blockId: string | number) => {
    setExpandedBlocks((prev) => ({ ...prev, [blockId]: !prev[blockId] }));
  };

  // Auto-scroll messages
  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  const applyGraphPages = (graph: WikiGraphDto) => {
    const graphPages = graph.pages.map((page) => ({
      id: page.id,
      topicId: page.topicId,
      title: page.title,
      pageKey: page.pageKey,
      pageType: page.pageType,
      conceptKey: page.conceptKey,
      parentConceptKey: page.parentConceptKey,
      parentWikiPageId: page.parentWikiPageId,
      sourceReadiness: page.sourceReadiness,
      evidenceStatus: page.evidenceStatus,
      safeSummary: page.safeSummary,
      curation: page.curation,
      orderIndex: page.orderIndex,
      blockCount: page.blockCount,
      contentReadiness: page.contentReadiness,
      hasLearningContent: page.hasLearningContent,
      visibleBlockCount: page.visibleBlockCount,
      requiredBlockTypesPresent: page.requiredBlockTypesPresent,
      learningSystemBinding: page.learningSystemBinding,
    }));

    if (graphPages.length > 0) {
      setPages((prev) => {
        const byId = new Map(prev.map((page) => [page.id, page]));
        return graphPages.map((page) => ({ ...page, blocks: byId.get(page.id)?.blocks }));
      });
      setActivePage((current) => {
        const currentGraphPage = current ? graphPages.find((page) => page.id === current.id) : null;
        return currentGraphPage ? { ...currentGraphPage, blocks: current?.blocks } : graphPages[0];
      });
      setIsPolling(false);
    }
  };

  // Fetch wiki pages (initial)
  useEffect(() => {
    setLoading(true);
    setError(false);
    setPages([]);
    setActivePage(null);
    setWikiGraph(null);
    setWikiVaultQuery("");
    setWikiVaultFilter("all");
    setIsPolling(false);
    pollAttemptsRef.current = 0;

    WikiAPI.getTopicPages(topicId)
      .then((r) => {
        const data = (r.data as WikiPage[]) ?? [];
        setPages(data);
        if (data.length > 0) {
          setActivePage(data[0]);
        } else {
          // Wiki henüz oluşmadı — arka plan görevi tamamlanana kadar poll et
          if (mode === "orkalm") {
            const fallbackPage = buildOrkaLmFallbackPage(topicId);
            setPages([fallbackPage]);
            setActivePage(fallbackPage);
            setIsPolling(false);
          } else {
            setIsPolling(true);
          }
        }
      })
      .catch(() => setError(true))
      .finally(() => setLoading(false));
  }, [mode, topicId]);

  useEffect(() => {
    setWikiGraphLoading(true);
    WikiAPI.getGraph(topicId)
      .then((graph) => {
        setWikiGraph(graph);
        if (pages.length === 0 && graph.pages.length > 0) {
          applyGraphPages(graph);
        }
      })
      .catch(() => setWikiGraph(null))
      .finally(() => setWikiGraphLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [topicId]);

  // Polling: wiki hazır olana kadar 3 saniyede bir kontrol et (max 60s)
  useEffect(() => {
    if (!isPolling) return;

    const interval = setInterval(async () => {
      pollAttemptsRef.current += 1;
      if (pollAttemptsRef.current >= MAX_POLL_ATTEMPTS) {
        setIsPolling(false);
        toast.error("Wiki oluşturma zaman aşımına uğradı veya sunucu hatası. Lütfen daha sonra tekrar deneyin.", {
          duration: 8000,
        });
        return;
      }
      try {
        const r = await WikiAPI.getTopicPages(topicId);
        const data = (r.data as WikiPage[]) ?? [];
        if (data.length > 0) {
          setPages(data);
          setActivePage(data[0]);
          setIsPolling(false);
        }
      } catch {
        setIsPolling(false);
        toast.error("Wiki sayfaları yüklenirken sunucu hatası oluştu.", { duration: 6000 });
      }
    }, 3000);

    return () => clearInterval(interval);
  }, [isPolling, topicId]);

  // Fetch page content
  useEffect(() => {
    if (!activePage || activePage.blocks) return;
    WikiAPI.getPage(activePage.id).then((r) => {
      const full = r.data as { page: WikiPage; blocks: WikiPage["blocks"] };
      const updated = { ...full.page, blocks: full.blocks };
      setPages((prev) =>
        prev.map((p) => (p.id === updated.id ? updated : p))
      );
      setActivePage(updated);
    });
  }, [activePage]);

  const pageContent =
    activePage?.blocks?.map((b) => b.content).join("\n\n") ?? "";

  // Wiki blok'ları yüklendiğinde Briefing çek (1 saatlik backend cache var)
  useEffect(() => {
    if (!activePage?.blocks || activePage.blocks.length === 0) {
      setBriefing(null);
      return;
    }
    setBriefingLoading(true);
    WikiAPI.getBriefing(topicId)
      .then((data) => {
        if (data.tldr || data.keyTakeaways.length > 0) {
          setBriefing({
            tldr: data.tldr,
            keyTakeaways: data.keyTakeaways,
            suggestedQuestions: data.suggestedQuestions,
          });
        }
      })
      .catch(() => {
        // Sessiz başarısızlık — briefing kart gösterilmez
        setBriefing(null);
      })
      .finally(() => setBriefingLoading(false));
  }, [topicId, activePage?.blocks?.length, notebookRefreshTick]);

  useEffect(() => {
    const pageId = activePage?.id;
    if (!pageId || pageId.startsWith("orkalm-source-")) {
      setWikiPageQuestionSet(null);
      setWikiPagePractice(null);
      setWikiPagePracticeResult(null);
      setWikiPracticeAnswers({});
      return;
    }

    let cancelled = false;
    setWikiPageQuestionSet(null);
    setWikiPagePractice(null);
    setWikiPagePracticeResult(null);
    setWikiPracticeAnswers({});
    WikiAPI.getPageQuestions(pageId, 5)
      .then((questionSet) => {
        if (!cancelled) setWikiPageQuestionSet(questionSet);
      })
      .catch(() => {
        if (!cancelled) setWikiPageQuestionSet(null);
      });
    return () => {
      cancelled = true;
    };
  }, [activePage?.id]);

  const refreshSources = async () => {
    setSourcesLoading(true);
    try {
      const [data, quality, notebook] = await Promise.all([
        SourcesAPI.getTopicSources(topicId),
        SourcesAPI.getTopicQuality(topicId).catch(() => null),
        SourcesAPI.getTopicNotebook(topicId).catch(() => null),
      ]);
      setSources(data);
      setSourceQuality(quality);
      setSourceNotebook(notebook);
      if (!activeSource && data.length > 0) setActiveSource(data[0]);
    } catch {
      setSources([]);
      setSourceQuality(null);
      setSourceNotebook(null);
    } finally {
      setSourcesLoading(false);
    }
  };

  useEffect(() => {
    refreshSources();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [topicId]);

  useEffect(() => {
    const sourceIds = new Set(sources.map((source) => source.id));
    setSelectedCompareSourceIds((current) => {
      const kept = current.filter((id) => sourceIds.has(id));
      if (kept.length > 0) return kept;
      return sources.slice(0, 2).map((source) => source.id);
    });
  }, [sources]);

  useEffect(() => {
    if (mode !== "orkalm" || sources.length === 0) {
      setCitationReview(null);
      return;
    }
    let cancelled = false;
    SourcesAPI.getTopicCitationReview(topicId)
      .then((review) => {
        if (!cancelled) setCitationReview(review);
      })
      .catch(() => {
        if (!cancelled) setCitationReview(null);
      });
    return () => {
      cancelled = true;
    };
  }, [mode, sources.length, topicId]);

  const refreshQuestionThreads = async () => {
    if (mode !== "orkalm") {
      setSourceQuestionThreads([]);
      setActiveQuestionThread(null);
      setSourceStudySummary(null);
      return;
    }
    setThreadLoading(true);
    try {
      const params = {
        topicId,
        sourceId: activeSource?.id,
        wikiPageId: undefined,
      };
      const [result, summary] = await Promise.all([
        SourcesAPI.listQuestionThreads(params),
        SourcesAPI.getSourceStudySummary(params).catch(() => null),
      ]);
      setSourceQuestionThreads(result.items ?? []);
      setSourceStudySummary(summary);
      setActiveQuestionThread((current) => {
        if (current && result.items.some((item) => item.threadId === current.threadId)) {
          return result.items.find((item) => item.threadId === current.threadId) ?? current;
        }
        return result.items[0] ?? null;
      });
    } catch {
      setSourceQuestionThreads([]);
      setActiveQuestionThread(null);
      setSourceStudySummary(null);
    } finally {
      setThreadLoading(false);
    }
  };

  useEffect(() => {
    refreshQuestionThreads();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mode, topicId, activeSource?.id, activePage?.id]);

  useEffect(() => {
    if (!activeSource) {
      setActiveSourceNotebook(null);
      setSourceConceptLinks(null);
      return;
    }
    let cancelled = false;
    Promise.all([
      SourcesAPI.getSourceNotebook(activeSource.id).catch(() => null),
      SourcesAPI.getSourceConceptLinks(activeSource.id).catch(() => null),
    ])
      .then(([notebook, conceptLinks]) => {
        if (!cancelled) {
          setActiveSourceNotebook(notebook);
          setSourceConceptLinks(conceptLinks);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setActiveSourceNotebook(null);
          setSourceConceptLinks(null);
        }
      });
    return () => {
      cancelled = true;
    };
  }, [activeSource?.id]);

  useEffect(() => {
    let cancelled = false;
    SourcesAPI.getTopicSourceConceptGraph(topicId)
      .then((graph) => {
        if (!cancelled) setSourceConceptGraph(graph);
      })
      .catch(() => {
        if (!cancelled) setSourceConceptGraph(null);
      });
    return () => {
      cancelled = true;
    };
  }, [topicId, sourceConceptSyncing]);

  useEffect(() => {
    if (mode === "orkalm" || !activePage?.id || activePage.id.startsWith("orkalm-source-")) {
      setActivePageSourceLinks(null);
      return;
    }
    let cancelled = false;
    SourcesAPI.getWikiPageSourceLinks(activePage.id)
      .then((links) => {
        if (!cancelled) setActivePageSourceLinks(links);
      })
      .catch(() => {
        if (!cancelled) setActivePageSourceLinks(null);
      });
    return () => {
      cancelled = true;
    };
  }, [mode, activePage?.id, sourceConceptSyncing]);

  useEffect(() => {
    if (!activePage?.id || activePage.id.startsWith("orkalm-source-")) {
      setWikiCopilot(null);
      setWikiCopilotLoading(false);
      return;
    }
    let cancelled = false;
    setWikiCopilotLoading(true);
    WikiAPI.getPageCopilot(activePage.id)
      .then((context) => {
        if (!cancelled) setWikiCopilot(context);
      })
      .catch(() => {
        if (!cancelled) setWikiCopilot(null);
      })
      .finally(() => {
        if (!cancelled) setWikiCopilotLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [activePage?.id, activePage?.blocks?.length, notebookRefreshTick]);

  useEffect(() => {
    if (!activePage?.blocks || activePage.blocks.length === 0) {
      setGlossary([]);
      setTimeline([]);
      setMindMap(null);
      setStudyCards([]);
      setRecommendations([]);
      setWeakSkills([]);
      setLearningCache(null);
      return;
    }
    setNotebookToolsLoading(true);
    Promise.allSettled([
      WikiAPI.getGlossary(topicId),
      WikiAPI.getTimeline(topicId),
      WikiAPI.getMindMap(topicId),
      WikiAPI.getStudyCards(topicId),
      WikiAPI.getRecommendations(topicId),
      LearningAPI.getTopicSummary(topicId),
    ])
      .then(([g, t, m, c, r, l]) => {
        if (g.status === "fulfilled") setGlossary(g.value.items ?? []);
        if (t.status === "fulfilled") setTimeline(t.value.items ?? []);
        if (m.status === "fulfilled") setMindMap({ mermaid: m.value.mermaid, nodes: m.value.nodes ?? [] });
        if (c.status === "fulfilled") setStudyCards(c.value.cards ?? []);
        if (r.status === "fulfilled") setRecommendations(r.value ?? []);
        if (l.status === "fulfilled") {
          setWeakSkills(l.value.weakSkills ?? []);
          setLearningCache(l.value.cache ?? null);
        }
      })
      .finally(() => setNotebookToolsLoading(false));
  }, [topicId, activePage?.blocks?.length]);

  const handleUploadSource = async (file: File | undefined) => {
    if (!file) return;
    if (mode !== "orkalm") {
      toast.error("Kaynak yukleme sadece OrkaLM icinde kullanilir.");
      return;
    }
    setUploadingSource(true);
    try {
      const uploaded = await SourcesAPI.upload({ topicId, file });
      toast.success(`${uploaded.fileName} kaynaklara eklendi.`);
      await refreshSources();
      setActiveSource(uploaded);
      setNotebookRefreshTick((tick) => tick + 1);
    } catch {
      toast.error("Kaynak yüklenemedi.");
    } finally {
      setUploadingSource(false);
    }
  };

  const handleAskSource = async () => {
    if (!activeSource || !sourceQuestion.trim() || sourceAsking) return;
    setSourceAsking(true);
    setSourceAnswer("");
    setSourceCitations([]);
    setSourceQuestionResponse(null);
    try {
      const result = await SourcesAPI.ask(activeSource.id, sourceQuestion.trim(), {
        topicId,
        wikiPageId: mode === "orkalm" ? undefined : activePage?.id,
        mode: "selected_source",
        includeLearnerContext: true,
        writeWikiTrace: mode !== "orkalm",
      });
      setSourceAnswer(result.answer);
      setSourceQuestionResponse(result);
      setSourceCitations((result.citations ?? []).map((citation) => ({
        id: citation.sourceChunkId ?? citation.citationId,
        pageNumber: citation.pageNumber ?? 1,
        chunkIndex: citation.chunkIndex ?? 0,
        label: citation.label,
        supportStatus: citation.supportStatus,
      })));
      const firstCitation = result.citations?.[0];
      if (firstCitation?.pageNumber) {
        await openSourcePage(activeSource.id, firstCitation.pageNumber, {
          focusChunkId: firstCitation.sourceChunkId ?? undefined,
          action: "source-answer-first-citation",
        });
      }
      SourcesAPI.getTopicQuality(topicId).then(setSourceQuality).catch(() => undefined);
      setNotebookRefreshTick((tick) => tick + 1);
    } catch {
      toast.error("Kaynaklı cevap üretilemedi.");
    } finally {
      setSourceAsking(false);
    }
  };

  const handleAskSourceCollection = async () => {
    if (!sourceQuestion.trim() || sourceAsking) return;
    setSourceAsking(true);
    setSourceAnswer("");
    setSourceCitations([]);
    setSourceQuestionResponse(null);
    try {
      const result = await SourcesAPI.askTopicSources(topicId, sourceQuestion.trim(), {
        sourceIds: activeSource ? [activeSource.id] : undefined,
        wikiPageId: mode === "orkalm" ? undefined : activePage?.id,
        mode: activeSource ? "selected_source" : "source_collection",
        includeLearnerContext: true,
        writeWikiTrace: mode !== "orkalm",
      });
      setSourceAnswer(result.answer);
      setSourceQuestionResponse(result);
      setSourceCitations((result.citations ?? []).map((citation) => ({
        id: citation.sourceChunkId ?? citation.citationId,
        pageNumber: citation.pageNumber ?? 1,
        chunkIndex: citation.chunkIndex ?? 0,
        label: citation.label,
        supportStatus: citation.supportStatus,
      })));
      if (result.context?.sourceId && !activeSource) {
        const matched = sources.find((source) => source.id === result.context.sourceId);
        if (matched) setActiveSource(matched);
      }
      SourcesAPI.getTopicQuality(topicId).then(setSourceQuality).catch(() => undefined);
      setNotebookRefreshTick((tick) => tick + 1);
    } catch {
      toast.error("Kaynak defteri cevabi uretilemedi.");
    } finally {
      setSourceAsking(false);
    }
  };

  const handleCreateQuestionThread = async () => {
    if (!sourceQuestion.trim() || threadLoading) return;
    setThreadLoading(true);
    try {
      const thread = await SourcesAPI.createQuestionThread({
        topicId,
        sourceId: activeSource?.id,
        sourceIds: activeSource ? [activeSource.id] : selectedCompareSourceIds,
        wikiPageId: mode === "orkalm" ? undefined : activePage?.id,
        conceptKey: mode === "orkalm" ? undefined : activePage?.conceptKey ?? undefined,
        initialQuestion: sourceQuestion.trim(),
        mode: activeSource ? "selected_source" : "source_collection",
        includeLearnerContext: true,
        writeWikiTrace: false,
      });
      setActiveQuestionThread(thread);
      await refreshQuestionThreads();
      setNotebookRefreshTick((tick) => tick + 1);
      toast.success("Source Q&A thread kaydedildi.");
    } catch {
      toast.error("Source Q&A thread olusturulamadi.");
    } finally {
      setThreadLoading(false);
    }
  };

  const handleAskThreadFollowUp = async () => {
    if (!activeQuestionThread || !threadFollowUp.trim() || threadLoading) return;
    setThreadLoading(true);
    try {
      const thread = await SourcesAPI.askQuestionThread(activeQuestionThread.threadId, {
        question: threadFollowUp.trim(),
        includeLearnerContext: true,
        writeWikiTrace: false,
      });
      setActiveQuestionThread(thread);
      setThreadFollowUp("");
      await refreshQuestionThreads();
      setNotebookRefreshTick((tick) => tick + 1);
    } catch {
      toast.error("Follow-up kaydedilemedi.");
    } finally {
      setThreadLoading(false);
    }
  };

  const handleReviewThread = async (reviewStatus: string) => {
    if (!activeQuestionThread || threadLoading) return;
    setThreadLoading(true);
    try {
      const thread = await SourcesAPI.reviewQuestionThread(activeQuestionThread.threadId, {
        reviewStatus,
        warnings: reviewStatus === "needs_review" ? ["student_marked_for_review"] : [],
      });
      setActiveQuestionThread(thread);
      await refreshQuestionThreads();
    } catch {
      toast.error("Thread review durumu guncellenemedi.");
    } finally {
      setThreadLoading(false);
    }
  };

  const handleWriteThreadTrace = async () => {
    if (!activeQuestionThread || threadLoading) return;
    if (mode === "orkalm") {
      toast("OrkaLM source Q&A su an Wiki'ye yazilmaz; iki yuzey ayri tutuluyor.");
      return;
    }
    setThreadLoading(true);
    try {
      await SourcesAPI.writeQuestionThreadWikiTrace(activeQuestionThread.threadId);
      const thread = await SourcesAPI.getQuestionThread(activeQuestionThread.threadId);
      setActiveQuestionThread(thread);
      await refreshQuestionThreads();
      setNotebookRefreshTick((tick) => tick + 1);
      toast.success("Thread Wiki notuna baglandi.");
    } catch {
      toast.error("Wiki trace yazilamadi.");
    } finally {
      setThreadLoading(false);
    }
  };

  const toggleCompareSource = (sourceId: string) => {
    setSelectedCompareSourceIds((current) =>
      current.includes(sourceId)
        ? current.filter((id) => id !== sourceId)
        : [...current, sourceId].slice(0, 8)
    );
  };

  const handleCompareSources = async () => {
    const sourceIds = selectedCompareSourceIds.filter(Boolean);
    if (sourceIds.length < 2 || sourceComparing) return;
    setSourceComparing(true);
    try {
      const result = await SourcesAPI.compareTopicSources(topicId, {
        topicId,
        sourceIds,
        wikiPageId: mode === "orkalm" ? undefined : activePage?.id,
        conceptKey: mode === "orkalm" ? undefined : activePage?.conceptKey ?? undefined,
        includeConceptLinks: true,
        includeCitationReview: true,
        writeWikiTrace: mode !== "orkalm",
      });
      setSourceCompare(result);
      setCitationReview({
        topicId,
        sourceId: null,
        reviewStatus: result.citationCoverage.coverageStatus,
        coverage: result.citationCoverage,
        items: result.citationReviewItems,
        warnings: result.warnings,
        generatedAt: result.generatedAt,
      });
      toast.success("Kaynak karsilastirma hazir.");
      setNotebookRefreshTick((tick) => tick + 1);
    } catch {
      toast.error("Kaynaklar karsilastirilamadi.");
    } finally {
      setSourceComparing(false);
    }
  };

  const openSourcePage = async (
    sourceId: string,
    page: number,
    options?: { focusChunkId?: string; action?: string }
  ) => {
    setSourcePageLoading(true);
    try {
      const pageData = await SourcesAPI.getPage(sourceId, page);
      setSourcePage(pageData);
      setFocusedChunkId(options?.focusChunkId ?? null);
      const src = sources.find((s) => s.id === sourceId);
      if (src) setActiveSource(src);
      recordWikiAction(options?.action ?? "source-page-opened", `${src?.fileName ?? sourceId} / sayfa ${page}`, {
        sourceId,
        page,
        focusChunkId: options?.focusChunkId,
      });
      window.setTimeout(() => {
        if (sourceViewerRef.current) {
          sourceViewerRef.current.scrollIntoView({ behavior: "smooth", block: "nearest" });
        }
      }, 80);
    } catch {
      toast.error(options?.action?.includes("citation")
        ? "Bu citation kaynak sayfasına bağlanamadı."
        : "Kaynak sayfası açılamadı.");
    } finally {
      setSourcePageLoading(false);
    }
  };

  const handleSourceClick = async (sourceId: string, page: number) => {
    await openSourcePage(sourceId, page);
  };

  const handleSourcePageNav = async (direction: -1 | 1) => {
    if (!sourcePage || !activeSource || sourcePageLoading) return;
    const nextPage = Math.min(Math.max(sourcePage.pageNumber + direction, 1), Math.max(activeSource.pageCount, 1));
    if (nextPage === sourcePage.pageNumber) return;
    await openSourcePage(activeSource.id, nextPage, { action: "source-page-navigation" });
  };

  const handleCreateAudioOverview = async () => {
    setAudioJob(null);
    const isSourceAudio = isOrkaLm && Boolean(activeSource?.id);
    const hasAudioContext = isSourceAudio || pages.length > 0 || messages.length > 0 || Boolean(wikiContextPageId);
    if (!hasAudioContext) {
      setAudioJob(null);
      toast("Sesli ders icin once OrkaLM kaynagi, Wiki notu veya ders sohbeti gerekiyor.");
      return;
    }

    setAudioLoading(true);
    try {
      const job = await AudioOverviewAPI.create({
        topicId,
        surface: isSourceAudio ? "orkalm" : "wiki",
        sourceId: isSourceAudio ? activeSource?.id : undefined,
        wikiPageId: isSourceAudio ? undefined : wikiContextPageId ?? undefined,
        audioMode,
        ttsQuality,
      });
      setAudioJob(job);
      if (job.status === "ready" || job.status === "completed") {
        toast.success("Sesli özet hazırlandı.");
      } else if (job.status === "failed" || job.errorMessage) {
        toast.error(job.errorMessage || "Sesli özet hazırlanamadı.");
      } else {
        startAudioPolling(job.id);
        toast("Ses dosyası hazırlanıyor...", { icon: "⏳" });
      }
    } catch {
      toast.error("Sesli özet hazırlanamadı.");
    } finally {
      setAudioLoading(false);
    }
  };

  const recordWikiAction = (action: string, label: string, payload?: Record<string, unknown>) => {
    void LearningAPI.recordSignal({
      topicId,
      signalType: "WikiActionClicked",
      skillTag: label.slice(0, 120),
      topicPath: `Wiki > ${action}`,
      isPositive: true,
      payloadJson: JSON.stringify({ action, label, ...(payload ?? {}) }),
    }).catch(() => {});
  };

  const handleSelectSource = (source: LearningSource) => {
    setActiveSource(source);
    recordWikiAction("source-selected", source.fileName, {
      sourceId: source.id,
      pageCount: source.pageCount,
      chunkCount: source.chunkCount,
    });
  };

  const handleDeleteSource = async (source: LearningSource) => {
    const confirmed = window.confirm(`${source.fileName} kaynağını silmek istiyor musun?`);
    if (!confirmed) return;
    try {
      await SourcesAPI.delete(source.id);
      toast.success("Kaynak kaldırıldı.");
      if (activeSource?.id === source.id) {
        setActiveSource(null);
        setSourceAnswer("");
        setSourceCitations([]);
        setSourcePage(null);
      }
      await refreshSources();
      setNotebookRefreshTick((tick) => tick + 1);
    } catch {
      toast.error("Kaynak silinemedi.");
    }
  };

  const handleCitationClick = async (kind: "doc" | "wiki" | "web" | "external", ref: string) => {
    recordWikiAction("citation-clicked", `${kind}:${ref}`, { citationKind: kind, ref });
    if (kind === "doc") {
      const parts = ref.split(":");
      const sourceId = parts[0];
      const page = parseInt(parts[1] || "1", 10);
      if (sourceId && !isNaN(page)) {
        await openSourcePage(sourceId, page, { action: "rich-markdown-citation-navigated" });
      }
    }
  };

  const handleCitationChipClick = async (citation: CitationDto) => {
    const citationId = citation.citationId ?? citation.label ?? "citation";
    if (citation.sourceType === "document" && citation.sourceId && citation.pageNumber) {
      await openSourcePage(citation.sourceId, citation.pageNumber, { action: "wiki-v2-citation-clicked" });
      return;
    }

    recordWikiAction("citation-clicked", citationId, {
      sourceType: citation.sourceType,
      sourceId: citation.sourceId,
      pageNumber: citation.pageNumber,
    });
  };

  // Önerilen soruyu Copilot'a doldur
  const handleSuggestedQuestion = (question: string) => {
    recordWikiAction("recommendation-to-copilot", question);
    setShowCopilot(true);
    setInput(question);
  };

  const askAbout = (label: string) => {
    recordWikiAction("ask-about", label);
    setShowCopilot(true);
    setInput(`${label} konusunu kaynaklara göre açıkla ve ilişkili noktaları göster.`);
  };

  const handleWikiCopilotAction = (action: NonNullable<WikiCopilotContextDto["primaryAction"]>) => {
    if (action.availability === "blocked") {
      toast(action.userSafeDescription || "Bu aksiyon guvenlik nedeniyle sinirlandi.");
      recordWikiAction("wiki-copilot-action-blocked", action.actionType, {
        targetSurface: action.targetSurface,
        reasonCodes: action.reasonCodes,
      });
      return;
    }

    recordWikiAction("wiki-copilot-action", action.actionType, {
      targetSurface: action.targetSurface,
      reasonCodes: action.reasonCodes,
    });

    if (action.actionType === "ask_source" || action.actionType === "inspect_citations") {
      document.getElementById("wiki-study-sources")?.scrollIntoView({ behavior: "smooth", block: "start" });
      return;
    }

    if (action.actionType === "create_study_pack" || action.actionType === "create_flashcards") {
      document.getElementById("wiki-notebook-studio")?.scrollIntoView({ behavior: "smooth", block: "start" });
      return;
    }

    if (action.actionType === "generate_checkpoint" || action.actionType === "start_repair" || action.actionType === "review_weak_concept") {
      document.getElementById("wiki-study-reinforcement")?.scrollIntoView({ behavior: "smooth", block: "start" });
    }

    setShowCopilot(true);
    const pageTitle = activePage?.title ?? "bu Wiki sayfasi";
    setInput(`${pageTitle}: ${action.userSafeLabel}. ${action.userSafeDescription}`);
  };

  const handleSyncWikiGraph = async () => {
    if (wikiGraphSyncing) return;
    setWikiGraphSyncing(true);
    try {
      const result = await WikiAPI.syncGraph(topicId, {
        includeTopicTreeFallback: true,
        createSummaryBlocks: true,
      });
      setWikiGraph(result.graph);
      applyGraphPages(result.graph);
      recordWikiAction("wiki-graph-synced", result.syncStatus, {
        createdPageCount: result.createdPageCount,
        updatedPageCount: result.updatedPageCount,
        createdLinkCount: result.createdLinkCount,
        evidenceStatus: result.evidenceStatus,
      });
      toast.success(result.createdPageCount > 0 ? "Wiki sayfa haritasi hazirlandi." : "Wiki sayfa haritasi guncellendi.");
    } catch {
      toast.error("Wiki sayfa haritasi senkronize edilemedi.");
    } finally {
      setWikiGraphSyncing(false);
    }
  };

  const handleSyncSourceConceptLinks = async () => {
    if (!activeSource || sourceConceptSyncing) return;
    setSourceConceptSyncing(true);
    try {
      const result = await SourcesAPI.syncSourceConceptLinks(activeSource.id);
      setSourceConceptLinks(result);
      const graph = await SourcesAPI.getTopicSourceConceptGraph(topicId).catch(() => null);
      setSourceConceptGraph(graph);
      recordWikiAction("source-concept-links-synced", result.title, {
        sourceId: activeSource.id,
        confirmedLinkCount: result.confirmedLinkCount,
        suggestedLinkCount: result.suggestedLinkCount,
      });
      toast.success("OrkaLM kaynak-kavram onerileri hazirlandi.");
    } catch {
      toast.error("Kaynak-kavram baglari guncellenemedi.");
    } finally {
      setSourceConceptSyncing(false);
    }
  };

  const selectGraphPage = (page: WikiGraphPageDto) => {
    const existing = pages.find((candidate) => candidate.id === page.id);
    const nextPage: WikiPage = existing ?? {
      id: page.id,
      title: page.title,
      pageKey: page.pageKey,
      pageType: page.pageType,
      conceptKey: page.conceptKey,
      parentConceptKey: page.parentConceptKey,
      parentWikiPageId: page.parentWikiPageId,
      sourceReadiness: page.sourceReadiness,
      evidenceStatus: page.evidenceStatus,
      safeSummary: page.safeSummary,
      curation: page.curation,
      orderIndex: page.orderIndex,
      blockCount: page.blockCount,
    };
    setActivePage(nextPage);
    recordWikiAction("wiki-graph-page-selected", page.title, {
      pageType: page.pageType,
      conceptKey: page.conceptKey,
      sourceReadiness: page.sourceReadiness,
    });
  };

  const rootNodes = mindMap?.nodes.filter((node) => !node.parentId) ?? [];
  const childNodes = (parentId: string) =>
    mindMap?.nodes.filter((node) => node.parentId === parentId).sort((a, b) => a.depth - b.depth || a.label.localeCompare(b.label, "tr")) ?? [];
  const sourceGraph = useMemo(() => {
    const totalPages = sources.reduce((sum, source) => sum + (source.pageCount || 0), 0);
    const totalChunks = sources.reduce((sum, source) => sum + (source.chunkCount || 0), 0);
    const readySources = sources.filter((source) => ["ready", "completed", "indexed"].includes((source.status || "").toLowerCase())).length;

    return {
      totalPages,
      totalChunks,
      readySources,
      density: sources.length === 0 ? 0 : Math.round(totalChunks / Math.max(totalPages, 1)),
    };
  }, [sources]);
  const sourceCoverageCoach = buildWikiSourceCoverageCoach({
    sourceCount: sources.length,
    readySources: sourceGraph.readySources,
    totalChunks: sourceGraph.totalChunks,
    quality: sourceQuality,
  });
  const activeGraphPage = useMemo(
    () => wikiGraph?.pages.find((page) => page.id === activePage?.id) ?? null,
    [activePage?.id, wikiGraph],
  );
  const wikiGraphPageById = useMemo(() => {
    const map = new Map<string, WikiGraphPageDto>();
    for (const page of wikiGraph?.pages ?? []) map.set(page.id, page);
    return map;
  }, [wikiGraph]);
  const wikiVaultPages = useMemo<WikiGraphPageDto[]>(() => {
    const graphPages = wikiGraph?.pages ?? [];
    const pageMap = new Map<string, WikiGraphPageDto>();

    for (const page of graphPages) pageMap.set(page.id, page);
    for (const page of pages) {
      if (pageMap.has(page.id)) continue;
      pageMap.set(page.id, {
        id: page.id,
        topicId,
        parentWikiPageId: page.parentWikiPageId,
        pageKey: page.pageKey ?? page.id,
        pageType: page.pageType ?? "page",
        conceptKey: page.conceptKey,
        parentConceptKey: page.parentConceptKey,
        title: page.title,
        status: "ready",
        sourceReadiness: page.sourceReadiness ?? "evidence_insufficient",
        evidenceStatus: page.evidenceStatus ?? "evidence_insufficient",
        safeSummary: page.safeSummary,
        contentReadiness: page.contentReadiness ?? (page.blocks?.length ? "ready" : "skeleton"),
        hasLearningContent: page.hasLearningContent ?? Boolean(page.blocks?.length),
        visibleBlockCount: page.visibleBlockCount ?? page.blockCount ?? page.blocks?.length ?? 0,
        requiredBlockTypesPresent: page.requiredBlockTypesPresent ?? Boolean(page.safeSummary || page.blocks?.length),
        orderIndex: page.orderIndex ?? 0,
        blockCount: page.blockCount ?? page.blocks?.length ?? 0,
        learningSystemBinding: page.learningSystemBinding ?? null,
        updatedAt: "",
      });
    }

    return Array.from(pageMap.values()).sort((a, b) => a.orderIndex - b.orderIndex || a.title.localeCompare(b.title, "tr"));
  }, [pages, topicId, wikiGraph]);
  const wikiVaultFilteredPages = useMemo(() => {
    const query = normalizeVaultText(wikiVaultQuery.trim());
    return wikiVaultPages.filter((page) => {
      if (!pageMatchesVaultFilter(page, wikiVaultFilter)) return false;
      if (!query) return true;
      return normalizeVaultText([
        page.title,
        page.pageKey,
        page.pageType,
        page.conceptKey,
        page.safeSummary,
        page.sourceReadiness,
        page.evidenceStatus,
      ].filter(Boolean).join(" ")).includes(query);
    });
  }, [wikiVaultFilter, wikiVaultPages, wikiVaultQuery]);
  const wikiVaultTreeRows = useMemo(() => {
    const filteredIds = new Set(wikiVaultFilteredPages.map((page) => page.id));
    const roots = wikiVaultFilteredPages.filter((page) => !page.parentWikiPageId || !filteredIds.has(page.parentWikiPageId));
    const childrenByParent = new Map<string, WikiGraphPageDto[]>();
    for (const page of wikiVaultFilteredPages) {
      if (!page.parentWikiPageId || !filteredIds.has(page.parentWikiPageId)) continue;
      const current = childrenByParent.get(page.parentWikiPageId) ?? [];
      current.push(page);
      childrenByParent.set(page.parentWikiPageId, current);
    }

    const rows: Array<{ page: WikiGraphPageDto; depth: number }> = [];
    const visit = (page: WikiGraphPageDto, depth: number) => {
      rows.push({ page, depth });
      for (const child of childrenByParent.get(page.id) ?? []) visit(child, depth + 1);
    };
    roots.forEach((page) => visit(page, 0));
    return rows;
  }, [wikiVaultFilteredPages]);
  const activeOutgoingLinks = useMemo(
    () => wikiGraph?.links.filter((link) => link.sourcePageId === activePage?.id) ?? [],
    [activePage?.id, wikiGraph],
  );
  const activeBacklinks = useMemo(
    () => wikiGraph?.links.filter((link) => link.targetPageId === activePage?.id) ?? [],
    [activePage?.id, wikiGraph],
  );
  const wikiBlockGroups = useMemo(() => {
    const blocks = activePage?.blocks ?? [];
    const groups = new Map<string, { id: string; label: string; blocks: NonNullable<WikiPage["blocks"]> }>();
    for (const block of blocks) {
      const group = blockGroupFor(block.type);
      const current = groups.get(group.id) ?? { ...group, blocks: [] };
      current.blocks.push(block);
      groups.set(group.id, current);
    }
    return Array.from(groups.values());
  }, [activePage?.blocks]);
  const relatedGraphPages = useMemo(() => {
    if (!wikiGraph || !activePage) return [];
    const relatedIds = new Set(
      wikiGraph.links
        .filter((link) => link.sourcePageId === activePage.id || link.targetPageId === activePage.id)
        .flatMap((link) => [link.sourcePageId, link.targetPageId].filter(Boolean) as string[])
        .filter((id) => id !== activePage.id)
    );
    return wikiGraph.pages.filter((page) => relatedIds.has(page.id)).slice(0, 8);
  }, [activePage, wikiGraph]);
  const wikiGraphWarnings = wikiGraph?.warnings ?? [];
  const workspaceState = useLearningWorkspaceState({ topicId });
  const hasSourceQualityConcern =
    sourceCoverageCoach.tone === "watch" ||
    sourceQuality?.retrievalHealthStatus === "degraded" ||
    sourceQuality?.citationCoverageStatus === "degraded" ||
    (sourceQuality?.emptyRunCount ?? 0) > 0 ||
    ((sourceQuality?.unsupportedCitationCount ?? 0) + (sourceQuality?.citationMissingCount ?? 0)) > 0;
  const pagePracticeReadyCount = wikiPageQuestionSet?.questions?.length ?? wikiPagePractice?.questions?.length ?? 0;

  const handleStartWikiPagePractice = async () => {
    if (!activePage?.id || wikiPagePracticeLoading) return;
    setWikiPagePracticeLoading(true);
    setWikiPagePracticeResult(null);
    setWikiPracticeAnswers({});
    try {
      const session = await WikiAPI.startPagePractice(activePage.id, {
        count: 5,
        mode: "wiki_page_practice",
      });
      setWikiPagePractice(session);
      if (session.status === "ready" && session.questions.length > 0) {
        toast.success(`${session.questions.length} wiki pratiği hazır.`);
      } else {
        toast("Bu wiki sayfası için henüz practice-ready soru yok.", { icon: "ℹ️" });
      }
    } catch {
      toast.error("Wiki pratiği başlatılamadı.");
    } finally {
      setWikiPagePracticeLoading(false);
    }
  };

  const handleSubmitWikiPagePractice = async () => {
    if (!wikiPagePractice || wikiPagePractice.questions.length === 0 || wikiPagePracticeLoading) return;
    setWikiPagePracticeLoading(true);
    try {
      const result = await QuestionPracticeAPI.submit({
        practiceSetId: wikiPagePractice.practiceSetId,
        topicId: wikiPagePractice.topicId ?? activePage?.topicId ?? topicId,
        mode: wikiPagePractice.mode,
        answers: wikiPagePractice.questions.map((question) => {
          const selected = wikiPracticeAnswers[question.questionItemId];
          return {
            questionItemId: question.questionItemId,
            selectedOptionKey: selected ?? null,
            wasSkipped: !selected,
          };
        }),
      });
      setWikiPagePracticeResult(result);
      toast.success("Wiki pratiği kaydedildi.");
    } catch {
      toast.error("Wiki pratiği kaydedilemedi.");
    } finally {
      setWikiPagePracticeLoading(false);
    }
  };

  const wikiStudyPackItems = useMemo(
    () => buildWikiStudyPackItems({
      hasBriefing: !!briefing,
      isBriefingLoading: briefingLoading,
      pagePracticeReadyCount,
      glossaryCount: glossary.length,
      studyCardCount: studyCards.length,
      recommendationCount: recommendations.length,
      weakSkillCount: weakSkills.length,
      sourceCount: sources.length,
      readySources: sourceGraph.readySources,
      totalChunks: sourceGraph.totalChunks,
      hasSourceQualityConcern,
    }),
    [
      briefing,
      briefingLoading,
      glossary.length,
      hasSourceQualityConcern,
      pagePracticeReadyCount,
      recommendations.length,
      sourceGraph.readySources,
      sourceGraph.totalChunks,
      sources.length,
      studyCards.length,
      weakSkills.length,
    ],
  );
  const scrollToStudySection = (section: WikiStudySection) => {
    document
      .getElementById(`wiki-study-${section}`)
      ?.scrollIntoView({ behavior: "smooth", block: "start" });
  };

  // ─── Copilot Send ────────────────────────────────────────
  const handleSend = async () => {
    if (!input.trim() || isStreaming) return;
    const userQ = input.trim();
    setInput("");
    setMessages((prev) => [...prev, { role: "user", content: userQ }]);
    setIsStreaming(true);
    setMessages((prev) => [...prev, { role: "assistant", content: "" }]);

    try {
      const apiBase =
        (import.meta as unknown as { env: Record<string, string> }).env
          ?.VITE_API_BASE_URL ?? "";

      const endpoint = `${apiBase}/api/wiki/${topicId}/chat`;

      const response = await fetch(endpoint, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${storage.getToken()}`,
        },
        body: JSON.stringify({
          question: userQ,
          mode,
          sourceId: activeSource?.id,
          activePageId: activePage?.id,
        }),
      });

      if (!response.ok) throw new Error("Ajan yanıt veremedi.");

      const reader = response.body?.getReader();
      const decoder = new TextDecoder();
      let aiContent = "";
      let buffer = "";

      while (reader) {
        const { value, done } = await reader.read();
        if (done) break;

        const chunk = decoder.decode(value, { stream: true });
        buffer += chunk;
        const lines = buffer.split("\n");
        buffer = lines.pop() || "";
        for (const line of lines) {
          if (line.startsWith("data: ")) {
            const str = line.replace("data: ", "").trim();
            if (!str || str === "[DONE]") continue;
            try {
              const json = JSON.parse(str);
              if (json.type === "token" && json.content) {
                aiContent += json.content;
              } else if (!json.type && json.content) {
                aiContent += json.content;
              } else if (json.type === "citation" && Array.isArray(json.citations)) {
                patchLastAssistantMessage({ citations: json.citations });
                continue;
              } else if (json.type === "artifact_ready" && json.artifactId) {
                void TutorAPI.getArtifact(json.artifactId)
                  .then((artifact) => {
                    addArtifactToLastAssistant(artifact);
                    return TutorAPI.markArtifactRendered(artifact.id);
                  })
                  .catch(() => {});
                continue;
              } else if (json.type === "metadata" && json.metadata) {
                patchLastAssistantMessage({ metadata: json.metadata });
                continue;
              } else if (json.type === "final") {
                patchLastAssistantMessage({ groundingStatus: json.groundingStatus ?? null });
                continue;
              } else {
                aiContent += str;
              }
            } catch {
              aiContent += str;
            }

            setMessages((prev) => {
              const updated = [...prev];
              updated[updated.length - 1] = {
                ...updated[updated.length - 1],
                content: aiContent,
              };
              return updated;
            });
          }
        }
      }
    } catch (err) {
      console.error(err);
      setMessages((prev) => {
        const updated = [...prev];
        updated[updated.length - 1] = {
          ...updated[updated.length - 1],
          content: "⚠️ Üzgünüm, şu an bağlantı kuramadım. Tekrar deneyin.",
        };
        return updated;
      });
    } finally {
      setIsStreaming(false);
    }
  };

  // ─── Render ──────────────────────────────────────────────
  const isOrkaLm = mode === "orkalm";
  const wikiContextPageId = !isOrkaLm && activePage?.id && !activePage.id.startsWith("orkalm-source-")
    ? activePage.id
    : undefined;
  const notebookSurface: "wiki_page" | "source_notebook" | "milestone" = isOrkaLm
    ? "source_notebook"
    : activePage
    ? "wiki_page"
    : "milestone";
  const HeaderIcon = isOrkaLm ? Network : BookOpen;
  const surfaceBreadcrumb = isOrkaLm ? "OrkaLM Notebook" : "Mufredat Haritasi";
  const surfaceTitle = isOrkaLm ? "OrkaLM" : (activePage?.title || "Wiki");
  const visibleSurfaceSubtitle = isOrkaLm
    ? "PDF, TXT ve MD kaynaklarını yükle; kaynak grafiği, kanıt paneli, özet, terimler, zihin haritası, UML, quiz, flashcard ve slayt taslağını aynı kaynak merkezinde çalıştır."
    : null;
  const surfaceSubtitle = isOrkaLm
    ? "PDF, TXT ve MD kaynaklarını yükle; kaynak grafiği, kanıt paneli, özet, terimler, zihin haritası ve sesli ders akışını aynı kaynak merkezinde çalıştır."
    : null;

  return (
    <motion.div
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      transition={{ duration: 0.2 }}
      className="flex-1 flex bg-transparent overflow-hidden relative"
    >
      {/* ─── LEFT PANE: WIKI CONTENT ─── */}
      <div className="flex-1 flex flex-col h-full overflow-hidden">
        {/* Header */}
        <div className="px-6 py-4 flex items-center justify-between flex-shrink-0 border-b border-[#526d82]/15 bg-[#f7f9fa]/62 backdrop-blur-sm z-10">
          <div className="flex flex-col gap-1 min-w-0 pr-8">
            <div className="flex items-center gap-1.5 text-xs text-[#667085] truncate font-medium tracking-wide">
              <span>{surfaceBreadcrumb}</span>
              <span className="text-zinc-700">/</span>
              <span className="text-[#344054] truncate">
                {activePage?.title || "Konu"}
              </span>
            </div>
            <h3 className="text-xl font-bold text-[#172033] truncate flex items-center gap-2.5">
              <HeaderIcon className="w-5 h-5 text-[#667085]" />
              <span>{surfaceTitle}</span>
            </h3>
            {(visibleSurfaceSubtitle || surfaceSubtitle) && (
              <p className="max-w-3xl text-xs leading-relaxed text-[#667085]">
                {visibleSurfaceSubtitle || surfaceSubtitle}
              </p>
            )}
          </div>
          {/* Sadece kapatıp sohbet listesine dönmek istenebileceği ihtimali için ufak buton */}
          <button
            type="button"
            onClick={handleSyncWikiGraph}
            disabled={wikiGraphSyncing}
            className="mr-2 inline-flex items-center gap-2 rounded-lg border border-[#526d82]/15 bg-white/62 px-3 py-2 text-xs font-black text-[#344054] transition hover:bg-[#dcecf3]/70 disabled:opacity-50"
            title="Wiki sayfa haritasini senkronize et"
          >
            {wikiGraphSyncing ? <Loader2 className="h-4 w-4 animate-spin" /> : <Network className="h-4 w-4" />}
            Harita
          </button>
          <button
            onClick={onClose}
            className="text-[#667085] hover:text-[#344054] hover:bg-[#dcecf3]/70 transition-colors duration-150 p-2 rounded-lg"
            title="Dersi Kapat"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content Area */}
        <div className="flex-1 overflow-y-auto px-8 lg:px-16 py-8 sidebar-scrollbar scroll-smooth">
          {loading && (
            <div className="flex flex-col items-center justify-center h-40 gap-3">
              <Loader2 className="w-6 h-6 text-emerald-500 animate-spin" />
              <span className="text-sm text-[#667085]">Ders yükleniyor...</span>
            </div>
          )}

          {!loading && error && (
            <div className="text-center py-16 bg-[#f7f9fa]/58 rounded-2xl border border-[#526d82]/14">
              <p className="text-base text-[#667085] mb-2">
                Wiki içeriği henüz oluşturulmadı.
              </p>
              <p className="text-sm text-[#98a2b3]">
                Sistem konuyu hazırlarken lütfen bekleyin.
              </p>
            </div>
          )}

          {!loading && !error && pages.length === 0 && isPolling && (
            <WikiGeneratingSkeleton />
          )}

          {!loading && !error && pages.length === 0 && !isPolling && (
            <div className="text-center py-16">
              <p className="text-base text-[#667085] mb-2">Wiki içeriği bulunamadı.</p>
              <p className="text-sm text-[#98a2b3]">Bir konu anlatımı tamamlandığında wiki otomatik oluşturulur.</p>
            </div>
          )}

          {!loading && activePage && (
            <div className="max-w-4xl mx-auto pb-12">
              <div className="mb-8">
                <h1 className="text-2xl md:text-3xl font-extrabold text-[#172033] tracking-tight">
                  {activePage.title}
                </h1>
              </div>

              <div className="mb-6 rounded-2xl border border-[#526d82]/16 bg-white/72 p-4 shadow-sm">
                <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="flex items-center gap-2 text-[11px] font-black uppercase tracking-[0.18em] text-[#52768a]">
                      <BookOpen className="h-4 w-4" />
                      Wiki Vault
                    </div>
                    <p className="mt-1 max-w-2xl text-sm leading-6 text-[#667085]">
                      Sayfa agaci, aktif sayfa baglami, backlink ve Notebook Studio ciktilari ayni calisma kasasinda izlenir.
                    </p>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    <span className={`rounded-full border px-2.5 py-1 text-[10px] font-bold ${pageBadgeTone(activeGraphPage?.status ?? "ready")}`}>
                      {userSafeStatus(activeGraphPage?.status ?? "ready")}
                    </span>
                    <span className={`rounded-full border px-2.5 py-1 text-[10px] font-bold ${pageBadgeTone(activeGraphPage?.evidenceStatus ?? activePage.evidenceStatus)}`}>
                      {userSafeStatus(activeGraphPage?.evidenceStatus ?? activePage.evidenceStatus ?? "evidence_insufficient")}
                    </span>
                  </div>
                </div>

                <div className="grid gap-4 xl:grid-cols-[1.25fr_0.9fr_0.95fr]">
                  <div className="rounded-xl border border-[#526d82]/12 bg-[#f7f9fa]/70 p-3">
                    <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
                      <div className="text-[10px] font-black uppercase tracking-[0.18em] text-[#667085]">
                        Page tree / list
                      </div>
                      <span className="rounded-full bg-white px-2 py-0.5 text-[10px] font-bold text-[#667085]">
                        {wikiVaultFilteredPages.length}/{wikiVaultPages.length} sayfa
                      </span>
                    </div>
                    <label className="mb-3 flex items-center gap-2 rounded-xl border border-[#526d82]/14 bg-white/78 px-3 py-2">
                      <Search className="h-4 w-4 shrink-0 text-[#98a2b3]" />
                      <input
                        value={wikiVaultQuery}
                        onChange={(event) => setWikiVaultQuery(event.target.value)}
                        placeholder="Sayfa, concept veya kaynak ara"
                        className="min-w-0 flex-1 bg-transparent text-xs font-semibold text-[#172033] outline-none placeholder:text-[#98a2b3]"
                      />
                    </label>
                    <div className="mb-3 flex gap-1.5 overflow-x-auto pb-1 sidebar-scrollbar">
                      {wikiVaultFilters.map((filter) => (
                        <button
                          key={filter.id}
                          type="button"
                          onClick={() => setWikiVaultFilter(filter.id)}
                          className={`whitespace-nowrap rounded-full border px-2.5 py-1 text-[10px] font-bold transition ${
                            wikiVaultFilter === filter.id
                              ? "border-[#172033]/18 bg-white text-[#172033]"
                              : "border-[#526d82]/12 bg-[#f7f9fa]/70 text-[#667085] hover:bg-white"
                          }`}
                        >
                          {filter.label}
                        </button>
                      ))}
                    </div>
                    <div className="max-h-72 space-y-1.5 overflow-y-auto pr-1 sidebar-scrollbar">
                      {wikiVaultTreeRows.length === 0 ? (
                        <div className="rounded-xl border border-[#526d82]/12 bg-white/64 px-3 py-3 text-xs font-semibold text-[#667085]">
                          Bu filtre icin sayfa bulunamadi.
                        </div>
                      ) : (
                        wikiVaultTreeRows.slice(0, 40).map(({ page, depth }) => (
                          <button
                            key={page.id}
                            type="button"
                            onClick={() => selectGraphPage(page)}
                            className={`w-full rounded-xl border px-3 py-2 text-left transition ${
                              activePage.id === page.id
                                ? "border-[#172033]/20 bg-white text-[#172033]"
                                : "border-[#526d82]/10 bg-white/55 text-[#344054] hover:border-[#9ec7d9]/45 hover:bg-white"
                            }`}
                            style={{ paddingLeft: `${12 + Math.min(depth, 3) * 14}px` }}
                          >
                            <div className="flex min-w-0 items-center justify-between gap-2">
                              <span className="truncate text-xs font-black">{page.title}</span>
                              <span className="shrink-0 rounded-full bg-[#f7f9fa] px-1.5 py-0.5 text-[9px] font-bold text-[#667085]">
                                {page.blockCount}
                              </span>
                            </div>
                            <div className="mt-1 flex flex-wrap gap-1">
                              <span className={`rounded-full border px-1.5 py-0.5 text-[9px] font-bold ${pageBadgeTone(page.pageType)}`}>
                                {userSafeStatus(page.pageType)}
                              </span>
                              <span className={`rounded-full border px-1.5 py-0.5 text-[9px] font-bold ${pageBadgeTone(page.sourceReadiness)}`}>
                                {userSafeStatus(page.sourceReadiness)}
                              </span>
                            </div>
                          </button>
                        ))
                      )}
                    </div>
                  </div>

                  <div className="rounded-xl border border-[#526d82]/12 bg-[#f7f9fa]/70 p-3">
                    <div className="mb-3 text-[10px] font-black uppercase tracking-[0.18em] text-[#667085]">
                      Aktif sayfa baglami
                    </div>
                    <div className="space-y-2 text-xs">
                      <div className="rounded-xl bg-white/72 px-3 py-2">
                        <div className="text-[10px] font-bold uppercase tracking-[0.14em] text-[#98a2b3]">Sayfa</div>
                        <div className="mt-1 font-black text-[#172033]">{activePage.title}</div>
                      </div>
                      <div className="flex flex-wrap gap-1.5">
                        <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold ${pageBadgeTone(activeGraphPage?.pageType ?? activePage.pageType)}`}>
                          {userSafeStatus(activeGraphPage?.pageType ?? activePage.pageType ?? "page")}
                        </span>
                        <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold ${pageBadgeTone(activeGraphPage?.sourceReadiness ?? activePage.sourceReadiness)}`}>
                          {userSafeStatus(activeGraphPage?.sourceReadiness ?? activePage.sourceReadiness ?? "evidence_insufficient")}
                        </span>
                        <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold ${pageBadgeTone(activeGraphPage?.evidenceStatus ?? activePage.evidenceStatus)}`}>
                          {userSafeStatus(activeGraphPage?.evidenceStatus ?? activePage.evidenceStatus ?? "evidence_insufficient")}
                        </span>
                      </div>
                      {(activeGraphPage?.conceptKey ?? activePage.conceptKey) && (
                        <div className="rounded-xl border border-[#526d82]/10 bg-white/64 px-3 py-2">
                          <div className="text-[10px] font-bold uppercase tracking-[0.14em] text-[#98a2b3]">Concept</div>
                          <div className="mt-1 font-mono text-[11px] font-bold text-[#52768a]">
                            {activeGraphPage?.conceptKey ?? activePage.conceptKey}
                          </div>
                        </div>
                      )}
                      {(activeGraphPage?.parentWikiPageId ?? activePage.parentWikiPageId) && (
                        <button
                          type="button"
                          onClick={() => {
                            const parent = wikiGraphPageById.get((activeGraphPage?.parentWikiPageId ?? activePage.parentWikiPageId)!);
                            if (parent) selectGraphPage(parent);
                          }}
                          className="w-full rounded-xl border border-[#526d82]/10 bg-white/64 px-3 py-2 text-left transition hover:border-[#9ec7d9]/45"
                        >
                          <div className="text-[10px] font-bold uppercase tracking-[0.14em] text-[#98a2b3]">Parent page</div>
                          <div className="mt-1 truncate text-xs font-black text-[#344054]">
                            {wikiGraphPageById.get((activeGraphPage?.parentWikiPageId ?? activePage.parentWikiPageId)!)?.title ?? "Ust sayfa"}
                          </div>
                        </button>
                      )}
                      {(activeGraphPage?.safeSummary ?? activePage.safeSummary) && (
                        <p className="rounded-xl border border-[#526d82]/10 bg-white/64 px-3 py-2 text-[11px] leading-5 text-[#667085]">
                          {activeGraphPage?.safeSummary ?? activePage.safeSummary}
                        </p>
                      )}
                      {(activeGraphPage?.curation ?? activePage.curation) && (
                        <div className="rounded-xl border border-[#e8c46f]/18 bg-[#fff8ee]/70 px-3 py-2">
                          <div className="flex flex-wrap items-center gap-1.5">
                            <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold ${pageBadgeTone((activeGraphPage?.curation ?? activePage.curation)?.curationStatus)}`}>
                              {userSafeStatus((activeGraphPage?.curation ?? activePage.curation)?.curationStatus)}
                            </span>
                            <span className="text-[10px] font-bold text-[#8a6a33]">
                              {userSafeStatus((activeGraphPage?.curation ?? activePage.curation)?.nextAction)}
                            </span>
                          </div>
                          <p className="mt-1 text-[11px] leading-5 text-[#8a6a33]">
                            {(activeGraphPage?.curation ?? activePage.curation)?.studentVisibleSummary}
                          </p>
                        </div>
                      )}
                      {(wikiCopilot || wikiCopilotLoading) && (
                        <div data-testid="wiki-copilot-panel" className="rounded-xl border border-[#8fb7a2]/22 bg-[#f2faf5]/78 px-3 py-3">
                          <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                            <div className="flex items-center gap-1.5">
                              <Sparkles className="h-3.5 w-3.5 text-[#47725d]" />
                              <span className="text-[10px] font-black uppercase tracking-[0.18em] text-[#47725d]">
                                Wiki Copilot
                              </span>
                            </div>
                            {wikiCopilotLoading ? (
                              <Loader2 className="h-3.5 w-3.5 animate-spin text-[#47725d]" />
                            ) : (
                              <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold ${pageBadgeTone(wikiCopilot?.nextAction)}`}>
                                {userSafeStatus(wikiCopilot?.nextAction ?? "continue_learning")}
                              </span>
                            )}
                          </div>
                          {wikiCopilot && (
                            <>
                              <p className="text-[11px] leading-5 text-[#47725d]">
                                {wikiCopilot.studentVisibleSummary}
                              </p>
                              <div className="mt-2 flex flex-wrap gap-1.5">
                                <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold ${pageBadgeTone(wikiCopilot.repairState)}`}>
                                  {userSafeStatus(wikiCopilot.repairState)}
                                </span>
                                <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold ${pageBadgeTone(wikiCopilot.sourceReadiness)}`}>
                                  {userSafeStatus(wikiCopilot.sourceReadiness)}
                                </span>
                                <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold ${pageBadgeTone(wikiCopilot.notebookPackStatus)}`}>
                                  {userSafeStatus(wikiCopilot.notebookPackStatus)}
                                </span>
                              </div>
                              {wikiCopilot.primaryAction && (
                                <button
                                  type="button"
                                  onClick={() => handleWikiCopilotAction(wikiCopilot.primaryAction!)}
                                  disabled={wikiCopilot.primaryAction.availability === "blocked"}
                                  className="mt-3 w-full rounded-xl border border-[#8fb7a2]/28 bg-white/75 px-3 py-2 text-left transition hover:bg-white disabled:cursor-not-allowed disabled:opacity-60"
                                >
                                  <div className="text-xs font-black text-[#172033]">{wikiCopilot.primaryAction.userSafeLabel}</div>
                                  <div className="mt-0.5 text-[11px] leading-5 text-[#667085]">{wikiCopilot.primaryAction.userSafeDescription}</div>
                                </button>
                              )}
                              {wikiCopilot.suggestedActions.length > 1 && (
                                <div className="mt-2 flex flex-wrap gap-1.5">
                                  {wikiCopilot.suggestedActions.slice(1, 5).map((action) => (
                                    <button
                                      key={`${action.actionType}-${action.availability}-${action.userSafeLabel}`}
                                      type="button"
                                      onClick={() => handleWikiCopilotAction(action)}
                                      disabled={action.availability === "blocked"}
                                      className={`rounded-full border px-2.5 py-1 text-[10px] font-bold transition disabled:cursor-not-allowed disabled:opacity-55 ${
                                        action.availability === "blocked"
                                          ? "border-amber-500/22 bg-[#fff8ee]/80 text-[#8a6a33]"
                                          : "border-[#526d82]/12 bg-white/70 text-[#344054] hover:bg-white"
                                      }`}
                                    >
                                      {action.userSafeLabel}
                                    </button>
                                  ))}
                                </div>
                              )}
                              {wikiCopilot.warnings.length > 0 && (
                                <p className="mt-2 rounded-lg border border-amber-500/18 bg-white/60 px-2 py-1.5 text-[10px] font-semibold leading-4 text-[#8a6a33]">
                                  {wikiCopilot.warnings.slice(0, 2).map(userSafeStatus).join(" / ")}
                                </p>
                              )}
                            </>
                          )}
                        </div>
                      )}
                      {activePageSourceLinks && (
                        <div className="rounded-xl border border-[#526d82]/10 bg-white/64 px-3 py-2">
                          <div className="flex items-center justify-between gap-2">
                            <div className="text-[10px] font-bold uppercase tracking-[0.14em] text-[#98a2b3]">Supporting sources</div>
                            <span className="rounded-full bg-[#f7f9fa] px-2 py-0.5 text-[10px] font-bold text-[#667085]">
                              {activePageSourceLinks.confirmedLinkCount}
                            </span>
                          </div>
                          {activePageSourceLinks.links.length > 0 ? (
                            <div className="mt-2 space-y-1.5">
                              {activePageSourceLinks.links.slice(0, 4).map((link) => (
                                <button
                                  key={`${link.sourceId ?? link.sourcePageId}-${link.linkType}`}
                                  type="button"
                                  onClick={() => {
                                    const source = sources.find((item) => item.id === link.sourceId);
                                    if (source) handleSelectSource(source);
                                  }}
                                  className="w-full rounded-lg border border-[#526d82]/10 bg-[#f7f4ec]/62 px-2.5 py-2 text-left text-[11px] transition hover:border-[#9ec7d9]/45"
                                >
                                  <span className="block truncate font-black text-[#344054]">{link.sourceTitle || "Source"}</span>
                                  <span className="mt-0.5 block truncate font-semibold text-[#667085]">
                                    {userSafeStatus(link.linkType)} / {userSafeStatus(link.confidence)} / {userSafeStatus(link.evidenceStatus)}
                                  </span>
                                </button>
                              ))}
                            </div>
                          ) : (
                            <p className="mt-2 text-[11px] font-semibold text-[#667085]">Bu concept sayfasi icin bagli kaynak yok.</p>
                          )}
                        </div>
                      )}
                    </div>
                  </div>

                  <div className="rounded-xl border border-[#526d82]/12 bg-[#f7f9fa]/70 p-3">
                    <div className="mb-3 flex items-center justify-between gap-2">
                      <div className="text-[10px] font-black uppercase tracking-[0.18em] text-[#667085]">
                        Backlinks / local graph
                      </div>
                      <span className="rounded-full bg-white px-2 py-0.5 text-[10px] font-bold text-[#667085]">
                        {activeBacklinks.length + activeOutgoingLinks.length} link
                      </span>
                    </div>
                    <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-1">
                      <div>
                        <div className="mb-1 text-[10px] font-black uppercase tracking-[0.14em] text-[#52768a]">Geri linkler</div>
                        <div className="space-y-1.5">
                          {activeBacklinks.length === 0 ? (
                            <p className="rounded-lg bg-white/62 px-2.5 py-2 text-[11px] text-[#667085]">Bu sayfaya gelen link yok.</p>
                          ) : (
                            activeBacklinks.slice(0, 5).map((link) => {
                              const source = wikiGraphPageById.get(link.sourcePageId);
                              return (
                                <button
                                  key={link.id}
                                  type="button"
                                  onClick={() => source && selectGraphPage(source)}
                                  className="w-full rounded-lg border border-[#526d82]/10 bg-white/66 px-2.5 py-2 text-left text-[11px] transition hover:border-[#9ec7d9]/45"
                                >
                                  <span className="block truncate font-black text-[#344054]">{source?.title ?? userSafeStatus(link.sourcePageId)}</span>
                                  <span className="mt-0.5 block truncate font-semibold text-[#667085]">{userSafeStatus(link.linkType)} - {link.safeLabel}</span>
                                </button>
                              );
                            })
                          )}
                        </div>
                      </div>
                      <div>
                        <div className="mb-1 text-[10px] font-black uppercase tracking-[0.14em] text-[#52768a]">Cikis linkleri</div>
                        <div className="space-y-1.5">
                          {activeOutgoingLinks.length === 0 ? (
                            <p className="rounded-lg bg-white/62 px-2.5 py-2 text-[11px] text-[#667085]">Bu sayfadan cikan link yok.</p>
                          ) : (
                            activeOutgoingLinks.slice(0, 5).map((link) => {
                              const target = link.targetPageId ? wikiGraphPageById.get(link.targetPageId) : null;
                              return (
                                <button
                                  key={link.id}
                                  type="button"
                                  onClick={() => target && selectGraphPage(target)}
                                  className="w-full rounded-lg border border-[#526d82]/10 bg-white/66 px-2.5 py-2 text-left text-[11px] transition hover:border-[#9ec7d9]/45 disabled:opacity-70"
                                  disabled={!target}
                                >
                                  <span className="block truncate font-black text-[#344054]">{target?.title ?? userSafeStatus(link.targetPageKey)}</span>
                                  <span className="mt-0.5 block truncate font-semibold text-[#667085]">{userSafeStatus(link.linkType)} - {link.safeLabel}</span>
                                </button>
                              );
                            })
                          )}
                        </div>
                      </div>
                    </div>
                    {relatedGraphPages.length > 0 && (
                      <div className="mt-3 rounded-xl border border-[#526d82]/10 bg-white/58 px-3 py-2">
                        <div className="mb-1 text-[10px] font-black uppercase tracking-[0.14em] text-[#98a2b3]">Local komsular</div>
                        <div className="flex flex-wrap gap-1.5">
                          {relatedGraphPages.slice(0, 6).map((page) => (
                            <button
                              key={page.id}
                              type="button"
                              onClick={() => selectGraphPage(page)}
                              className="max-w-[150px] truncate rounded-full border border-[#526d82]/12 bg-[#f7f9fa] px-2 py-1 text-[10px] font-bold text-[#667085] transition hover:bg-white hover:text-[#172033]"
                            >
                              {page.title}
                            </button>
                          ))}
                        </div>
                      </div>
                    )}
                  </div>
                </div>
              </div>

              <div className="mb-6 rounded-2xl border border-[#526d82]/16 bg-white/68 p-4 shadow-sm">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="flex items-center gap-2 text-[11px] font-black uppercase tracking-[0.18em] text-[#52768a]">
                      <Network className="h-4 w-4" />
                      Wiki sayfa grafi
                    </div>
                    <p className="mt-1 text-sm leading-6 text-[#667085]">
                      {wikiGraphLoading
                        ? "Sayfa iliskileri yukleniyor..."
                        : wikiGraph
                          ? `${wikiGraph.pages.length} sayfa, ${wikiGraph.links.length} iliski.`
                          : "Bu konu icin graph bilgisi henuz okunamadi."}
                    </p>
                  </div>
                  <button
                    type="button"
                    onClick={handleSyncWikiGraph}
                    disabled={wikiGraphSyncing}
                    className="inline-flex shrink-0 items-center gap-2 rounded-xl border border-[#526d82]/16 bg-[#f7f9fa]/75 px-3 py-2 text-xs font-black text-[#344054] transition hover:bg-[#dcecf3]/70 disabled:opacity-50"
                  >
                    {wikiGraphSyncing ? <Loader2 className="h-4 w-4 animate-spin" /> : <Network className="h-4 w-4" />}
                    Senkronize et
                  </button>
                </div>

                <div className="mt-3 flex flex-wrap gap-2">
                  <span className="rounded-full border border-[#526d82]/14 bg-[#f7f9fa]/70 px-2.5 py-1 text-[10px] font-bold text-[#344054]">
                    {userSafeStatus(activeGraphPage?.pageType ?? activePage.pageType ?? "page")}
                  </span>
                  {(activeGraphPage?.conceptKey ?? activePage.conceptKey) && (
                    <span className="rounded-full border border-sky-500/18 bg-sky-500/8 px-2.5 py-1 text-[10px] font-bold text-[#52768a]">
                      {activeGraphPage?.conceptKey ?? activePage.conceptKey}
                    </span>
                  )}
                  <span className="rounded-full border border-[#8fb7a2]/22 bg-[#f2faf5]/80 px-2.5 py-1 text-[10px] font-bold text-[#47725d]">
                    {userSafeStatus(activeGraphPage?.sourceReadiness ?? activePage.sourceReadiness ?? "evidence_insufficient")}
                  </span>
                  <span className={`rounded-full border px-2.5 py-1 text-[10px] font-bold ${pageBadgeTone(activeGraphPage?.contentReadiness ?? activePage.contentReadiness)}`}>
                    {userSafeStatus(activeGraphPage?.contentReadiness ?? activePage.contentReadiness ?? "skeleton")}
                  </span>
                  <span className={`rounded-full border px-2.5 py-1 text-[10px] font-bold ${pageBadgeTone(activeGraphPage?.learningSystemBinding?.readiness ?? activePage.learningSystemBinding?.readiness)}`}>
                    {userSafeStatus(activeGraphPage?.learningSystemBinding?.readiness ?? activePage.learningSystemBinding?.readiness ?? "unbound")}
                  </span>
                  <span className="rounded-full border border-[#526d82]/14 bg-[#f7f9fa]/70 px-2.5 py-1 text-[10px] font-bold text-[#667085]">
                    {activeGraphPage?.blockCount ?? activePage.blockCount ?? activePage.blocks?.length ?? 0} blok
                  </span>
                </div>

                {(activeGraphPage?.safeSummary ?? activePage.safeSummary) && (
                  <p className="mt-3 rounded-xl border border-[#526d82]/12 bg-[#f7f9fa]/64 px-3 py-2 text-xs leading-5 text-[#667085]">
                    {activeGraphPage?.safeSummary ?? activePage.safeSummary}
                  </p>
                )}
                {(activeGraphPage?.curation ?? activePage.curation) && (
                  <div className="mt-3 rounded-xl border border-[#e8c46f]/18 bg-[#fff8ee]/70 px-3 py-2">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className={`rounded-full border px-2.5 py-1 text-[10px] font-bold ${pageBadgeTone((activeGraphPage?.curation ?? activePage.curation)?.curationStatus)}`}>
                        {userSafeStatus((activeGraphPage?.curation ?? activePage.curation)?.curationStatus)}
                      </span>
                      <span className="text-[10px] font-bold uppercase tracking-[0.14em] text-[#8a6a33]">
                        Wiki hygiene
                      </span>
                    </div>
                    <p className="mt-1 text-xs leading-5 text-[#8a6a33]">
                      {(activeGraphPage?.curation ?? activePage.curation)?.studentVisibleSummary}
                    </p>
                  </div>
                )}

                {relatedGraphPages.length > 0 && (
                  <div className="mt-3">
                    <div className="mb-2 text-[10px] font-black uppercase tracking-[0.18em] text-[#667085]">Bagli sayfalar</div>
                    <div className="flex flex-wrap gap-2">
                      {relatedGraphPages.map((page) => (
                        <button
                          key={page.id}
                          type="button"
                          onClick={() => selectGraphPage(page)}
                          className="rounded-xl border border-[#526d82]/14 bg-[#f7f9fa]/70 px-3 py-2 text-left text-xs font-bold text-[#344054] transition hover:border-[#9ec7d9]/50 hover:bg-white"
                        >
                          <span className="block max-w-[220px] truncate">{page.title}</span>
                          <span className="mt-0.5 block text-[10px] font-semibold text-[#667085]">
                            {userSafeStatus(page.pageType)} - {userSafeStatus(page.sourceReadiness)}
                          </span>
                        </button>
                      ))}
                    </div>
                  </div>
                )}

                {wikiGraphWarnings.length > 0 && (
                  <p className="mt-3 text-[11px] font-semibold leading-5 text-[#8a6a33]">
                    {wikiGraphWarnings.slice(0, 2).map(userSafeStatus).join(" - ")}
                  </p>
                )}
              </div>

              <div className="mb-8 rounded-2xl border border-[#526d82]/16 bg-[#f7f9fa]/72 p-4 shadow-sm">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <div className="flex items-center gap-2 text-[11px] font-black uppercase tracking-[0.18em] text-[#667085]">
                      <Sparkles className="h-4 w-4 text-[#47725d]" />
                      Wiki Çalışma Paketi
                    </div>
                    <h2 className="mt-1 text-lg font-black text-[#172033]">Bu konuyu çalış</h2>
                    <p className="mt-1 max-w-2xl text-sm leading-6 text-[#667085]">
                      Wiki özeti, kişisel pekiştirme, kaynak kanıtı ve kartları tek çalışma girişi olarak takip et.
                    </p>
                  </div>
                  {weakSkills.length > 0 && (
                    <div className="max-w-sm rounded-xl border border-sky-500/18 bg-sky-500/8 px-3 py-2 text-xs text-[#52768a]">
                      <div className="font-black">Bu konuda çalışma kuyruğu sinyali var.</div>
                      <div className="mt-1 leading-5">
                        Zayıf kavramı Wiki’den tekrar edebilir veya Tutor’a kısa telafi sorusu açabilirsin.
                      </div>
                    </div>
                  )}
                </div>

                {wikiStudyPackItems.length === 0 ? (
                  <div className="mt-4 rounded-xl border border-[#526d82]/12 bg-white/62 px-3 py-3 text-sm text-[#667085]">
                    Bu konu için çalışma paketi henüz oluşmadı. Wiki içeriği, kaynak veya quiz sinyali geldikçe burada adımlar belirecek.
                  </div>
                ) : (
                  <div className="mt-4 grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
                    {wikiStudyPackItems.map((item) => {
                      const toneClass = item.tone === "ready"
                        ? "border-emerald-500/20 bg-emerald-500/8 text-[#47725d]"
                        : item.tone === "watch"
                          ? "border-amber-500/24 bg-amber-500/8 text-[#8a6a33]"
                          : "border-[#526d82]/14 bg-white/62 text-[#667085]";

                      return (
                        <button
                          key={item.id}
                          type="button"
                          onClick={() => scrollToStudySection(item.section)}
                          className={`min-h-[92px] rounded-xl border px-3 py-3 text-left transition hover:-translate-y-0.5 hover:bg-white/78 focus:outline-none focus:ring-2 focus:ring-[#9ec7d9] ${toneClass}`}
                        >
                          <div className="text-sm font-black text-[#172033]">{item.label}</div>
                          <div className="mt-1 text-[11px] leading-5 opacity-85">{item.detail}</div>
                        </button>
                      );
                    })}
                  </div>
                )}
              </div>

              {/* NotebookLM-tarzı Briefing Document — wiki üst kısmında "okumadan önce göz at" özet */}
              <div id="wiki-study-practice" className="mb-8 scroll-mt-6 rounded-xl border border-[#8fb7a2]/24 bg-[#f2faf5]/62 overflow-hidden">
                <div className="px-5 py-3 flex flex-wrap items-center justify-between gap-3 border-b border-[#8fb7a2]/18">
                  <div className="flex items-center gap-2">
                    <HelpCircle className="w-4 h-4 text-[#47725d]" />
                    <span className="text-xs font-semibold uppercase tracking-widest text-[#47725d]">
                      Sayfa Pratigi
                    </span>
                  </div>
                  <span className={`rounded-full border px-2.5 py-1 text-[10px] font-bold ${pageBadgeTone(wikiPageQuestionSet?.status ?? wikiPagePractice?.status ?? "empty")}`}>
                    {wikiPageQuestionSet?.totalQuestions ?? wikiPagePractice?.totalQuestions ?? 0} soru
                  </span>
                </div>
                <div className="px-5 py-4">
                  {!wikiPageQuestionSet && !wikiPagePractice ? (
                    <p className="text-sm text-[#667085]">
                      Bu sayfaya bagli practice-ready soru henuz gorunmuyor. Plan/quiz sinyali geldikce burada cozumlenebilir sorular acilir.
                    </p>
                  ) : (
                    <div className="space-y-3">
                      <div className="flex flex-wrap items-center justify-between gap-3">
                        <div>
                          <div className="text-sm font-black text-[#172033]">
                            {wikiPagePractice?.status === "ready" ? "Mini pratik hazir" : "Bu sayfaya bagli soru havuzu hazir"}
                          </div>
                          <div className="mt-1 text-xs leading-5 text-[#667085]">
                            Concept: {wikiPageQuestionSet?.conceptKey ?? activePage.conceptKey ?? "page scope"} / KG-bound practice akisi
                          </div>
                        </div>
                        <button
                          type="button"
                          onClick={handleStartWikiPagePractice}
                          disabled={wikiPagePracticeLoading}
                          className="inline-flex items-center gap-2 rounded-xl border border-[#8fb7a2]/28 bg-white/78 px-3 py-2 text-xs font-black text-[#172033] transition hover:bg-white disabled:opacity-60"
                        >
                          {wikiPagePracticeLoading ? <Loader2 className="h-4 w-4 animate-spin" /> : <Zap className="h-4 w-4 text-[#47725d]" />}
                          Pratigi baslat
                        </button>
                      </div>

                      {wikiPagePractice?.questions?.length ? (
                        <div className="space-y-3">
                          {wikiPagePractice.questions.slice(0, 5).map((question, index) => (
                            <div key={question.questionItemId} className="rounded-xl border border-[#526d82]/12 bg-white/72 p-3">
                              <div className="mb-2 flex flex-wrap items-center gap-2">
                                <span className="rounded-full bg-[#f7f9fa] px-2 py-0.5 text-[10px] font-bold text-[#667085]">
                                  {index + 1}
                                </span>
                                <span className="rounded-full border border-sky-500/16 bg-sky-500/8 px-2 py-0.5 text-[10px] font-bold text-[#52768a]">
                                  {question.conceptKey ?? "concept"}
                                </span>
                                <span className={`rounded-full border px-2 py-0.5 text-[10px] font-bold ${pageBadgeTone(question.difficulty)}`}>
                                  {userSafeStatus(question.difficulty)}
                                </span>
                              </div>
                              <div className="text-sm font-bold leading-6 text-[#172033]">{question.stem}</div>
                              <div className="mt-3 grid gap-2">
                                {question.options.map((option) => (
                                  <label
                                    key={option.optionKey}
                                    className={`flex cursor-pointer items-start gap-2 rounded-xl border px-3 py-2 text-sm transition ${
                                      wikiPracticeAnswers[question.questionItemId] === option.optionKey
                                        ? "border-[#8fb7a2]/40 bg-[#f2faf5] text-[#172033]"
                                        : "border-[#526d82]/12 bg-[#f7f9fa]/62 text-[#344054] hover:bg-white"
                                    }`}
                                  >
                                    <input
                                      type="radio"
                                      className="mt-1"
                                      name={`wiki-practice-${question.questionItemId}`}
                                      checked={wikiPracticeAnswers[question.questionItemId] === option.optionKey}
                                      onChange={() => setWikiPracticeAnswers((prev) => ({ ...prev, [question.questionItemId]: option.optionKey }))}
                                    />
                                    <span>{option.text}</span>
                                  </label>
                                ))}
                              </div>
                            </div>
                          ))}
                          <div className="flex flex-wrap items-center justify-between gap-3">
                            <div className="text-xs font-semibold text-[#667085]">
                              Bos birakilanlar skip olarak kaydedilir; tutor bunu eksik kanit olarak gorur.
                            </div>
                            <button
                              type="button"
                              onClick={handleSubmitWikiPagePractice}
                              disabled={wikiPagePracticeLoading}
                              className="inline-flex items-center gap-2 rounded-xl bg-[#172033] px-4 py-2 text-xs font-black text-white transition hover:bg-[#344054] disabled:opacity-60"
                            >
                              {wikiPagePracticeLoading ? <Loader2 className="h-4 w-4 animate-spin" /> : <CheckCircle2 className="h-4 w-4" />}
                              Kaydet
                            </button>
                          </div>
                          {wikiPagePracticeResult && (
                            <div className="rounded-xl border border-[#526d82]/12 bg-white/74 px-3 py-3">
                              <div className="grid grid-cols-2 gap-2 text-xs sm:grid-cols-4">
                                <div><span className="block text-[#98a2b3]">Toplam</span><b>{wikiPagePracticeResult.totalQuestions}</b></div>
                                <div><span className="block text-[#98a2b3]">Dogru</span><b>{wikiPagePracticeResult.correctCount}</b></div>
                                <div><span className="block text-[#98a2b3]">Yanlis</span><b>{wikiPagePracticeResult.wrongCount}</b></div>
                                <div><span className="block text-[#98a2b3]">Bos</span><b>{wikiPagePracticeResult.blankCount}</b></div>
                              </div>
                            </div>
                          )}
                        </div>
                      ) : (
                        <div className="rounded-xl border border-[#526d82]/12 bg-white/64 px-3 py-3 text-sm text-[#667085]">
                          {wikiPageQuestionSet?.emptyState || "Soru havuzu bulundu ama pratik oturumu henuz baslatilmadi."}
                        </div>
                      )}
                    </div>
                  )}
                </div>
              </div>

              {(briefing || briefingLoading) && (
                <div id="wiki-study-briefing" className="mb-8 scroll-mt-6 rounded-xl border border-emerald-500/20 bg-emerald-500/5 overflow-hidden">
                  <div className="px-5 py-3 flex items-center gap-2 border-b border-emerald-500/15">
                    <Lightbulb className="w-4 h-4 text-[#47725d]" />
                    <span className="text-xs font-semibold uppercase tracking-widest text-[#47725d]">
                      Hızlı Bakış
                    </span>
                  </div>
                  <div className="px-5 py-4 space-y-4">
                    {briefingLoading && !briefing && (
                      <div className="flex items-center gap-2 text-sm text-[#667085]">
                        <Loader2 className="w-4 h-4 animate-spin" />
                        Özet hazırlanıyor...
                      </div>
                    )}
                    {briefing && (
                      <>
                        {briefing.tldr && (
                          <p className="text-sm text-[#172033] leading-relaxed">
                            <span className="font-semibold text-[#47725d]">TL;DR — </span>
                            {briefing.tldr}
                          </p>
                        )}
                        {briefing.keyTakeaways.length > 0 && (
                          <div>
                            <div className="flex items-center gap-1.5 mb-2 text-xs font-semibold text-[#667085] uppercase tracking-wide">
                              <ListChecks className="w-3.5 h-3.5" />
                              Anahtar Çıkarımlar
                            </div>
                            <ul className="space-y-1.5">
                              {briefing.keyTakeaways.map((kt, i) => (
                                <li key={i} className="flex gap-2 text-sm text-[#344054] leading-snug">
                                  <span className="text-[#47725d] font-mono text-xs mt-0.5">{i + 1}.</span>
                                  <span>{kt}</span>
                                </li>
                              ))}
                            </ul>
                          </div>
                        )}
                        {briefing.suggestedQuestions.length > 0 && (
                          <div className="pt-2 border-t border-emerald-500/10">
                            <div className="text-xs font-semibold text-[#667085] uppercase tracking-wide mb-2">
                              Öneri Sorular
                            </div>
                            <div className="flex flex-wrap gap-2">
                              {briefing.suggestedQuestions.map((q, i) => (
                                <button
                                  key={i}
                                  onClick={() => handleSuggestedQuestion(q)}
                                  className="text-xs px-3 py-1.5 rounded-full bg-[#f7f9fa]/68 hover:bg-[#dcecf3]/70 border border-[#526d82]/15 hover:border-[#8fb7a2]/40 text-[#344054] hover:text-[#47725d] transition"
                                >
                                  {q}
                                </button>
                              ))}
                            </div>
                          </div>
                        )}
                      </>
                    )}
                  </div>
                </div>
              )}

              {(weakSkills.length > 0 || recommendations.length > 0) && (
                <div id="wiki-study-reinforcement" className="mb-8 scroll-mt-6 rounded-xl border border-sky-500/20 bg-sky-500/5 overflow-hidden">
                  <div className="px-5 py-3 flex items-center justify-between gap-3 border-b border-sky-500/15">
                    <div className="flex items-center gap-2">
                      <Zap className="w-4 h-4 text-sky-400" />
                      <span className="text-xs font-semibold uppercase tracking-widest text-sky-300">
                        Kişisel Pekiştirme
                      </span>
                    </div>
                    {learningCache && (
                      <span className="rounded-full border border-sky-400/20 bg-sky-500/10 px-2.5 py-1 text-[10px] font-semibold uppercase tracking-wider text-sky-200">
                        {learningCache.hit ? "Redis hizli hafiza" : "SQL canli"} · {learningCache.source}
                      </span>
                    )}
                  </div>
                  <div className="px-5 py-4 grid gap-4 md:grid-cols-2">
                    <div>
                      <div className="text-xs font-semibold text-[#667085] uppercase tracking-wide mb-2">
                        Zorlandigin Yerler
                      </div>
                      {weakSkills.length === 0 ? (
                        <p className="text-sm text-[#667085]">Henüz konu bazlı zayıflık sinyali yok.</p>
                      ) : (
                        <div className="space-y-2">
                          {weakSkills.slice(0, 4).map((skill) => (
                            <button
                              key={skill.skillTag}
                              onClick={() => handleSuggestedQuestion(`${skill.topicPath || skill.skillTag} konusunu telafi dersi gibi anlat; once neden zorlandigimi acikla, sonra 1 ornek, 1 mini diagram ve 3 mikro soru ver.`)}
                              className="w-full text-left rounded-lg border border-[#526d82]/15 bg-[#f7f9fa]/66 px-3 py-2 hover:border-[#9ec7d9]/50 transition"
                            >
                              <div className="text-sm text-[#172033]">{skill.skillTag}</div>
                              <div className="text-[11px] text-[#667085]">
                                {skill.wrongCount}/{skill.totalCount} hata, dogruluk %{Math.round(skill.accuracy * 100)}
                              </div>
                              <div className="mt-2 inline-flex rounded-full bg-sky-500/10 px-2 py-0.5 text-[10px] font-bold text-[#52768a]">
                                Telafi dersi başlat
                              </div>
                            </button>
                          ))}
                        </div>
                      )}
                    </div>
                    <div>
                      <div className="text-xs font-semibold text-[#667085] uppercase tracking-wide mb-2">
                        Pekistirme Onerileri
                      </div>
                      {recommendations.length === 0 ? (
                        <p className="text-sm text-[#667085]">Quiz veya sinif sinyali geldikce burada ozel oneriler olusur.</p>
                      ) : (
                        <div className="space-y-2">
                          {recommendations.slice(0, 4).map((rec) => (
                            <button
                              key={rec.id}
                              onClick={() => handleSuggestedQuestion(rec.actionPrompt ?? rec.title)}
                              className="w-full text-left rounded-lg border border-[#526d82]/15 bg-[#f7f9fa]/66 px-3 py-2 hover:border-[#9ec7d9]/50 transition"
                            >
                              <div className="text-sm text-[#172033]">{rec.title}</div>
                              <div className="text-[11px] text-[#667085]">{rec.reason}</div>
                            </button>
                          ))}
                        </div>
                      )}
                    </div>
                  </div>
                </div>
              )}

              <div id="wiki-study-sources" className="mb-8 scroll-mt-6 grid grid-cols-1 xl:grid-cols-[1.15fr_0.85fr] gap-4">
                <div className="rounded-xl border border-[#526d82]/16 bg-[#f7f9fa]/58 overflow-hidden">
                  <div className="px-5 py-3 border-b border-[#526d82]/15 flex items-center justify-between gap-3">
                    <div className="flex items-center gap-2">
                      <FileText className="w-4 h-4 text-amber-400" />
                      <span className="text-xs font-semibold uppercase tracking-widest text-[#344054]">
                        {isOrkaLm ? "OrkaLM Kaynak Notebook'u" : "Notebook Kaynakları"}
                      </span>
                    </div>
                    <div>
                      <input
                        ref={fileInputRef}
                        type="file"
                        accept=".pdf,.txt,.md"
                        className="hidden"
                        onChange={(e) => {
                          handleUploadSource(e.target.files?.[0]);
                          e.target.value = "";
                        }}
                      />
                      <button
                        onClick={() => fileInputRef.current?.click()}
                        disabled={uploadingSource}
                        className={isOrkaLm ? "inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-amber-500/10 hover:bg-amber-500/20 border border-amber-500/20 text-amber-300 text-xs transition disabled:opacity-50" : "hidden"}
                      >
                        {uploadingSource ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Upload className="w-3.5 h-3.5" />}
                        {isOrkaLm ? "PDF / Kaynak Yükle" : "Kaynak Yükle"}
                      </button>
                    </div>
                  </div>
                  <div className="p-4 space-y-3">
                    {sourcesLoading && (
                      <div className="flex items-center gap-2 text-sm text-[#667085]">
                        <Loader2 className="w-4 h-4 animate-spin" />
                        Kaynaklar yükleniyor...
                      </div>
                    )}
                    <SourceCoverageCoach
                      title={sourceCoverageCoach.title}
                      detail={sourceCoverageCoach.detail}
                      tone={sourceCoverageCoach.tone}
                      actionLabel={sourceCoverageCoach.actionLabel}
                      onAction={() => {
                        if (isOrkaLm && (sources.length === 0 || sourceCoverageCoach.actionLabel === "Kaynak ekle")) {
                          fileInputRef.current?.click();
                        } else if (sourceCoverageCoach.actionLabel === "Kaynakları yenile") {
                          void refreshSources();
                        } else {
                          setShowCopilot(false);
                        }
                      }}
                    />
                    {(workspaceState.sourceReadiness || workspaceState.recentArtifacts.length > 0 || workspaceState.staleWarnings.length > 0) && (
                      <div className="rounded-2xl border border-[#526d82]/14 bg-white/64 p-3">
                        <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                          <div>
                            <div className="text-[10px] font-black uppercase tracking-[0.2em] text-[#52768a]">Learning workspace</div>
                            <p className="mt-1 text-[11px] text-[#667085]">
                              {isOrkaLm
                                ? "OrkaLM kaynak, citation ve source artifact durumunu kendi icinde izler."
                                : "Wiki sayfa, kavram ve Wiki artifact durumunu kendi icinde izler."}
                            </p>
                          </div>
                          {workspaceState.sourceReadiness && (
                            <span className="rounded-full border border-[#526d82]/14 bg-[#f7f9fa]/70 px-2.5 py-1 text-[10px] font-bold text-[#344054]">
                              {userSafeStatus(workspaceState.sourceReadiness)}
                            </span>
                          )}
                        </div>
                        {workspaceState.recentArtifacts.length > 0 && (
                          <div className="space-y-2">
                            {workspaceState.recentArtifacts.slice(0, 3).map((artifact) => (
                              <div key={artifact.id} className="rounded-xl border border-[#526d82]/12 bg-[#f7f9fa]/70 px-3 py-2">
                                <div className="flex items-center justify-between gap-2 text-xs">
                                  <span className="font-black text-[#172033]">{artifact.title || userSafeStatus(artifact.artifactType)}</span>
                                  <span className="rounded-full bg-white px-2 py-0.5 text-[10px] font-bold text-[#667085]">
                                    {userSafeStatus(artifact.sourceBasis)}
                                  </span>
                                </div>
                                {(artifact.safety?.warnings?.length > 0 || artifact.accessibility?.issues?.length > 0) && (
                                  <p className="mt-1 text-[11px] font-semibold text-[#8a6a33]">
                                    {[...(artifact.safety?.warnings ?? []), ...(artifact.accessibility?.issues ?? [])].slice(0, 1).map(userSafeStatus).join(" · ")}
                                  </p>
                                )}
                              </div>
                            ))}
                          </div>
                        )}
                        {workspaceState.staleWarnings.length > 0 && (
                          <p className="mt-2 text-[11px] font-semibold text-[#8a6a33]">
                            {workspaceState.staleWarnings.slice(0, 2).map(userSafeStatus).join(" · ")}
                          </p>
                        )}
                      </div>
                    )}
                    {isOrkaLm && sourceNotebook && (
                      <div className="rounded-2xl border border-amber-500/16 bg-[#fff8ee]/66 p-3">
                        <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                          <div>
                            <div className="text-[10px] font-black uppercase tracking-[0.2em] text-[#8a6a33]">OrkaLM source notebook</div>
                            <p className="mt-1 text-[11px] text-[#667085]">
                              {sourceNotebook.readySourceCount}/{sourceNotebook.sourceCount} hazir kaynak · {sourceNotebook.chunkCount} indeks parçası · %{Math.round((sourceNotebook.citationCoverage ?? 0) * 100)} citation destek
                            </p>
                          </div>
                          <span className="rounded-full border border-amber-500/18 bg-white/70 px-2.5 py-1 text-[10px] font-bold text-[#8a6a33]">
                            {userSafeStatus(sourceNotebook.evidenceStatus)}
                          </span>
                        </div>
                        {(activeSourceNotebook ?? sourceNotebook).warnings.length > 0 && (
                          <p className="mb-2 rounded-lg border border-amber-500/16 bg-white/54 px-2 py-1.5 text-[11px] font-semibold leading-5 text-[#8a6a33]">
                            {(activeSourceNotebook ?? sourceNotebook).warnings.slice(0, 2).map(userSafeStatus).join(" · ")}
                          </p>
                        )}
                        <div className="flex flex-wrap gap-2">
                          {(activeSourceNotebook ?? sourceNotebook).nextActions.slice(0, 5).map((action) => (
                            <span key={`${action.actionType}-${action.userSafeLabel}`} className="rounded-full border border-[#526d82]/12 bg-white/62 px-2.5 py-1 text-[10px] font-bold text-[#667085]">
                              {action.userSafeLabel}
                            </span>
                          ))}
                        </div>
                        {!isOrkaLm && (activeSourceNotebook ?? sourceNotebook).linkedWikiPages.length > 0 && (
                          <div className="mt-2 flex flex-wrap gap-1.5 text-[10px] font-semibold text-[#667085]">
                            {(activeSourceNotebook ?? sourceNotebook).linkedWikiPages.slice(0, 4).map((page) => (
                              <span key={page.id} className="rounded-full bg-white/60 px-2 py-0.5">
                                {page.title}
                              </span>
                            ))}
                          </div>
                        )}
                        {activeSource && (
                          <div className="mt-3 rounded-xl border border-[#526d82]/12 bg-white/58 p-2">
                            <div className="flex flex-wrap items-center justify-between gap-2">
                              <div>
                                <div className="text-[10px] font-black uppercase tracking-[0.16em] text-[#526d82]">Source-to-concept graph</div>
                                <p className="mt-1 text-[11px] text-[#667085]">
                                  {(sourceConceptLinks?.confirmedLinkCount ?? 0)} onayli bag / {(sourceConceptLinks?.suggestedLinkCount ?? 0)} onerilen bag / {sourceConceptGraph?.edges.length ?? 0} graph kenari
                                </p>
                              </div>
                              <button
                                type="button"
                                onClick={handleSyncSourceConceptLinks}
                                disabled={sourceConceptSyncing}
                                className="rounded-full border border-[#526d82]/15 bg-[#f7f9fa] px-2.5 py-1 text-[10px] font-black text-[#172033] transition hover:border-[#526d82]/30 disabled:opacity-60"
                              >
                                {sourceConceptSyncing ? "Hazirlaniyor..." : "Oneri grafigi"}
                              </button>
                            </div>
                            {sourceConceptLinks?.warnings?.length ? (
                              <p className="mt-2 rounded-lg border border-amber-500/16 bg-[#fff8ee] px-2 py-1 text-[11px] font-semibold text-[#8a6a33]">
                                {sourceConceptLinks.warnings.slice(0, 2).map(userSafeStatus).join(" / ")}
                              </p>
                            ) : null}
                            {sourceConceptLinks?.links?.length ? (
                              <div className="mt-2 flex flex-wrap gap-1.5">
                                {sourceConceptLinks.links.slice(0, 6).map((link) => (
                                  <button
                                    key={`${link.wikiPageId ?? link.conceptKey}-${link.linkType}`}
                                    type="button"
                                    onClick={() => {
                                      const page = wikiVaultPages.find((candidate) => candidate.id === link.wikiPageId);
                                      if (page) selectGraphPage(page);
                                    }}
                                    className="rounded-full border border-[#526d82]/12 bg-[#f7f4ec]/72 px-2 py-0.5 text-[10px] font-bold text-[#667085] hover:text-[#172033]"
                                  >
                                    {link.conceptTitle || link.conceptKey} / {userSafeStatus(link.confidence)}{link.isSuggestion ? " suggestion" : ""}
                                  </button>
                                ))}
                              </div>
                            ) : (
                              <p className="mt-2 text-[11px] font-semibold text-[#667085]">
                                Bu kaynak icin OrkaLM concept onerileri henuz hazirlanmadi.
                              </p>
                            )}
                          </div>
                        )}
                      </div>
                    )}
                    {!sourcesLoading && sources.length === 0 && (
                      <p className="text-sm text-[#667085]">
                        {isOrkaLm
                          ? "PDF, TXT veya MD yukle. OrkaLM bu kaynaklardan kanit, ozet, terim, zihin haritasi, UML, quiz, flashcard ve slayt taslagi uretir."
                          : "Wiki burada kaynak yukletmez; bu yuzeyde Wiki sayfasi, kavram, pratik, graph, template ve export preview ozellikleri kullanilir."}
                      </p>
                    )}
                    {sources.length > 0 && (
                      <div className="rounded-2xl border border-[#526d82]/14 bg-[#f7f4ec]/64 p-3">
                        <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
                          <div>
                            <div className="text-[10px] font-black uppercase tracking-[0.2em] text-[#8a6a33]">Kaynak grafı</div>
                            <p className="mt-1 text-[11px] text-[#667085]">
                              {sources.length} kaynak · {sourceGraph.totalPages} sayfa · {sourceGraph.totalChunks} chunk · yoğunluk {sourceGraph.density}/sayfa
                            </p>
                          </div>
                          <span className="rounded-full border border-[#8a6a33]/16 bg-[#f7f9fa]/62 px-2.5 py-1 text-[10px] font-bold text-[#8a6a33]">
                            {sourceGraph.readySources}/{sources.length} hazır
                          </span>
                        </div>
                        <div className="flex flex-wrap gap-2">
                          {sources.map((source, idx) => (
                            <button
                              key={source.id}
                              onClick={() => handleSelectSource(source)}
                              className={`group relative text-left px-3 py-2 rounded-xl border transition max-w-full ${
                                activeSource?.id === source.id
                                  ? "border-amber-500/40 bg-amber-500/10 text-amber-200"
                                  : "border-[#526d82]/15 bg-[#f7f9fa]/58 text-[#667085] hover:text-[#172033] hover:border-[#526d82]/18"
                              }`}
                            >
                              <span className="absolute -left-1 -top-1 grid h-5 w-5 place-items-center rounded-full border border-[#526d82]/12 bg-[#f7f9fa] text-[10px] font-black text-[#8a6a33]">
                                {idx + 1}
                              </span>
                              <div className="text-xs font-medium truncate max-w-[220px] pl-2">{source.fileName}</div>
                              <div className="mt-1 flex flex-wrap gap-1 pl-2 text-[10px] text-[#98a2b3]">
                                <span>{source.pageCount} sayfa</span>
                                <span>·</span>
                                <span>{source.chunkCount} parça</span>
                                <span>·</span>
                                <span>{source.status}</span>
                              </div>
                              <div className="mt-2 h-1 overflow-hidden rounded-full bg-[#dcecf3]/70">
                                <div
                                  className="h-full rounded-full bg-amber-300 transition-all"
                                  style={{ width: `${Math.min(100, Math.max(8, sourceGraph.totalChunks ? (source.chunkCount / sourceGraph.totalChunks) * 100 : 8))}%` }}
                                />
                              </div>
                            </button>
                          ))}
                        </div>
                        {isOrkaLm && sources.length > 1 && (
                          <div className="mt-3 rounded-xl border border-[#526d82]/12 bg-white/55 p-2">
                            <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                              <div>
                                <div className="text-[10px] font-black uppercase tracking-[0.16em] text-[#526d82]">Multi-source compare</div>
                                <p className="mt-1 text-[11px] text-[#667085]">
                                  {selectedCompareSourceIds.length} kaynak secildi. Compare coverage/concept overlap gosterir; semantic agreement iddiasi uretmez.
                                </p>
                              </div>
                              <button
                                type="button"
                                onClick={handleCompareSources}
                                disabled={sourceComparing || selectedCompareSourceIds.length < 2}
                                className="inline-flex items-center gap-1.5 rounded-lg border border-[#526d82]/15 bg-[#f7f9fa]/80 px-3 py-2 text-[11px] font-black text-[#172033] transition hover:border-amber-500/30 disabled:opacity-45"
                              >
                                {sourceComparing ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Network className="h-3.5 w-3.5" />}
                                Compare selected
                              </button>
                            </div>
                            <div className="flex flex-wrap gap-1.5">
                              {sources.map((source) => {
                                const selected = selectedCompareSourceIds.includes(source.id);
                                return (
                                  <button
                                    key={`compare-${source.id}`}
                                    type="button"
                                    onClick={() => toggleCompareSource(source.id)}
                                    className={`rounded-full border px-2.5 py-1 text-[10px] font-bold transition ${
                                      selected
                                        ? "border-amber-500/35 bg-amber-500/12 text-[#8a6a33]"
                                        : "border-[#526d82]/12 bg-[#f7f9fa]/68 text-[#667085] hover:text-[#172033]"
                                    }`}
                                  >
                                    {selected ? "Selected" : "Add"} / {source.fileName}
                                  </button>
                                );
                              })}
                            </div>
                            {sourceCompare && (
                              <div className="mt-3 space-y-2">
                                <div className="grid gap-2 sm:grid-cols-3">
                                  <div className="rounded-lg border border-[#526d82]/10 bg-[#f7f9fa]/70 px-2.5 py-2 text-[11px] text-[#667085]">
                                    <span className="font-bold text-[#344054]">Coverage:</span> %{Math.round((sourceCompare.citationCoverage.coverageRatio ?? 0) * 100)} / {userSafeStatus(sourceCompare.citationCoverage.coverageStatus)}
                                  </div>
                                  <div className="rounded-lg border border-[#526d82]/10 bg-[#f7f9fa]/70 px-2.5 py-2 text-[11px] text-[#667085]">
                                    <span className="font-bold text-[#344054]">Shared concepts:</span> {sourceCompare.sharedConcepts.length}
                                  </div>
                                  <div className="rounded-lg border border-[#526d82]/10 bg-[#f7f9fa]/70 px-2.5 py-2 text-[11px] text-[#667085]">
                                    <span className="font-bold text-[#344054]">Review needed:</span> {sourceCompare.citationCoverage.needsReviewCount}
                                  </div>
                                </div>
                                {sourceCompare.warnings.length > 0 && (
                                  <p className="rounded-lg border border-amber-500/16 bg-[#fff8ee]/72 px-2.5 py-1.5 text-[11px] font-semibold text-[#8a6a33]">
                                    {sourceCompare.warnings.slice(0, 4).map(userSafeStatus).join(" / ")}
                                  </p>
                                )}
                                <div className="grid gap-2 sm:grid-cols-2">
                                  {sourceCompare.sourceSummaries.slice(0, 6).map((summary) => (
                                    <div key={summary.sourceId} className="rounded-xl border border-[#526d82]/12 bg-white/62 p-2">
                                      <div className="flex items-start justify-between gap-2">
                                        <div className="min-w-0">
                                          <div className="truncate text-xs font-bold text-[#172033]">{summary.sourceTitle}</div>
                                          <p className="mt-1 text-[10px] text-[#667085]">
                                            {userSafeStatus(summary.sourceReadiness)} / {userSafeStatus(summary.evidenceStatus)}
                                          </p>
                                        </div>
                                        <span className="rounded-full bg-[#f7f9fa] px-2 py-0.5 text-[10px] font-bold text-[#667085]">
                                          %{Math.round((summary.citationCoverage ?? 0) * 100)}
                                        </span>
                                      </div>
                                      <div className="mt-2 flex flex-wrap gap-1 text-[10px] font-semibold text-[#667085]">
                                        <span>{summary.supportedCitationCount} supported</span>
                                        <span>{summary.needsReviewCitationCount} review</span>
                                        <span>{summary.linkedConceptCount} concepts</span>
                                      </div>
                                    </div>
                                  ))}
                                </div>
                                {sourceCompare.sharedConcepts.length > 0 && (
                                  <div className="rounded-xl border border-[#526d82]/12 bg-[#f7f9fa]/58 p-2">
                                    <div className="mb-1 text-[10px] font-black uppercase tracking-[0.16em] text-[#667085]">Shared linked concepts</div>
                                    <div className="flex flex-wrap gap-1.5">
                                      {sourceCompare.sharedConcepts.slice(0, 8).map((concept) => (
                                        <button
                                          key={`${concept.wikiPageId ?? concept.conceptKey}-shared`}
                                          type="button"
                                          onClick={() => {
                                            const page = concept.wikiPageId ? wikiGraphPageById.get(concept.wikiPageId) : null;
                                            if (page) selectGraphPage(page);
                                          }}
                                          className="rounded-full border border-[#526d82]/12 bg-white/70 px-2 py-0.5 text-[10px] font-bold text-[#344054] transition hover:border-amber-500/30"
                                        >
                                          {concept.conceptTitle || concept.conceptKey} / {userSafeStatus(concept.linkConfidence)}
                                        </button>
                                      ))}
                                    </div>
                                  </div>
                                )}
                                {sourceCompare.citationReviewItems.length > 0 && (
                                  <div className="rounded-xl border border-[#526d82]/12 bg-white/58 p-2">
                                    <div className="mb-1 text-[10px] font-black uppercase tracking-[0.16em] text-[#667085]">Citation review</div>
                                    <div className="space-y-1.5">
                                      {sourceCompare.citationReviewItems.slice(0, 5).map((item) => (
                                        <div key={item.id} className="flex flex-wrap items-center justify-between gap-2 rounded-lg bg-[#f7f9fa]/70 px-2 py-1.5 text-[11px] text-[#667085]">
                                          <span className="font-semibold text-[#344054]">{item.sourceTitle} / {item.citationId}</span>
                                          <span>{userSafeStatus(item.citationStatus)}{item.pageNumber ? ` / page ${item.pageNumber}` : ""}</span>
                                        </div>
                                      ))}
                                    </div>
                                  </div>
                                )}
                              </div>
                            )}
                          </div>
                        )}
                      </div>
                    )}
                    {sourceQuality && (
                      <div className="rounded-xl border border-[#526d82]/14 bg-[#f7f9fa]/70 px-3 py-2">
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <div>
                            <div className="text-[10px] font-black uppercase tracking-[0.2em] text-[#667085]">Kaynak Sağlığı</div>
                            <p className="mt-1 text-[11px] text-[#344054]">
                              Retrieval {sourceQualityLabel(sourceQuality.retrievalHealthStatus)} · citation {sourceQualityLabel(sourceQuality.citationCoverageStatus)}
                            </p>
                          </div>
                          <span className="rounded-full border border-[#526d82]/14 bg-white/70 px-2.5 py-1 text-[10px] font-bold text-[#344054]">
                            %{Math.round((sourceQuality.citationCoverage ?? 0) * 100)} destek
                          </span>
                        </div>
                        <div className="mt-2 grid gap-2 sm:grid-cols-3">
                          <div className="rounded-lg bg-white/56 px-2.5 py-1.5 text-[11px] text-[#667085]">
                            {sourceQuality.retrievalRunCount} arama izi
                          </div>
                          <div className="rounded-lg bg-white/56 px-2.5 py-1.5 text-[11px] text-[#667085]">
                            {sourceQuality.emptyRunCount} boş sonuç
                          </div>
                          <div className="rounded-lg bg-white/56 px-2.5 py-1.5 text-[11px] text-[#667085]">
                            {sourceQuality.unsupportedCitationCount + sourceQuality.citationMissingCount} citation sorunu
                          </div>
                        </div>
                      </div>
                    )}
                    {isOrkaLm && citationReview && !sourceCompare && (
                      <div className="rounded-xl border border-[#526d82]/14 bg-[#f7f9fa]/70 px-3 py-2">
                        <div className="flex flex-wrap items-center justify-between gap-2">
                          <div>
                            <div className="text-[10px] font-black uppercase tracking-[0.2em] text-[#667085]">Citation review</div>
                            <p className="mt-1 text-[11px] text-[#344054]">
                              {citationReview.coverage.totalCitationChecks} citation check / {userSafeStatus(citationReview.coverage.coverageStatus)}
                            </p>
                          </div>
                          <span className="rounded-full border border-[#526d82]/14 bg-white/70 px-2.5 py-1 text-[10px] font-bold text-[#344054]">
                            {citationReview.coverage.needsReviewCount} review
                          </span>
                        </div>
                        {citationReview.warnings.length > 0 && (
                          <p className="mt-2 rounded-lg border border-amber-500/16 bg-[#fff8ee]/70 px-2 py-1.5 text-[11px] font-semibold text-[#8a6a33]">
                            {citationReview.warnings.slice(0, 3).map(userSafeStatus).join(" / ")}
                          </p>
                        )}
                      </div>
                    )}
                    {isOrkaLm && activeSource && (
                      <div className="pt-3 border-t border-[#526d82]/15 space-y-3">
                        <div className="flex items-center justify-between gap-2 rounded-xl border border-[#526d82]/12 bg-[#f7f9fa]/58 px-3 py-2">
                          <div className="min-w-0">
                            <div className="truncate text-xs font-semibold text-[#172033]">{activeSource.fileName}</div>
                            <div className="text-[10px] text-[#667085]">{activeSource.status}</div>
                          </div>
                          <button
                            type="button"
                            onClick={() => handleDeleteSource(activeSource)}
                            className="inline-flex items-center gap-1.5 rounded-lg border border-red-500/12 bg-red-500/5 px-2.5 py-1.5 text-[11px] font-semibold text-red-500/75 transition hover:border-red-500/30 hover:bg-red-500/10 hover:text-red-500"
                            title="Kaynağı sil"
                            aria-label={`${activeSource.fileName} kaynağını sil`}
                          >
                            <Trash2 className="h-3.5 w-3.5" />
                            Sil
                          </button>
                        </div>
                        <div className="flex flex-wrap gap-2">
                          <input
                            value={sourceQuestion}
                            onChange={(e) => setSourceQuestion(e.target.value)}
                            onKeyDown={(e) => e.key === "Enter" && handleAskSource()}
                            placeholder={`${activeSource.fileName} hakkında soru sor...`}
                            className="min-w-[180px] flex-1 bg-[#f7f9fa]/62 border border-[#526d82]/15 focus:border-[#e8c46f]/55 rounded-lg px-3 py-2 text-sm text-[#172033] placeholder-zinc-600 outline-none"
                          />
                          <button
                            onClick={handleAskSource}
                            disabled={sourceAsking || !sourceQuestion.trim()}
                            className="inline-flex items-center gap-1.5 rounded-lg bg-amber-500/10 px-3 py-2 text-xs font-bold text-[#8a6a33] hover:bg-amber-500/20 transition disabled:opacity-40"
                          >
                            {sourceAsking ? <Loader2 className="w-4 h-4 animate-spin" /> : <Send className="w-4 h-4" />}
                            Ask selected source
                          </button>
                          <button
                            onClick={handleAskSourceCollection}
                            disabled={sourceAsking || !sourceQuestion.trim()}
                            className="inline-flex items-center gap-1.5 rounded-lg border border-[#526d82]/15 bg-[#f7f9fa]/70 px-3 py-2 text-xs font-bold text-[#344054] transition hover:border-amber-500/25 hover:text-[#8a6a33] disabled:opacity-40"
                          >
                            Ask source collection
                          </button>
                          <button
                            onClick={handleCreateQuestionThread}
                            disabled={threadLoading || !sourceQuestion.trim()}
                            className="inline-flex items-center gap-1.5 rounded-lg border border-[#526d82]/15 bg-white/68 px-3 py-2 text-xs font-bold text-[#344054] transition hover:border-amber-500/25 hover:text-[#8a6a33] disabled:opacity-40"
                          >
                            Save source Q&A thread
                          </button>
                        </div>
                        {isOrkaLm && (
                          <div className="space-y-3">
                          {sourceStudySummary && (
                            <div className="rounded-xl border border-[#526d82]/14 bg-[#f7f9fa]/70 p-3">
                              <div className="flex flex-wrap items-start justify-between gap-2">
                                <div>
                                  <div className="text-[10px] font-black uppercase tracking-[0.2em] text-[#667085]">Source study status</div>
                                  <p className="mt-1 text-[11px] text-[#344054]">
                                    {userSafeStatus(sourceStudySummary.studyStatus)} / {userSafeStatus(sourceStudySummary.sourceReadiness)} / {userSafeStatus(sourceStudySummary.evidenceStatus)}
                                  </p>
                                </div>
                                <span className="rounded-full border border-[#526d82]/14 bg-white/70 px-2.5 py-1 text-[10px] font-bold text-[#344054]">
                                  Next: {userSafeStatus(sourceStudySummary.recommendedNextAction)}
                                </span>
                              </div>
                              <div className="mt-2 grid gap-2 sm:grid-cols-4">
                                <div className="rounded-lg bg-white/62 px-2.5 py-1.5 text-[11px] text-[#667085]">
                                  <span className="font-bold text-[#344054]">{sourceStudySummary.threadCount}</span> threads / {sourceStudySummary.turnCount} turns
                                </div>
                                <div className="rounded-lg bg-white/62 px-2.5 py-1.5 text-[11px] text-[#667085]">
                                  <span className="font-bold text-[#344054]">{sourceStudySummary.needsReviewCount}</span> review
                                </div>
                                <div className="rounded-lg bg-white/62 px-2.5 py-1.5 text-[11px] text-[#667085]">
                                  <span className="font-bold text-[#344054]">{sourceStudySummary.citationWarningCount}</span> citation warnings
                                </div>
                                <div className="rounded-lg bg-white/62 px-2.5 py-1.5 text-[11px] text-[#667085]">
                                  <span className="font-bold text-[#344054]">{sourceStudySummary.relatedConceptCount}</span> linked concepts
                                </div>
                              </div>
                              {sourceStudySummary.nextActions.length > 0 && (
                                <div className="mt-2 flex flex-wrap gap-1.5">
                                  {sourceStudySummary.nextActions.slice(0, 6).map((action) => (
                                    <span key={`study-action-${action}`} className="rounded-full border border-[#526d82]/12 bg-white/70 px-2 py-0.5 text-[10px] font-bold text-[#667085]">
                                      {userSafeStatus(action)}
                                    </span>
                                  ))}
                                </div>
                              )}
                              {sourceStudySummary.warnings.length > 0 && (
                                <p className="mt-2 rounded-lg border border-amber-500/16 bg-[#fff8ee]/70 px-2 py-1.5 text-[11px] font-semibold text-[#8a6a33]">
                                  {sourceStudySummary.warnings.slice(0, 4).map(userSafeStatus).join(" / ")}
                                </p>
                              )}
                            </div>
                          )}
                          <div className="rounded-xl border border-[#526d82]/14 bg-white/58 p-3">
                            <div className="flex flex-wrap items-center justify-between gap-2">
                              <div>
                                <div className="text-[10px] font-black uppercase tracking-[0.2em] text-[#667085]">Source Q&A memory</div>
                                <p className="mt-1 text-[11px] text-[#344054]">
                                  {threadLoading ? "Loading reviewed threads..." : `${sourceQuestionThreads.length} thread / ${activeQuestionThread?.turns.length ?? 0} active turns`}
                                </p>
                              </div>
                              <div className="flex flex-wrap gap-1.5">
                                <button
                                  type="button"
                                  onClick={() => handleReviewThread("needs_review")}
                                  disabled={!activeQuestionThread || threadLoading}
                                  className="rounded-full border border-amber-500/20 bg-[#fff8ee]/70 px-2.5 py-1 text-[10px] font-bold text-[#8a6a33] disabled:opacity-40"
                                >
                                  Mark needs review
                                </button>
                                {!isOrkaLm && (
                                  <button
                                    type="button"
                                    onClick={handleWriteThreadTrace}
                                    disabled={!activeQuestionThread || threadLoading}
                                    className="rounded-full border border-[#526d82]/14 bg-[#f7f9fa]/70 px-2.5 py-1 text-[10px] font-bold text-[#344054] disabled:opacity-40"
                                  >
                                    Write to Wiki
                                  </button>
                                )}
                              </div>
                            </div>
                            {sourceQuestionThreads.length > 0 && (
                              <div className="mt-2 flex gap-1.5 overflow-x-auto pb-1">
                                {sourceQuestionThreads.slice(0, 8).map((thread) => (
                                  <button
                                    key={thread.threadId}
                                    type="button"
                                    onClick={() => setActiveQuestionThread(thread)}
                                    className={`max-w-[180px] shrink-0 rounded-lg border px-2.5 py-1.5 text-left text-[10px] transition ${
                                      activeQuestionThread?.threadId === thread.threadId
                                        ? "border-amber-500/35 bg-[#fff8ee]/80 text-[#8a6a33]"
                                        : "border-[#526d82]/12 bg-[#f7f9fa]/70 text-[#667085] hover:border-amber-500/25"
                                    }`}
                                  >
                                    <div className="truncate font-bold">{thread.title}</div>
                                    <div className="truncate">{userSafeStatus(thread.citationReviewStatus)} / {thread.turns.length} turns</div>
                                  </button>
                                ))}
                              </div>
                            )}
                            {activeQuestionThread ? (
                              <div className="mt-3 space-y-2">
                                <div className="flex flex-wrap gap-1.5 text-[10px] font-bold text-[#667085]">
                                  <span className="rounded-full bg-[#f7f9fa]/80 px-2 py-0.5">{userSafeStatus(activeQuestionThread.status)}</span>
                                  <span className="rounded-full bg-[#f7f9fa]/80 px-2 py-0.5">{userSafeStatus(activeQuestionThread.sourceBasis)}</span>
                                  <span className="rounded-full bg-[#f7f9fa]/80 px-2 py-0.5">{userSafeStatus(activeQuestionThread.citationReviewStatus)}</span>
                                </div>
                                {activeQuestionThread.warnings.length > 0 && (
                                  <p className="rounded-lg border border-amber-500/16 bg-[#fff8ee]/70 px-2 py-1.5 text-[11px] font-semibold text-[#8a6a33]">
                                    {activeQuestionThread.warnings.slice(0, 4).map(userSafeStatus).join(" / ")}
                                  </p>
                                )}
                                <div className="max-h-56 space-y-2 overflow-y-auto pr-1">
                                  {activeQuestionThread.turns.slice(-4).map((turn) => (
                                    <div key={turn.turnId} className="rounded-lg border border-[#526d82]/10 bg-[#f7f9fa]/70 p-2 text-[11px] text-[#344054]">
                                      <div className="font-bold text-[#172033]">{turn.question}</div>
                                      <p className="mt-1 leading-relaxed">{turn.safeAnswerSummary}</p>
                                      <div className="mt-1 flex flex-wrap gap-1.5 text-[10px] font-semibold text-[#667085]">
                                        <span>{userSafeStatus(turn.sourceBasis)}</span>
                                        <span>{userSafeStatus(turn.reviewStatus)}</span>
                                        <span>{turn.citations.length} citations</span>
                                      </div>
                                    </div>
                                  ))}
                                </div>
                                {activeQuestionThread.linkedConcepts.length > 0 && (
                                  <div className="flex flex-wrap gap-1.5">
                                    {activeQuestionThread.linkedConcepts.slice(0, 6).map((link) => (
                                      <button
                                        key={`${link.conceptKey}-${link.wikiPageId ?? "thread"}`}
                                        type="button"
                                        onClick={() => {
                                          const page = link.wikiPageId ? wikiGraphPageById.get(link.wikiPageId) : null;
                                          if (page) selectGraphPage(page);
                                        }}
                                        className="rounded-full border border-[#526d82]/12 bg-white/70 px-2 py-0.5 text-[10px] font-bold text-[#344054]"
                                      >
                                        {link.conceptTitle || link.conceptKey}
                                      </button>
                                    ))}
                                  </div>
                                )}
                                <div className="flex flex-wrap gap-2">
                                  <input
                                    value={threadFollowUp}
                                    onChange={(e) => setThreadFollowUp(e.target.value)}
                                    onKeyDown={(e) => e.key === "Enter" && handleAskThreadFollowUp()}
                                    placeholder="Continue this source Q&A thread..."
                                    className="min-w-[180px] flex-1 rounded-lg border border-[#526d82]/15 bg-white/70 px-3 py-2 text-sm text-[#172033] placeholder-zinc-500 outline-none focus:border-[#e8c46f]/55"
                                  />
                                  <button
                                    type="button"
                                    onClick={handleAskThreadFollowUp}
                                    disabled={threadLoading || !threadFollowUp.trim()}
                                    className="inline-flex items-center gap-1.5 rounded-lg bg-amber-500/10 px-3 py-2 text-xs font-bold text-[#8a6a33] transition hover:bg-amber-500/20 disabled:opacity-40"
                                  >
                                    {threadLoading ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />}
                                    Ask follow-up
                                  </button>
                                </div>
                              </div>
                            ) : (
                              <p className="mt-2 rounded-lg border border-dashed border-[#526d82]/18 bg-[#f7f9fa]/58 px-3 py-2 text-[11px] text-[#667085]">
                                Ask a source question, then save it as a thread to revisit citations, review state, and safe follow-ups later.
                              </p>
                            )}
                          </div>
                          </div>
                        )}
                        {sourceAnswer && (
                          <div className="rounded-lg border border-[#526d82]/15 bg-[#f7f9fa]/62 px-4 py-3 space-y-3">
                            <RichMarkdown
                              content={sourceAnswer}
                              onSourceClick={handleSourceClick}
                              onCitationClick={handleCitationClick}
                              className="prose prose-invert prose-sm max-w-none prose-p:my-1.5 prose-p:text-[#344054]"
                            />
                            {sourceQuestionResponse && (
                              <div className="flex flex-wrap items-center gap-2 rounded-xl border border-[#526d82]/12 bg-white/60 px-3 py-2 text-[11px] text-[#667085]">
                                <span className="font-bold text-[#344054]">Kaynak durumu:</span>
                                <span>{userSafeStatus(sourceQuestionResponse.sourceBasis)}</span>
                                <span>{userSafeStatus(sourceQuestionResponse.evidenceStatus)}</span>
                                <span>{userSafeStatus(sourceQuestionResponse.sourceReadiness)}</span>
                                {sourceQuestionResponse.traceBlockId ? <span>Wiki trace yazildi</span> : null}
                                {sourceQuestionResponse.safety?.status && sourceQuestionResponse.safety.status !== "safe" ? (
                                  <span>{userSafeStatus(sourceQuestionResponse.safety.status)}</span>
                                ) : null}
                                {sourceQuestionResponse.warnings.length > 0 ? (
                                  <span>{sourceQuestionResponse.warnings.slice(0, 3).map(userSafeStatus).join(", ")}</span>
                                ) : null}
                              </div>
                            )}
                            {sourceCitations.length > 0 && (
                              <div className="rounded-xl border border-amber-500/18 bg-[#fff8ee]/70 p-3">
                                <div className="mb-2 flex items-center justify-between gap-2">
                                  <span className="text-[10px] font-black uppercase tracking-[0.2em] text-[#8a6a33]">
                                    Citation chips
                                  </span>
                                  <span className="text-[10px] font-semibold text-[#667085]">{sourceCitations.length} citation</span>
                                </div>
                                <div className="flex flex-wrap gap-2">
                                  {sourceCitations.slice(0, 8).map((citation, idx) => (
                                    <button
                                      key={`${citation.id}-${idx}`}
                                      type="button"
                                      onClick={() => openSourcePage(activeSource.id, citation.pageNumber, {
                                        focusChunkId: citation.id,
                                        action: "source-citation-chip",
                                      })}
                                      className={`rounded-full border px-2.5 py-1 text-[11px] font-semibold transition ${
                                        focusedChunkId === citation.id
                                          ? "border-amber-500/40 bg-amber-500/15 text-[#8a6a33]"
                                          : "border-[#526d82]/14 bg-[#f7f9fa]/70 text-[#667085] hover:border-amber-500/30 hover:text-[#8a6a33]"
                                      }`}
                                      title={`Page ${citation.pageNumber}, citation ${citation.chunkIndex + 1}`}
                                    >
                                      {citation.label || `[doc:p${citation.pageNumber}]`} / {userSafeStatus(citation.supportStatus)}
                                    </button>
                                  ))}
                                </div>
                              </div>
                            )}
                            {sourceQuestionResponse?.relatedConcepts?.length ? (
                              <div className="rounded-xl border border-[#526d82]/12 bg-white/55 p-3">
                                <div className="mb-2 text-[10px] font-black uppercase tracking-[0.2em] text-[#667085]">Related concept pages</div>
                                <div className="flex flex-wrap gap-2">
                                  {sourceQuestionResponse.relatedConcepts.slice(0, 8).map((link) => (
                                    <button
                                      key={`${link.wikiPageId ?? link.conceptKey}-${link.linkType}`}
                                      type="button"
                                      onClick={() => {
                                        const page = link.wikiPageId ? wikiGraphPageById.get(link.wikiPageId) : null;
                                        if (page) selectGraphPage(page);
                                      }}
                                      className="rounded-full border border-[#526d82]/14 bg-[#f7f9fa]/70 px-2.5 py-1 text-[11px] font-semibold text-[#344054] transition hover:border-amber-500/30 hover:text-[#8a6a33]"
                                    >
                                      {link.conceptTitle || link.conceptKey} / {userSafeStatus(link.confidence)}
                                    </button>
                                  ))}
                                </div>
                              </div>
                            ) : null}
                          </div>
                        )}
                        {(sourcePage || sourcePageLoading) && (
                          <div ref={sourceViewerRef} className="rounded-2xl border border-amber-500/20 bg-[#fff8ee]/62 px-4 py-3 shadow-sm">
                            <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
                              <div>
                                <span className="text-[10px] font-black uppercase tracking-[0.2em] text-[#8a6a33]">
                                  Kaynak Kanıt Paneli
                                </span>
                                <div className="mt-1 text-xs font-semibold text-[#172033]">
                                  {sourcePage ? `${sourcePage.title} · Sayfa ${sourcePage.pageNumber}` : "Sayfa yükleniyor..."}
                                </div>
                              </div>
                              <div className="flex items-center gap-2">
                                <button
                                  type="button"
                                  onClick={() => handleSourcePageNav(-1)}
                                  disabled={!sourcePage || !activeSource || sourcePage.pageNumber <= 1 || sourcePageLoading}
                                  className="rounded-full border border-[#526d82]/14 bg-[#f7f9fa]/68 px-2.5 py-1 text-[11px] font-bold text-[#667085] transition hover:text-[#172033] disabled:opacity-40"
                                >
                                  Önceki
                                </button>
                                <button
                                  type="button"
                                  onClick={() => handleSourcePageNav(1)}
                                  disabled={!sourcePage || !activeSource || sourcePage.pageNumber >= activeSource.pageCount || sourcePageLoading}
                                  className="rounded-full border border-[#526d82]/14 bg-[#f7f9fa]/68 px-2.5 py-1 text-[11px] font-bold text-[#667085] transition hover:text-[#172033] disabled:opacity-40"
                                >
                                  Sonraki
                                </button>
                                <button
                                  onClick={() => {
                                    setSourcePage(null);
                                    setFocusedChunkId(null);
                                  }}
                                  className="text-[#667085] hover:text-[#344054]"
                                >
                                  <X className="w-3.5 h-3.5" />
                                </button>
                              </div>
                            </div>

                            {sourcePageLoading && (
                              <div className="mb-3 flex items-center gap-2 rounded-xl border border-[#526d82]/12 bg-[#f7f9fa]/62 px-3 py-2 text-xs text-[#667085]">
                                <Loader2 className="h-3.5 w-3.5 animate-spin" />
                                Kaynak sayfası hazırlanıyor...
                              </div>
                            )}

                            {sourcePage && (
                              <>
                              <div data-testid="source-evidence-trust-strip" className="mb-3 grid gap-2 sm:grid-cols-3">
                                <div className="rounded-xl border border-emerald-500/18 bg-emerald-500/8 px-3 py-2">
                                  <div className="text-[10px] font-black uppercase tracking-[0.18em] text-[#47725d]">Kaynak güveni</div>
                                  <div className="mt-1 text-[11px] font-semibold text-[#344054]">Metin chunk + sayfa eşleşti</div>
                                </div>
                                <div className="rounded-xl border border-amber-500/18 bg-amber-500/8 px-3 py-2">
                                  <div className="text-[10px] font-black uppercase tracking-[0.18em] text-[#8a6a33]">Citation trail</div>
                                  <div className="mt-1 text-[11px] font-semibold text-[#344054]">
                                    {focusedChunkId ? "Odak chunk kilitlendi" : "İlk güçlü kanıt vurgulanıyor"}
                                  </div>
                                </div>
                                <div className="rounded-xl border border-[#526d82]/14 bg-[#f7f9fa]/70 px-3 py-2">
                                  <div className="text-[10px] font-black uppercase tracking-[0.18em] text-[#667085]">Pekiştir</div>
                                  <div className="mt-1 text-[11px] font-semibold text-[#344054]">
                                    {sourcePage.chunks.length} parça güvenli citation özeti
                                  </div>
                                </div>
                              </div>
                              <div className="grid gap-3 md:grid-cols-[0.34fr_0.66fr]">
                                <div className="rounded-xl border border-[#526d82]/14 bg-[#f7f9fa]/70 p-3">
                                  <div className="flex aspect-[3/4] flex-col items-center justify-center rounded-lg border border-dashed border-[#8a6a33]/24 bg-[#f7f4ec]/80 text-center">
                                    <FileText className="mb-3 h-7 w-7 text-[#8a6a33]" />
                                    <div className="text-3xl font-black text-[#172033]">{sourcePage.pageNumber}</div>
                                    <div className="mt-1 text-[10px] font-bold uppercase tracking-[0.2em] text-[#667085]">PDF/TXT sayfası</div>
                                    {activeSource && (
                                      <div className="mt-3 max-w-[150px] truncate text-[11px] text-[#667085]">{activeSource.fileName}</div>
                                    )}
                                  </div>
                                </div>
                                <div className="max-h-72 overflow-y-auto sidebar-scrollbar space-y-2 pr-1">
                                  {sourcePage.chunks.map((chunk) => {
                                    const focused = focusedChunkId === chunk.id || (!focusedChunkId && Boolean(chunk.highlightHint));
                                    return (
                                      <div
                                        key={chunk.id}
                                        className={`rounded-xl border px-3 py-2 transition ${
                                          focused
                                            ? "border-amber-500/35 bg-amber-500/12 shadow-sm"
                                            : "border-[#526d82]/12 bg-[#f7f9fa]/62"
                                        }`}
                                      >
                                        <div className="mb-1 flex flex-wrap items-center justify-between gap-2">
                                          <span className="rounded-full bg-[#dcecf3]/70 px-2 py-0.5 text-[10px] font-mono text-[#667085]">
                                            parça {chunk.chunkIndex + 1}
                                          </span>
                                          {focused && (
                                            <span className="rounded-full bg-amber-500/12 px-2 py-0.5 text-[10px] font-bold text-[#8a6a33]">
                                              highlight
                                            </span>
                                          )}
                                        </div>
                                        {chunk.highlightHint && (
                                          <p className="mb-2 rounded-lg bg-[#fff8ee] px-2 py-1 text-[11px] font-semibold text-[#8a6a33]">
                                            {chunk.highlightHint}
                                          </p>
                                        )}
                                        <p className="text-xs leading-relaxed text-[#344054]">
                                          Raw kaynak parçası burada gösterilmez; güvenli citation etiketi ve kısa highlight kullanılır.
                                        </p>
                                        <button
                                          type="button"
                                          onClick={() => handleSuggestedQuestion(`Sayfa ${chunk.pageNumber}, parça ${chunk.chunkIndex + 1} citation bağlamını kullanarak kısa bir pekiştirme anlatımı yap, sonra 2 mikro soru sor.`)}
                                          className="mt-2 rounded-full border border-[#526d82]/12 bg-[#f7f9fa]/68 px-2.5 py-1 text-[10px] font-bold text-[#667085] transition hover:border-amber-500/30 hover:text-[#8a6a33]"
                                        >
                                          Bu citation ile pekiştir
                                        </button>
                                      </div>
                                    );
                                  })}
                                </div>
                              </div>
                              </>
                            )}
                          </div>
                        )}
                      </div>
                    )}
                  </div>
                </div>

                <div className="space-y-4">
                  <div id="wiki-notebook-studio" className="scroll-mt-6">
                    <NotebookStudioPanel
                      topicId={topicId}
                      wikiPageId={wikiContextPageId}
                      wikiPageTitle={wikiContextPageId ? activePage?.title : undefined}
                      surface={notebookSurface}
                      sourceId={isOrkaLm ? activeSource?.id : undefined}
                      sourceTitle={isOrkaLm ? activeSource?.title || activeSource?.fileName : undefined}
                      sourceEvidenceStatus={activeSourceNotebook?.evidenceStatus ?? sourceNotebook?.evidenceStatus}
                    />
                  </div>

                  <div id="wiki-study-glossary" className="scroll-mt-6 rounded-xl border border-[#526d82]/16 bg-[#f7f9fa]/58 p-4">
                    <div className="flex items-center justify-between gap-3 mb-3">
                      <div className="flex items-center gap-2">
                        <Headphones className="w-4 h-4 text-sky-400" />
                        <span className="text-xs font-semibold uppercase tracking-widest text-[#344054]">
                          Sesli Ders
                        </span>
                      </div>
                      <button
                        data-testid="audio-overview-create"
                        onClick={handleCreateAudioOverview}
                        disabled={audioLoading || audioPolling}
                        className="text-xs px-3 py-1.5 rounded-lg bg-sky-500/10 hover:bg-sky-500/20 border border-sky-500/20 text-sky-300 transition disabled:opacity-50"
                      >
                        {audioLoading ? "Hazırlanıyor..." : audioPolling ? "Ses üretiliyor..." : "Sesli Özet"}
                      </button>
                    </div>
                    <div className="mb-3 space-y-2 rounded-xl border border-[#526d82]/12 bg-white/55 px-3 py-3">
                      <div className="flex flex-wrap gap-1.5">
                        {[
                          ["brief", "Brief"],
                          ["deep_dive", "Deep dive"],
                          ["critique", "Critique"],
                          ["debate", "Debate"],
                        ].map(([value, label]) => (
                          <button
                            key={value}
                            type="button"
                            onClick={() => setAudioMode(value as "brief" | "deep_dive" | "critique" | "debate")}
                            className={
                              audioMode === value
                                ? "rounded-lg border border-sky-500/30 bg-sky-500/15 px-2.5 py-1 text-[11px] font-black text-[#2d5870]"
                                : "rounded-lg border border-[#526d82]/12 bg-white/60 px-2.5 py-1 text-[11px] font-bold text-[#667085] transition hover:text-[#172033]"
                            }
                          >
                            {label}
                          </button>
                        ))}
                      </div>
                      <div className="flex flex-wrap gap-1.5">
                        {[
                          ["draft", "Draft"],
                          ["standard", "Standard"],
                          ["studio", "Studio"],
                        ].map(([value, label]) => (
                          <button
                            key={value}
                            type="button"
                            onClick={() => setTtsQuality(value as "draft" | "standard" | "studio")}
                            className={
                              ttsQuality === value
                                ? "rounded-lg border border-emerald-500/28 bg-emerald-500/12 px-2.5 py-1 text-[11px] font-black text-[#47725d]"
                                : "rounded-lg border border-[#526d82]/12 bg-white/60 px-2.5 py-1 text-[11px] font-bold text-[#667085] transition hover:text-[#172033]"
                            }
                          >
                            {label}
                          </button>
                        ))}
                      </div>
                    </div>
                    {audioJob ? (
                      <div className="space-y-3">
                        {(audioJob.status === "ready" || audioJob.status === "completed") && !audioJob.errorMessage ? (
                          <audio controls preload="auto" src={audioBlobUrl || ""} className="w-full">
                            {audioJob.captionTrack && (
                              <track
                                kind="captions"
                                srcLang="tr"
                                label="Turkce"
                                src={`data:text/vtt;charset=utf-8,${encodeURIComponent(audioJob.captionTrack)}`}
                                default
                              />
                            )}
                          </audio>
                        ) : audioPolling ? (
                          <div className="flex items-center gap-2 rounded-xl border border-sky-500/20 bg-sky-500/8 px-3 py-2 text-xs font-semibold leading-5 text-sky-300">
                            <svg className="h-4 w-4 animate-spin" viewBox="0 0 24 24" fill="none"><circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"/><path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"/></svg>
                            Edge-TTS ses dosyası hazırlanıyor, otomatik olarak yüklenecek...
                          </div>
                        ) : (
                          <div className="rounded-xl border border-amber-500/20 bg-[#fff8ee]/80 px-3 py-2 text-xs font-semibold leading-5 text-[#8a641f]">
                            Ses dosyası hazır değil. Orka metin akışını gösteriyor; bu sesli çalışma odası ya da üretilmiş ses gibi sunulmaz.
                            {audioJob.errorMessage && <span className="mt-1 block font-medium">{audioJob.errorMessage}</span>}
                            {audioError && <span className="mt-1 block font-medium">{audioError}</span>}
                          </div>
                        )}
                        <div className="text-[11px] text-[#667085]">
                          Konuşmacılar: {audioJob.speakers.join(", ") || "HOCA"}
                        </div>
                        <div className="flex flex-wrap gap-1.5 text-[10px] font-bold text-[#667085]">
                          <span className="rounded-full border border-[#526d82]/14 bg-white/60 px-2 py-0.5">{audioJob.surface ?? "wiki"}</span>
                          <span className="rounded-full border border-[#526d82]/14 bg-white/60 px-2 py-0.5">{audioJob.contextType ?? "wiki_page"}</span>
                          <span className="rounded-full border border-[#526d82]/14 bg-white/60 px-2 py-0.5">{audioJob.audioMode ?? "brief"}</span>
                          <span className="rounded-full border border-[#526d82]/14 bg-white/60 px-2 py-0.5">{audioJob.captionTrack ? "caption ready" : "caption fallback"}</span>
                        </div>
                        <button
                          data-testid="audio-study-room-open"
                          type="button"
                          onClick={() => setAudioClassroomOpen(true)}
                          className="inline-flex items-center gap-2 rounded-lg border border-emerald-500/20 bg-emerald-500/10 px-3 py-1.5 text-xs font-bold text-emerald-300 transition hover:bg-emerald-500/18"
                        >
                          <MessageCircle className="h-3.5 w-3.5" />
                          Sesli calisma odasinda sor
                        </button>
                        {audioClassroomOpen && (
                          <ClassroomAudioPlayer
                            text={audioJob.script}
                            topicId={topicId}
                            audioOverviewJobId={audioJob.id}
                            surface={audioJob.surface ?? (isOrkaLm ? "orkalm" : "wiki")}
                            wikiPageId={(audioJob.surface ?? (isOrkaLm ? "orkalm" : "wiki")) === "wiki" ? audioJob.wikiPageId ?? wikiContextPageId : undefined}
                            sourceId={(audioJob.surface ?? (isOrkaLm ? "orkalm" : "wiki")) === "orkalm" ? audioJob.sourceId ?? activeSource?.id : undefined}
                            audioMode={audioJob.audioMode ?? "brief"}
                            captionTrack={audioJob.captionTrack}
                            onClose={() => setAudioClassroomOpen(false)}
                          />
                        )}
                        <RichMarkdown
                          content={audioJob.script}
                          className="prose prose-invert prose-xs max-w-none text-xs prose-p:my-1 prose-p:text-[#667085]"
                        />
                      </div>
                    ) : (
                      <>
                      <p className="text-sm text-[#667085]">
                        Wiki veya OrkaLM contextinden transcript, caption fallback ve Edge-TTS destekli sesli ozet hazirlar.
                      </p>
                      <p className="hidden">
                        Wiki ve kaynaklardan 2-3 kişilik podcast metni ve oynatılabilir backend ses akışı üretir.
                      </p>
                      </>
                    )}
                  </div>

                  <div className="rounded-xl border border-[#526d82]/16 bg-[#f7f9fa]/58 p-4">
                    <div className="flex items-center gap-2 mb-3">
                      <Tags className="w-4 h-4 text-[#47725d]" />
                      <span className="text-xs font-semibold uppercase tracking-widest text-[#344054]">Terimler</span>
                      {notebookToolsLoading && <Loader2 className="w-3.5 h-3.5 animate-spin text-[#98a2b3]" />}
                    </div>
                    {glossary.length === 0 ? (
                      <p className="text-sm text-[#667085]">Henüz otomatik sözlük yok.</p>
                    ) : (
                      <div className="space-y-2 max-h-44 overflow-y-auto sidebar-scrollbar">
                        {glossary.map((item, i) => (
                          <div key={`${item.term}-${i}`} className="text-xs group flex items-start gap-2">
                            <button
                              onClick={() => askAbout(item.term)}
                              className="font-semibold text-[#172033] group-hover:text-[#47725d] transition text-left"
                            >
                              {item.term}
                            </button>
                            <span className="text-[#667085]"> — {item.simpleExplanation}</span>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>

                  <div className="rounded-xl border border-[#526d82]/16 bg-[#f7f9fa]/58 p-4">
                    <div className="flex items-center gap-2 mb-3">
                      <CalendarDays className="w-4 h-4 text-purple-300" />
                      <span className="text-xs font-semibold uppercase tracking-widest text-[#344054]">Timeline</span>
                    </div>
                    {timeline.length === 0 ? (
                      <p className="text-sm text-[#667085]">Bu içerikte belirgin tarihsel akış bulunamadı.</p>
                    ) : (
                      <div className="space-y-2 max-h-44 overflow-y-auto sidebar-scrollbar">
                        {timeline.map((item, i) => (
                          <button
                            key={`${item.year}-${i}`}
                            onClick={() => askAbout(`${item.year}: ${item.event}`)}
                            className="flex gap-3 text-xs text-left hover:bg-[#f7f9fa]/66 rounded-md px-2 py-1 transition"
                          >
                            <span className="font-mono text-purple-300 min-w-16">{item.year}</span>
                            <span className="text-[#667085]">{item.event}</span>
                          </button>
                        ))}
                      </div>
                    )}
                  </div>
                </div>
              </div>

              <div className="mb-8 rounded-2xl border border-[#526d82]/16 bg-gradient-to-br from-zinc-900/70 via-zinc-950/80 to-emerald-950/20 overflow-hidden">
                <div className="px-5 py-4 border-b border-[#526d82]/15 flex items-center justify-between gap-3">
                  <div className="flex items-center gap-2">
                    <Zap className="w-4 h-4 text-[#47725d]" />
                    <span className="text-xs font-semibold uppercase tracking-widest text-[#344054]">
                      Studio: Pekiştir ve Haritala
                    </span>
                    {notebookToolsLoading && <Loader2 className="w-3.5 h-3.5 animate-spin text-[#98a2b3]" />}
                  </div>
                  <span className="text-[10px] text-[#98a2b3] uppercase tracking-wider">
                    Kaynağa bağlı · tıkla, Copilot'a at
                  </span>
                </div>

                <div className="grid grid-cols-1 2xl:grid-cols-[1.1fr_0.9fr] gap-4 p-4">
                  <div className="rounded-xl border border-[#526d82]/16 bg-[#f7f9fa]/62 p-4">
                    <div className="flex items-center gap-2 mb-4">
                      <Network className="w-4 h-4 text-[#47725d]" />
                      <h4 className="text-sm font-semibold text-[#172033]">Mind Map</h4>
                    </div>

                    {mindMap?.nodes?.length ? (
                      <div className="space-y-4">
                        <div className="overflow-x-auto sidebar-scrollbar pb-2">
                          <div className="flex gap-4 min-w-max">
                            {rootNodes.map((root) => (
                              <motion.div
                                key={root.id}
                                initial={{ opacity: 0, y: 8 }}
                                animate={{ opacity: 1, y: 0 }}
                                className="min-w-[220px]"
                              >
                                <button
                                  onClick={() => askAbout(root.label)}
                                  className="w-full text-left px-4 py-3 rounded-xl bg-emerald-500/10 border border-[#8fb7a2]/34 text-[#47725d] hover:bg-emerald-500/20 transition shadow-lg shadow-emerald-950/20"
                                >
                                  <div className="text-sm font-semibold">{root.label}</div>
                                  <div className="text-[10px] text-[#47725d]/70 mt-1">Ana dal · soruya gönder</div>
                                </button>
                                <div className="mt-3 pl-4 border-l border-emerald-500/20 space-y-2">
                                  {childNodes(root.id).map((child) => (
                                    <div key={child.id}>
                                      <button
                                        onClick={() => askAbout(child.label)}
                                        className="w-full text-left px-3 py-2 rounded-lg bg-[#f7f9fa]/76 border border-[#526d82]/15 text-[#344054] hover:border-[#8fb7a2]/40 hover:text-[#47725d] transition"
                                      >
                                        <span className="text-xs font-medium">{child.label}</span>
                                      </button>
                                      {childNodes(child.id).length > 0 && (
                                        <div className="mt-2 ml-3 pl-3 border-l border-[#526d82]/15 space-y-1.5">
                                          {childNodes(child.id).map((leaf) => (
                                            <button
                                              key={leaf.id}
                                              onClick={() => askAbout(leaf.label)}
                                              className="block w-full text-left px-2.5 py-1.5 rounded-md text-[11px] bg-[#f7f9fa]/62 border border-[#526d82]/10 text-[#667085] hover:text-[#172033] hover:border-[#526d82]/18 transition"
                                            >
                                              {leaf.label}
                                            </button>
                                          ))}
                                        </div>
                                      )}
                                    </div>
                                  ))}
                                </div>
                              </motion.div>
                            ))}
                          </div>
                        </div>

                        <details className="group rounded-lg border border-[#526d82]/15 bg-[#f7f9fa]/70 overflow-hidden">
                          <summary className="cursor-pointer px-3 py-2 text-xs text-[#667085] hover:text-[#344054]">
                            Mermaid / UML benzeri diagram çıktısını göster
                          </summary>
                          <div className="px-3 pb-3">
                            <RichMarkdown
                              content={`\`\`\`mermaid\n${mindMap.mermaid}\n\`\`\``}
                              className="prose prose-invert prose-sm max-w-none"
                            />
                          </div>
                        </details>
                      </div>
                    ) : (
                      <p className="text-sm text-[#667085]">Kaynaklar hazır olduğunda dallı öğrenme haritası burada oluşur.</p>
                    )}
                  </div>

                  <div id="wiki-study-cards" className="scroll-mt-6 rounded-xl border border-[#526d82]/16 bg-[#f7f9fa]/62 p-4">
                    <div className="flex items-center gap-2 mb-4">
                      <HelpCircle className="w-4 h-4 text-amber-400" />
                      <h4 className="text-sm font-semibold text-[#172033]">Flashcards / Quizlets</h4>
                    </div>
                    {studyCards.length === 0 ? (
                      <p className="text-sm text-[#667085]">Pekiştirme kartları henüz hazır değil.</p>
                    ) : (
                      <div className="grid grid-cols-1 md:grid-cols-2 2xl:grid-cols-1 gap-3 max-h-[430px] overflow-y-auto sidebar-scrollbar pr-1">
                        {studyCards.map((card, i) => {
                          const flipped = !!flippedCards[i];
                          return (
                            <motion.button
                              key={`${card.front}-${i}`}
                              type="button"
                              onClick={() => setFlippedCards((prev) => ({ ...prev, [i]: !prev[i] }))}
                              whileTap={{ scale: 0.98 }}
                              className={`min-h-[128px] text-left rounded-xl border p-4 transition ${
                                flipped
                                  ? "bg-amber-500/10 border-amber-500/30"
                                  : "bg-[#f7f9fa]/76 border-[#526d82]/15 hover:border-[#e8c46f]/38"
                              }`}
                            >
                              <div className="flex items-start justify-between gap-2 mb-2">
                                <span className="text-[10px] font-mono text-[#98a2b3]">Kart {i + 1}</span>
                                <span className="text-[10px] text-amber-400">{flipped ? "Cevap" : "Soru"}</span>
                              </div>
                              <p className="text-sm font-medium text-[#172033] leading-relaxed">
                                {flipped ? card.back : card.front}
                              </p>
                              {flipped && card.sourceHint && (
                                <p className="text-[11px] text-[#667085] mt-3">{card.sourceHint}</p>
                              )}
                              <div className="mt-3 flex items-center justify-between gap-2">
                                <span className="text-[10px] text-[#98a2b3]">Çevirmek için tıkla</span>
                                <span
                                  onClick={(e) => {
                                    e.stopPropagation();
                                    askAbout(card.front);
                                  }}
                                  className="text-[10px] px-2 py-1 rounded-full bg-[#f7f9fa]/62 border border-[#526d82]/15 text-[#667085] hover:text-[#47725d] hover:border-[#8fb7a2]/34 transition"
                                >
                                  Copilot'a at
                                </span>
                              </div>
                            </motion.button>
                          );
                        })}
                      </div>
                    )}
                  </div>
                </div>
              </div>

              {activePage?.blocks && activePage.blocks.length > 0 ? (
                <div className="space-y-6">
                  <div className="rounded-2xl border border-[#526d82]/14 bg-white/62 p-4">
                    <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
                      <div className="flex items-center gap-2 text-[11px] font-black uppercase tracking-[0.18em] text-[#52768a]">
                        <ListChecks className="h-4 w-4" />
                        Blok gruplari
                      </div>
                      <span className="rounded-full bg-[#f7f9fa] px-2.5 py-1 text-[10px] font-bold text-[#667085]">
                        {activePage.blocks.length} blok
                      </span>
                    </div>
                    <div className="flex flex-wrap gap-2">
                      {wikiBlockGroups.map((group) => (
                        <span
                          key={group.id}
                          className="rounded-full border border-[#526d82]/12 bg-[#f7f9fa]/75 px-3 py-1 text-[10px] font-bold text-[#667085]"
                        >
                          {group.label}: {group.blocks.length}
                        </span>
                      ))}
                    </div>
                  </div>
                  {activePage.blocks.map((block, idx) => {
                    const parsedQuiz = tryParseQuiz(block.content);
                    const isQuiz = block.type === "Quiz" || block.type === "quiz" || !!parsedQuiz;
                    const uniqueId = block.id || idx;
                    const isExpanded = isQuiz ? !!expandedBlocks[uniqueId] : true;

                    const textWithoutJson = parsedQuiz
                      ? block.content.replace(/```(?:json|quiz)?\s*[\s\S]+?\s*```/i, "").replace(/\{[\s\S]*"question"[\s\S]*"options"[\s\S]*"explanation"[\s\S]*\}/i, "")
                      : block.content;

                    return (
                      <div
                        key={uniqueId}
                        className={`rounded-xl border overflow-hidden transition-all duration-300 shadow-sm ${
                          isQuiz
                            ? "border-amber-500/30 bg-amber-500/5 hover:border-amber-500/50"
                            : "border-[#526d82]/15 bg-[#f7f9fa]/58"
                        }`}
                      >
                        <div
                          className={`px-5 py-4 flex items-center justify-between gap-2 ${isQuiz ? "cursor-pointer select-none border-b border-amber-500/20 hover:bg-amber-500/10" : ""} ${!isExpanded ? "border-transparent" : "border-[#526d82]/15/40"}`}
                          onClick={() => isQuiz && toggleBlock(uniqueId)}
                        >
                          <div className="flex items-center gap-2">
                            <span
                              className={`text-xs font-bold uppercase tracking-widest ${isQuiz ? "text-amber-500 flex items-center gap-2" : "text-[#667085]"}`}
                            >
                              {isQuiz ? (
                                <>
                                  <Sparkles className="w-3.5 h-3.5" />
                                  Pekiştirme Sorusu
                                </>
                              ) : (
                                block.title || `Bölüm ${idx + 1}`
                              )}
                            </span>
                          </div>
                          {isQuiz && (
                            <ChevronDown
                              className={`w-4 h-4 text-amber-500 transition-transform duration-300 ${isExpanded ? "rotate-180" : ""}`}
                            />
                          )}
                        </div>
                        <AnimatePresence initial={false}>
                          {isExpanded && (
                            <motion.div
                              initial={{ height: 0, opacity: 0 }}
                              animate={{ height: "auto", opacity: 1 }}
                              exit={{ height: 0, opacity: 0 }}
                              transition={{ duration: 0.3, ease: "easeInOut" }}
                            >
                              <div className="px-6 py-5">
                                <div
                                  className="prose prose-invert prose-base max-w-none
                                  prose-headings:text-[#172033] prose-headings:font-bold
                                  prose-p:text-[#344054] prose-p:leading-relaxed
                                  prose-strong:text-[#172033]
                                  prose-li:text-[#344054]
                                  prose-code:text-[#47725d] prose-code:bg-emerald-500/10 prose-code:px-1.5 prose-code:py-0.5 prose-code:rounded-md prose-code:before:content-none prose-code:after:content-none
                                  prose-pre:bg-[#0c0c0c] prose-pre:border prose-pre:border-[#526d82]/15 prose-pre:rounded-xl prose-pre:shadow-2xl
                                "
                                >
                                  {textWithoutJson.trim() && (
                                    <RichMarkdown content={textWithoutJson} onSourceClick={handleSourceClick} onCitationClick={handleCitationClick} />
                                  )}
                                </div>
                                {parsedQuiz && (
                                  <div className="mt-6">
                                    <QuizCard quiz={parsedQuiz} messageId={block.id} topicId={topicId} />
                                  </div>
                                )}
                              </div>
                            </motion.div>
                          )}
                        </AnimatePresence>
                      </div>
                    );
                  })}
                </div>
              ) : (
                <div
                  className="prose prose-invert prose-base max-w-none
                    prose-headings:text-[#172033] prose-headings:font-bold
                    prose-p:text-[#344054] prose-p:leading-relaxed
                    prose-strong:text-[#172033]
                    prose-code:text-[#47725d] prose-code:bg-emerald-500/10 prose-code:px-1.5 prose-code:py-0.5 prose-code:rounded-md prose-code:before:content-none prose-code:after:content-none
                    prose-pre:bg-[#0c0c0c] prose-pre:border prose-pre:border-[#526d82]/15 prose-pre:rounded-xl
                  "
                >
                  <RichMarkdown content={pageContent} onSourceClick={handleSourceClick} onCitationClick={handleCitationClick} />
                </div>
              )}
            </div>
          )}
        </div>
      </div>

      {/* ─── RIGHT PANE: COPILOT (Ayarlanabilir / Kapatılabilir) ─── */}
      <AnimatePresence initial={false}>
        {showCopilot && (
          <motion.div
            initial={{ width: 0, opacity: 0 }}
            animate={{ width: 440, opacity: 1 }}
            exit={{ width: 0, opacity: 0 }}
            transition={{ duration: 0.3, ease: [0.16, 1, 0.3, 1] }}
            className="h-full bg-[#f7f9fa]/70 border-l border-[#526d82]/15 flex flex-col flex-shrink-0"
          >
            {/* Copilot Header */}
            <div className="px-5 py-4 border-b border-[#526d82]/15 flex items-center justify-between flex-shrink-0 bg-[#f7f9fa]/45">
              <div className="flex items-center gap-2">
                <MessageCircle className="w-4 h-4 text-[#667085]" />
                <span className="text-sm font-medium text-[#172033]">Belge Ajanı</span>
              </div>
              <button
                onClick={() => setShowCopilot(false)}
                className="text-[#667085] hover:text-[#344054] p-2 rounded-lg hover:bg-[#dcecf3]/55 transition-colors"
                title="Paneli Gizle"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            {/* Messages Area */}
            <div className="flex-1 overflow-y-auto px-5 py-4 space-y-4 sidebar-scrollbar bg-[#f7f9fa]/62">
              {messages.length === 0 && (
                <div className="flex flex-col items-center justify-center h-full text-center gap-4 py-8">
                  <div className="w-12 h-12 rounded-2xl bg-[#dcecf3]/75 flex items-center justify-center shadow-inner border border-[#526d82]/18/50">
                    <MessageCircle className="w-6 h-6 text-[#667085]" />
                  </div>
                  <div>
                    <p className="text-base text-[#344054] font-semibold mb-1">
                      Belge Ajanı Hazır
                    </p>
                    <p className="text-sm text-[#667085] max-w-[260px] leading-relaxed">
                      Yandaki ders dokümanı hakkında aklınıza takılan her şeyi bana sorabilirsiniz.
                    </p>
                  </div>
                </div>
              )}

              {messages.map((msg, i) => (
                <div
                  key={i}
                  className={`flex ${msg.role === "user" ? "justify-end" : "justify-start"}`}
                >
                  <div
                    className={`max-w-[90%] rounded-2xl px-4 py-3 text-sm leading-relaxed shadow-sm ${
                      msg.role === "user"
                        ? "bg-[#dcecf3]/75 text-[#172033] border border-[#526d82]/18/50 rounded-tr-sm"
                        : "bg-[#f7f9fa]/76 text-[#172033] border border-[#526d82]/18 rounded-tl-sm"
                    }`}
                  >
                    <RichMarkdown
                      content={msg.content || "…"}
                      onSourceClick={handleSourceClick}
                      onCitationClick={handleCitationClick}
                      className="prose prose-invert prose-sm max-w-none prose-p:my-1.5 prose-p:leading-relaxed prose-headings:text-sm prose-headings:mb-1.5 prose-headings:mt-3 prose-li:my-1 prose-code:text-[#47725d] prose-code:text-[13px] prose-a:text-[#47725d]"
                    />
                    {msg.role === "assistant" && (msg.citations?.length ?? 0) > 0 && (
                      <div className="mt-3 flex flex-wrap gap-1.5 border-t border-[#526d82]/10 pt-2">
                        {msg.citations?.slice(0, 8).map((citation, idx) => {
                          const scopeSummary = citationScopeSummary(citation);
                          return (
                          <button
                            key={`${citation.citationId ?? citation.label ?? idx}-${idx}`}
                            type="button"
                            onClick={() => void handleCitationChipClick(citation)}
                            className="rounded-xl border border-[#526d82]/16 bg-white/70 px-2.5 py-1.5 text-left text-[10px] font-semibold text-[#526d82] hover:border-[#2d5870]/30 hover:text-[#2d5870]"
                            title={citationDisplayTitle(citation)}
                          >
                            <span className="block leading-4">{citationPrimaryLabel(citation)}</span>
                            {scopeSummary && (
                              <span className="block text-[9px] font-bold leading-4 text-[#8a6a33]">
                                {scopeSummary}
                              </span>
                            )}
                          </button>
                          );
                        })}
                      </div>
                    )}
                    {msg.role === "assistant" && msg.metadata && (
                      <WikiLearningTraceSummary metadata={msg.metadata} />
                    )}
                    {msg.role === "assistant" && (msg.artifacts?.length ?? 0) > 0 && (
                      <div className="mt-3 space-y-2">
                        {msg.artifacts?.map((artifact) => (
                          <div key={artifact.id} className="rounded-xl border border-[#526d82]/14 bg-white/65 p-3">
                            <div className="mb-2 flex items-center justify-between gap-2">
                              <span className="text-xs font-bold text-[#172033]">{artifact.title || "Kaynak kartı"}</span>
                              <span className="rounded-full bg-[#eef1f3] px-2 py-0.5 text-[10px] font-bold text-[#667085]">
                                {artifact.artifactType}
                              </span>
                            </div>
                            <RichMarkdown
                              content={artifact.content}
                              onSourceClick={handleSourceClick}
                              onCitationClick={handleCitationClick}
                              className="prose prose-sm max-w-none prose-p:my-1 prose-li:my-1 prose-code:text-[#47725d]"
                            />
                          </div>
                        ))}
                      </div>
                    )}
                    {msg.role === "assistant" && msg.metadata?.fallbackReason && (
                      <div className="mt-2 rounded-lg border border-amber-500/20 bg-amber-50 px-3 py-2 text-xs font-semibold text-amber-700">
                        {msg.metadata.fallbackReason === "source_retrieval_empty"
                          ? "Bu cevap için kaynaklarda net dayanak bulunamadı."
                          : "Cevap güvenli kaynak modunda sınırlandı."}
                      </div>
                    )}
                  </div>
                </div>
              ))}

              {isStreaming && (
                <div className="flex items-center gap-3 pl-2 py-2">
                  <div className="flex gap-1.5">
                    <span className="w-1.5 h-1.5 bg-zinc-500 rounded-full animate-bounce [animation-delay:0ms]" />
                    <span className="w-1.5 h-1.5 bg-zinc-500 rounded-full animate-bounce [animation-delay:150ms]" />
                    <span className="w-1.5 h-1.5 bg-zinc-500 rounded-full animate-bounce [animation-delay:300ms]" />
                  </div>
                  <span className="text-xs text-[#667085] font-medium tracking-wide">
                    Yanıt sentezleniyor...
                  </span>
                </div>
              )}
              <div ref={messagesEndRef} />
            </div>

            {/* Input Area */}
            <div className="px-4 py-4 border-t border-[#526d82]/15 bg-[#f7f9fa]/62 flex-shrink-0">
              <div className="relative flex items-end gap-2 bg-[#f7f9fa]/62 border border-[#526d82]/18 focus-within:border-[#526d82]/22 focus-within:bg-[#f7f9fa]/62 rounded-xl px-3 py-2 transition-all shadow-inner">
                <textarea
                  value={input}
                  onChange={(e) => setInput(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" && !e.shiftKey) {
                      e.preventDefault();
                      handleSend();
                    }
                  }}
                  placeholder="Ders hakkında bir soru sorun..."
                  className="flex-1 bg-transparent text-sm text-[#172033] placeholder-zinc-600 outline-none resize-none max-h-32 min-h-[40px] py-2 sidebar-scrollbar"
                  rows={input.split("\n").length > 1 ? Math.min(input.split("\n").length, 5) : 1}
                />
                <button
                  onClick={handleSend}
                  disabled={isStreaming || !input.trim()}
                  className="mb-1 p-2 rounded-lg bg-emerald-500/10 text-emerald-500 hover:bg-emerald-500 hover:text-emerald-950 transition-all duration-200 disabled:opacity-30 disabled:hover:bg-emerald-500/10 disabled:hover:text-emerald-500"
                >
                  <Send className="w-4 h-4" />
                </button>
              </div>
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Closed State FAB */}
      {!showCopilot && (
        <motion.button
          initial={{ opacity: 0, scale: 0.8 }}
          animate={{ opacity: 1, scale: 1 }}
          onClick={() => setShowCopilot(true)}
          className="absolute right-8 bottom-8 flex items-center gap-2 px-5 py-3 rounded-full bg-[#f7f9fa]/68 hover:bg-[#dcecf3]/70 border border-[#526d82]/15 hover:border-[#526d82]/18 shadow-2xl transition-all duration-300 group z-20"
        >
          <Sparkles className="w-5 h-5 text-amber-500 group-hover:text-amber-400" />
          <span className="text-sm font-semibold text-[#344054] group-hover:text-[#172033]">
            Ajanı Aç
          </span>
        </motion.button>
      )}
    </motion.div>
  );
}

// ── Wiki Generating Skeleton ──────────────────────────────────────────────────

function WikiGeneratingSkeleton() {
  return (
    <div className="max-w-4xl mx-auto pb-12">
      {/* Status banner */}
      <div className="flex items-center gap-3 mb-8 px-4 py-3 rounded-xl bg-[#f7f9fa]/70 border border-[#526d82]/15">
        <Clock className="w-4 h-4 text-emerald-500 animate-pulse flex-shrink-0" />
        <div>
          <p className="text-sm font-medium text-[#172033]">Kişisel wikiniz hazırlanıyor</p>
          <p className="text-xs text-[#667085] mt-0.5">
            Sohbet verilerinizden derleniyor, lütfen bekleyin...
          </p>
        </div>
        <div className="ml-auto flex gap-1">
          {[0, 1, 2].map((i) => (
            <span
              key={i}
              className="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-bounce"
              style={{ animationDelay: `${i * 150}ms` }}
            />
          ))}
        </div>
      </div>

      {/* Skeleton lines */}
      <div className="space-y-6">
        {/* Title skeleton */}
        <div className="h-8 w-2/3 rounded-lg bg-[#dcecf3]/62 animate-pulse" />

        {/* Block skeletons */}
        {[1, 0.8, 0.9, 0.7].map((w, i) => (
          <div key={i} className="rounded-xl border border-[#526d82]/15 bg-[#f7f9fa]/58 p-5 space-y-3">
            <div className="h-3.5 rounded bg-[#dcecf3]/70/70 animate-pulse" style={{ width: `${w * 40}%` }} />
            <div className="h-3 rounded bg-[#dcecf3]/55 animate-pulse w-full" />
            <div className="h-3 rounded bg-[#dcecf3]/55 animate-pulse" style={{ width: `${w * 80}%` }} />
            <div className="h-3 rounded bg-[#dcecf3]/55 animate-pulse" style={{ width: `${w * 65}%` }} />
          </div>
        ))}
      </div>
    </div>
  );
}
