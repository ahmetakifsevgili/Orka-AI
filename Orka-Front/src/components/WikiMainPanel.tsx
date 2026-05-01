/*
 * WikiDrawer — Premium Wiki Copilot Panel
 * Sağdan kayan panel. İçerisinde:
 *   1. Wiki doküman görüntüleme (Mevcut)
 *   2. Wiki Soru-Cevap Ajanı (Mevcut, iyileştirilmiş)
 *   3. Korteks Derin Araştırma (YENİ — internetten araştırma)
 */

import { useState, useEffect, useMemo, useRef } from "react";
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
  Upload,
  FileText,
  Headphones,
  CalendarDays,
  Tags,
  Network,
  HelpCircle,
  Zap,
} from "lucide-react";
import toast from "react-hot-toast";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { AudioOverviewAPI, LearningAPI, SourcesAPI, WikiAPI, storage } from "@/services/api";
import { tryParseQuiz } from "@/lib/quizParser";
import QuizCard from "./QuizCard";
import RichMarkdown from "./RichMarkdown";

interface WikiPage {
  id: string;
  title: string;
  blocks?: Array<{
    id: string;
    type: string;
    content: string;
    title?: string;
  }>;
}

interface WikiMainPanelProps {
  topicId: string;
  onClose: () => void;
}

interface CopilotMessage {
  role: "user" | "assistant" | "system";
  content: string;
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
  text: string;
  highlightHint?: string;
}

interface MindMapNode {
  id: string;
  label: string;
  parentId?: string | null;
  depth: number;
}

const MAX_POLL_ATTEMPTS = 20; // 20 × 3s = 60 saniye maksimum bekleme

