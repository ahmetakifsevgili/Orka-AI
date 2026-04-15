/*
 * WikiDrawer — Premium Wiki Copilot Panel
 * Sağdan kayan panel. İçerisinde:
 *   1. Wiki doküman görüntüleme (Mevcut)
 *   2. Wiki Soru-Cevap Ajanı (Mevcut, iyileştirilmiş)
 *   3. Korteks Derin Araştırma (YENİ — internetten araştırma)
 */

import { useState, useEffect, useRef } from "react";
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
} from "lucide-react";
import toast from "react-hot-toast";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { WikiAPI, storage } from "@/services/api";
import { tryParseQuiz } from "@/lib/quizParser";
import QuizCard from "./QuizCard";

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
      className="flex-1 flex bg-zinc-950 overflow-hidden relative"
    >
      {/* ─── LEFT PANE: WIKI CONTENT ─── */}
      <div className="flex-1 flex flex-col h-full overflow-hidden">
        {/* Header */}
        <div className="px-6 py-4 flex items-center justify-between flex-shrink-0 border-b border-zinc-800/60 bg-zinc-950/50 backdrop-blur-sm z-10">
          <div className="flex flex-col gap-1 min-w-0 pr-8">
            <div className="flex items-center gap-1.5 text-xs text-zinc-500 truncate font-medium tracking-wide">
              <span>Müfredat Haritası</span>
              <span className="text-zinc-700">/</span>
              <span className="text-zinc-300 truncate">
                {activePage?.title || "Konu"}
              </span>
            </div>
            <h3 className="text-xl font-bold text-zinc-100 truncate flex items-center gap-2.5">
              <BookOpen className="w-5 h-5 text-zinc-400" />
              <span>{activePage?.title || "Wiki"}</span>
            </h3>
          </div>
          {/* Sadece kapatıp sohbet listesine dönmek istenebileceği ihtimali için ufak buton */}
          <button
            onClick={onClose}
            className="text-zinc-500 hover:text-zinc-300 hover:bg-zinc-800 transition-colors duration-150 p-2 rounded-lg"
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
              <span className="text-sm text-zinc-400">Ders yükleniyor...</span>
            </div>
          )}

          {!loading && error && (
            <div className="text-center py-16 bg-zinc-900/30 rounded-2xl border border-zinc-800/50">
              <p className="text-base text-zinc-400 mb-2">
                Wiki içeriği henüz oluşturulmadı.
              </p>
              <p className="text-sm text-zinc-600">
                Sistem konuyu hazırlarken lütfen bekleyin.
              </p>
            </div>
          )}

          {!loading && !error && pages.length === 0 && isPolling && (
            <WikiGeneratingSkeleton />
          )}

          {!loading && !error && pages.length === 0 && !isPolling && (
            <div className="text-center py-16">
              <p className="text-base text-zinc-500 mb-2">Wiki içeriği bulunamadı.</p>
              <p className="text-sm text-zinc-600">Bir konu anlatımı tamamlandığında wiki otomatik oluşturulur.</p>
            </div>
          )}

          {!loading && activePage && (
            <div className="max-w-4xl mx-auto pb-12">
              <div className="mb-8">
                <h1 className="text-2xl md:text-3xl font-extrabold text-zinc-100 tracking-tight">
                  {activePage.title}
                </h1>
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
                            : "border-zinc-800/60 bg-zinc-900/30"
                        }`}
                      >
                        <div
                          className={`px-5 py-4 flex items-center justify-between gap-2 ${isQuiz ? "cursor-pointer select-none border-b border-amber-500/20 hover:bg-amber-500/10" : ""} ${!isExpanded ? "border-transparent" : "border-zinc-800/40"}`}
                          onClick={() => isQuiz && toggleBlock(uniqueId)}
                        >
                          <div className="flex items-center gap-2">
                            <span
                              className={`text-xs font-bold uppercase tracking-widest ${isQuiz ? "text-amber-500 flex items-center gap-2" : "text-zinc-500"}`}
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
                                  prose-headings:text-zinc-100 prose-headings:font-bold
                                  prose-p:text-zinc-300 prose-p:leading-relaxed
                                  prose-strong:text-zinc-100
                                  prose-li:text-zinc-300
                                  prose-code:text-emerald-400 prose-code:bg-emerald-500/10 prose-code:px-1.5 prose-code:py-0.5 prose-code:rounded-md prose-code:before:content-none prose-code:after:content-none
                                  prose-pre:bg-[#0c0c0c] prose-pre:border prose-pre:border-zinc-800 prose-pre:rounded-xl prose-pre:shadow-2xl
                                "
                                >
                                  {textWithoutJson.trim() && (
                                    <ReactMarkdown remarkPlugins={[remarkGfm]}>
                                      {textWithoutJson}
                                    </ReactMarkdown>
                                  )}
                                </div>
                                {parsedQuiz && (
                                  <div className="mt-6">
                                    <QuizCard quiz={parsedQuiz} messageId={block.id} />
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
                    prose-headings:text-zinc-100 prose-headings:font-bold
                    prose-p:text-zinc-300 prose-p:leading-relaxed
                    prose-strong:text-zinc-100
                    prose-code:text-emerald-400 prose-code:bg-emerald-500/10 prose-code:px-1.5 prose-code:py-0.5 prose-code:rounded-md prose-code:before:content-none prose-code:after:content-none
                    prose-pre:bg-[#0c0c0c] prose-pre:border prose-pre:border-zinc-800 prose-pre:rounded-xl
                  "
                >
                  <ReactMarkdown remarkPlugins={[remarkGfm]}>
                    {pageContent}
                  </ReactMarkdown>
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
            className="h-full bg-zinc-950/80 border-l border-zinc-800 flex flex-col flex-shrink-0"
          >
            {/* Copilot Header */}
            <div className="px-5 py-4 border-b border-zinc-800/60 flex items-center justify-between flex-shrink-0 bg-zinc-900/20">
              <div className="flex items-center gap-2">
                <MessageCircle className="w-4 h-4 text-zinc-400" />
                <span className="text-sm font-medium text-zinc-200">Belge Ajanı</span>
              </div>
              <button
                onClick={() => setShowCopilot(false)}
                className="text-zinc-500 hover:text-zinc-300 p-2 rounded-lg hover:bg-zinc-800/50 transition-colors"
                title="Paneli Gizle"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            {/* Messages Area */}
            <div className="flex-1 overflow-y-auto px-5 py-4 space-y-4 sidebar-scrollbar bg-zinc-950/50">
              {messages.length === 0 && (
                <div className="flex flex-col items-center justify-center h-full text-center gap-4 py-8">
                  <div className="w-12 h-12 rounded-2xl bg-zinc-800/80 flex items-center justify-center shadow-inner border border-zinc-700/50">
                    <MessageCircle className="w-6 h-6 text-zinc-400" />
                  </div>
                  <div>
                    <p className="text-base text-zinc-300 font-semibold mb-1">
                      Belge Ajanı Hazır
                    </p>
                    <p className="text-sm text-zinc-500 max-w-[260px] leading-relaxed">
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
                        ? "bg-zinc-800/80 text-zinc-100 border border-zinc-700/50 rounded-tr-sm"
                        : "bg-zinc-900/80 text-zinc-200 border border-zinc-800/80 rounded-tl-sm"
                    }`}
                  >
                    <div className="prose prose-invert prose-sm max-w-none prose-p:my-1.5 prose-p:leading-relaxed prose-headings:text-sm prose-headings:mb-1.5 prose-headings:mt-3 prose-li:my-1 prose-code:text-emerald-400 prose-code:text-[13px] prose-a:text-emerald-400">
                      <ReactMarkdown remarkPlugins={[remarkGfm]}>
                        {msg.content || "…"}
                      </ReactMarkdown>
                    </div>
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
                  <span className="text-xs text-zinc-500 font-medium tracking-wide">
                    Yanıt sentezleniyor...
                  </span>
                </div>
              )}
              <div ref={messagesEndRef} />
            </div>

            {/* Input Area */}
            <div className="px-4 py-4 border-t border-zinc-800/60 bg-zinc-900/40 flex-shrink-0">
              <div className="relative flex items-end gap-2 bg-zinc-950/50 border border-zinc-800/80 focus-within:border-zinc-700/80 focus-within:bg-zinc-950 rounded-xl px-3 py-2 transition-all shadow-inner">
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
                  className="flex-1 bg-transparent text-sm text-zinc-200 placeholder-zinc-600 outline-none resize-none max-h-32 min-h-[40px] py-2 sidebar-scrollbar"
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
          className="absolute right-8 bottom-8 flex items-center gap-2 px-5 py-3 rounded-full bg-zinc-900 hover:bg-zinc-800 border border-zinc-800 hover:border-zinc-700 shadow-2xl transition-all duration-300 group z-20"
        >
          <Sparkles className="w-5 h-5 text-amber-500 group-hover:text-amber-400" />
          <span className="text-sm font-semibold text-zinc-300 group-hover:text-zinc-100">
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
      <div className="flex items-center gap-3 mb-8 px-4 py-3 rounded-xl bg-zinc-900/60 border border-zinc-800/60">
        <Clock className="w-4 h-4 text-emerald-500 animate-pulse flex-shrink-0" />
        <div>
          <p className="text-sm font-medium text-zinc-200">Kişisel wikiniz hazırlanıyor</p>
          <p className="text-xs text-zinc-500 mt-0.5">
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
        <div className="h-8 w-2/3 rounded-lg bg-zinc-800/60 animate-pulse" />

        {/* Block skeletons */}
        {[1, 0.8, 0.9, 0.7].map((w, i) => (
          <div key={i} className="rounded-xl border border-zinc-800/60 bg-zinc-900/30 p-5 space-y-3">
            <div className="h-3.5 rounded bg-zinc-800/70 animate-pulse" style={{ width: `${w * 40}%` }} />
            <div className="h-3 rounded bg-zinc-800/50 animate-pulse w-full" />
            <div className="h-3 rounded bg-zinc-800/50 animate-pulse" style={{ width: `${w * 80}%` }} />
            <div className="h-3 rounded bg-zinc-800/50 animate-pulse" style={{ width: `${w * 65}%` }} />
          </div>
        ))}
      </div>
    </div>
  );
}