export default function WikiMainPanel({ topicId, onClose }: WikiMainPanelProps) {
  const [pages, setPages] = useState<WikiPage[]>([]);
  const [activePage, setActivePage] = useState<WikiPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);
  const [isPolling, setIsPolling] = useState(false);
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
  const [sourceAsking, setSourceAsking] = useState(false);
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
  const [audioJob, setAudioJob] = useState<{
    id: string;
    status: string;
    script: string;
    speakers: string[];
    errorMessage?: string;
  } | null>(null);
  const [audioLoading, setAudioLoading] = useState(false);

  const toggleBlock = (blockId: string | number) => {
    setExpandedBlocks((prev) => ({ ...prev, [blockId]: !prev[blockId] }));
  };

  // Auto-scroll messages
  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  // Fetch wiki pages (initial)
  useEffect(() => {
    setLoading(true);
    setError(false);
    setPages([]);
    setActivePage(null);
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
          setIsPolling(true);
        }
      })
      .catch(() => setError(true))
      .finally(() => setLoading(false));
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

  const refreshSources = async () => {
    setSourcesLoading(true);
    try {
      const data = await SourcesAPI.getTopicSources(topicId);
      setSources(data);
      if (!activeSource && data.length > 0) setActiveSource(data[0]);
    } catch {
      setSources([]);
    } finally {
      setSourcesLoading(false);
    }
  };

  useEffect(() => {
    refreshSources();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [topicId]);

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
    try {
      const result = await SourcesAPI.ask(activeSource.id, sourceQuestion.trim());
      setSourceAnswer(result.answer);
      setSourceCitations(result.citations ?? []);
      const firstCitation = result.citations?.[0];
      if (firstCitation) {
        await openSourcePage(activeSource.id, firstCitation.pageNumber, {
          focusChunkId: firstCitation.id,
          action: "source-answer-first-citation",
        });
      }
      setNotebookRefreshTick((tick) => tick + 1);
    } catch {
      toast.error("Kaynaklı cevap üretilemedi.");
    } finally {
      setSourceAsking(false);
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
        sourceViewerRef.current?.scrollIntoView({ behavior: "smooth", block: "nearest" });
      }, 0);
    } catch {
      toast.error("Kaynak sayfası açılamadı.");
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
    setAudioLoading(true);
    try {
      const job = await AudioOverviewAPI.create({ topicId });
      setAudioJob(job);
      toast.success("Sesli özet hazırlandı.");
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

  const handleCitationClick = (kind: "doc" | "wiki" | "web" | "external", ref: string) => {
    recordWikiAction("citation-clicked", `${kind}:${ref}`, { citationKind: kind, ref });
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
        body: JSON.stringify({ question: userQ }),
      });

      if (!response.ok) throw new Error("Ajan yanıt veremedi.");

      const reader = response.body?.getReader();
      const decoder = new TextDecoder();
      let aiContent = "";

      while (reader) {
        const { value, done } = await reader.read();
        if (done) break;

        const chunk = decoder.decode(value);
        const lines = chunk.split("\n");
        for (const line of lines) {
          if (line.startsWith("data: ")) {
            const str = line.replace("data: ", "").trim();
            if (!str || str === "[DONE]") continue;
            try {
              const json = JSON.parse(str);
              if (json.content) {
                aiContent += json.content;
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
              <span>Müfredat Haritası</span>
              <span className="text-zinc-700">/</span>
              <span className="text-[#344054] truncate">
                {activePage?.title || "Konu"}
              </span>
            </div>
            <h3 className="text-xl font-bold text-[#172033] truncate flex items-center gap-2.5">
              <BookOpen className="w-5 h-5 text-[#667085]" />
              <span>{activePage?.title || "Wiki"}</span>
            </h3>
          </div>
          {/* Sadece kapatıp sohbet listesine dönmek istenebileceği ihtimali için ufak buton */}
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

              {/* NotebookLM-tarzı Briefing Document — wiki üst kısmında "okumadan önce göz at" özet */}
              {(briefing || briefingLoading) && (
                <div className="mb-8 rounded-xl border border-emerald-500/20 bg-emerald-500/5 overflow-hidden">
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
                <div className="mb-8 rounded-xl border border-sky-500/20 bg-sky-500/5 overflow-hidden">
                  <div className="px-5 py-3 flex items-center justify-between gap-3 border-b border-sky-500/15">
                    <div className="flex items-center gap-2">
                      <Zap className="w-4 h-4 text-sky-400" />
                      <span className="text-xs font-semibold uppercase tracking-widest text-sky-300">
                        Kisisel Pekistirme
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
                        <p className="text-sm text-[#667085]">Henuz konu bazli zayiflik sinyali yok.</p>
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

              <div className="mb-8 grid grid-cols-1 xl:grid-cols-[1.15fr_0.85fr] gap-4">
                <div className="rounded-xl border border-[#526d82]/16 bg-[#f7f9fa]/58 overflow-hidden">
                  <div className="px-5 py-3 border-b border-[#526d82]/15 flex items-center justify-between gap-3">
                    <div className="flex items-center gap-2">
                      <FileText className="w-4 h-4 text-amber-400" />
                      <span className="text-xs font-semibold uppercase tracking-widest text-[#344054]">
                        Notebook Kaynakları
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
                        className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-amber-500/10 hover:bg-amber-500/20 border border-amber-500/20 text-amber-300 text-xs transition disabled:opacity-50"
                      >
                        {uploadingSource ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Upload className="w-3.5 h-3.5" />}
                        Kaynak Yükle
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
                    {!sourcesLoading && sources.length === 0 && (
                      <p className="text-sm text-[#667085]">
                        Bu konuya PDF, TXT veya MD yükleyerek belgeyle sohbeti başlatabilirsin.
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
                      </div>
                    )}
                    {activeSource && (
                      <div className="pt-3 border-t border-[#526d82]/15 space-y-3">
                        <div className="flex gap-2">
                          <input
                            value={sourceQuestion}
                            onChange={(e) => setSourceQuestion(e.target.value)}
                            onKeyDown={(e) => e.key === "Enter" && handleAskSource()}
                            placeholder={`${activeSource.fileName} hakkında soru sor...`}
                            className="flex-1 bg-[#f7f9fa]/62 border border-[#526d82]/15 focus:border-[#e8c46f]/55 rounded-lg px-3 py-2 text-sm text-[#172033] placeholder-zinc-600 outline-none"
                          />
                          <button
                            onClick={handleAskSource}
                            disabled={sourceAsking || !sourceQuestion.trim()}
                            className="p-2 rounded-lg bg-amber-500/10 text-amber-300 hover:bg-amber-500/20 transition disabled:opacity-40"
                          >
                            {sourceAsking ? <Loader2 className="w-4 h-4 animate-spin" /> : <Send className="w-4 h-4" />}
                          </button>
                        </div>
                        {sourceAnswer && (
                          <div className="rounded-lg border border-[#526d82]/15 bg-[#f7f9fa]/62 px-4 py-3 space-y-3">
                            <RichMarkdown
                              content={sourceAnswer}
                              onSourceClick={handleSourceClick}
                              onCitationClick={handleCitationClick}
                              className="prose prose-invert prose-sm max-w-none prose-p:my-1.5 prose-p:text-[#344054]"
                            />
                            {sourceCitations.length > 0 && (
                              <div className="rounded-xl border border-amber-500/18 bg-[#fff8ee]/70 p-3">
                                <div className="mb-2 flex items-center justify-between gap-2">
                                  <span className="text-[10px] font-black uppercase tracking-[0.2em] text-[#8a6a33]">
                                    Kanıt citationları
                                  </span>
                                  <span className="text-[10px] font-semibold text-[#667085]">{sourceCitations.length} parça</span>
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
                                      title={citation.text}
                                    >
                                      [doc:p{citation.pageNumber}] parça {citation.chunkIndex + 1}
                                    </button>
                                  ))}
                                </div>
                              </div>
                            )}
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
                                    {sourcePage.chunks.length} parça Copilot'a gönderilebilir
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
                                        <p className="text-xs leading-relaxed text-[#344054]">{chunk.text}</p>
                                        <button
                                          type="button"
                                          onClick={() => handleSuggestedQuestion(`Bu kaynak parçasını kullanarak kısa bir pekiştirme anlatımı yap, sonra 2 mikro soru sor:\n\n${chunk.text.slice(0, 900)}`)}
                                          className="mt-2 rounded-full border border-[#526d82]/12 bg-[#f7f9fa]/68 px-2.5 py-1 text-[10px] font-bold text-[#667085] transition hover:border-amber-500/30 hover:text-[#8a6a33]"
                                        >
                                          Bu parçayla pekiştir
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
                  <div className="rounded-xl border border-[#526d82]/16 bg-[#f7f9fa]/58 p-4">
                    <div className="flex items-center justify-between gap-3 mb-3">
                      <div className="flex items-center gap-2">
                        <Headphones className="w-4 h-4 text-sky-400" />
                        <span className="text-xs font-semibold uppercase tracking-widest text-[#344054]">
                          Audio Overview
                        </span>
                      </div>
                      <button
                        onClick={handleCreateAudioOverview}
                        disabled={audioLoading}
                        className="text-xs px-3 py-1.5 rounded-lg bg-sky-500/10 hover:bg-sky-500/20 border border-sky-500/20 text-sky-300 transition disabled:opacity-50"
                      >
                        {audioLoading ? "Hazırlanıyor..." : "Sesli Özet"}
                      </button>
                    </div>
                    {audioJob ? (
                      <div className="space-y-3">
                        <audio controls src={AudioOverviewAPI.streamUrl(audioJob.id)} className="w-full" />
                        <div className="text-[11px] text-[#667085]">
                          Konuşmacılar: {audioJob.speakers.join(", ") || "HOCA"}
                        </div>
                        <RichMarkdown
                          content={audioJob.script}
                          className="prose prose-invert prose-xs max-w-none text-xs prose-p:my-1 prose-p:text-[#667085]"
                        />
                      </div>
                    ) : (
                      <p className="text-sm text-[#667085]">
                        Wiki ve kaynaklardan 2-3 kişilik podcast metni ve oynatılabilir backend ses akışı üretir.
                      </p>
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

                  <div className="rounded-xl border border-[#526d82]/16 bg-[#f7f9fa]/62 p-4">
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
