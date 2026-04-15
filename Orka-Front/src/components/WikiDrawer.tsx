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
  Search,
  Sparkles,
  Globe,
  MessageCircle,
  Send,
  Paperclip,
  Link,
} from "lucide-react";
import toast from "react-hot-toast";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { WikiAPI, KorteksAPI, storage } from "@/services/api";
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

interface WikiDrawerProps {
  topicId: string;
  onClose: () => void;
}

type CopilotMode = "wiki-chat" | "korteks";

interface CopilotMessage {
  role: "user" | "assistant" | "system";
  content: string;
}

export default function WikiDrawer({ topicId, onClose }: WikiDrawerProps) {
  const [pages, setPages] = useState<WikiPage[]>([]);
  const [activePage, setActivePage] = useState<WikiPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);
  const [width, setWidth] = useState(520);
  const [isResizing, setIsResizing] = useState(false);
  const [expandedBlocks, setExpandedBlocks] = useState<
    Record<string, boolean>
  >({});

  // Copilot State
  const [showCopilot, setShowCopilot] = useState(false);
  const [copilotMode, setCopilotMode] = useState<CopilotMode>("wiki-chat");
  const [messages, setMessages] = useState<CopilotMessage[]>([]);
  const [input, setInput] = useState("");
  const [isStreaming, setIsStreaming] = useState(false);
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  // Korteks dosya/URL state
  const [attachedFile, setAttachedFile] = useState<File | null>(null);
  const [urlInput, setUrlInput] = useState("");
  const [showUrlInput, setShowUrlInput] = useState(false);

  const toggleBlock = (blockId: string | number) => {
    setExpandedBlocks((prev) => ({ ...prev, [blockId]: !prev[blockId] }));
  };

  // Auto-scroll messages
  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  // Resize handler
  useEffect(() => {
    const handleMouseMove = (e: MouseEvent) => {
      if (!isResizing) return;
      const newWidth = window.innerWidth - e.clientX;
      if (newWidth >= 400 && newWidth <= 900) {
        setWidth(newWidth);
      }
    };

    const handleMouseUp = () => {
      setIsResizing(false);
      document.body.style.cursor = "default";
    };

    if (isResizing) {
      window.addEventListener("mousemove", handleMouseMove);
      window.addEventListener("mouseup", handleMouseUp);
      document.body.style.cursor = "col-resize";
    }

    return () => {
      window.removeEventListener("mousemove", handleMouseMove);
      window.removeEventListener("mouseup", handleMouseUp);
    };
  }, [isResizing]);

  // Fetch wiki pages
  useEffect(() => {
    setLoading(true);
    setError(false);
    setPages([]);
    setActivePage(null);

    WikiAPI.getTopicPages(topicId)
      .then((r) => {
        const data = (r.data as WikiPage[]) ?? [];
        setPages(data);
        if (data.length > 0) setActivePage(data[0]);
      })
      .catch(() => setError(true))
      .finally(() => setLoading(false));
  }, [topicId]);

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

    // Kullanıcı mesajına ek göster
    const attachLabel = attachedFile
      ? ` [📎 ${attachedFile.name}]`
      : urlInput.trim()
        ? ` [🔗 ${urlInput.trim()}]`
        : "";
    setMessages((prev) => [...prev, { role: "user", content: userQ + attachLabel }]);
    setIsStreaming(true);
    setMessages((prev) => [...prev, { role: "assistant", content: "" }]);

    try {
      const apiBase =
        (import.meta as unknown as { env: Record<string, string> }).env
          ?.VITE_API_BASE_URL ?? "";

      let response: Response;

      if (copilotMode === "korteks") {
        // Korteks: dosya varsa multipart, URL varsa JSON, yoksa JSON
        if (attachedFile) {
          response = await KorteksAPI.streamWithFile({
            topic: userQ,
            file: attachedFile,
          });
        } else {
          response = await KorteksAPI.stream({
            topic: userQ,
            sourceUrl: urlInput.trim() || undefined,
          });
        }
        // Kullanılan ekleri temizle
        setAttachedFile(null);
        setUrlInput("");
        setShowUrlInput(false);
      } else {
        // Wiki-chat modu — mevcut akış
        response = await fetch(`${apiBase}/api/wiki/${topicId}/chat`, {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            Authorization: `Bearer ${storage.getToken()}`,
          },
          body: JSON.stringify({ question: userQ }),
        });
      }

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
            const str = line.substring(6).replace(/\r$/, "");
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
      initial={{ x: "100%", opacity: 0 }}
      animate={{ x: 0, opacity: 1 }}
      exit={{ x: "100%", opacity: 0 }}
      transition={{ duration: 0.25, ease: "easeOut" }}
      style={{ width: `${width}px` }}
      className="flex-shrink-0 bg-zinc-950 border-l border-zinc-800 flex flex-col h-full relative"
    >
      {/* Resize Handle */}
      <div
        onMouseDown={() => setIsResizing(true)}
        className="absolute left-0 top-0 bottom-0 w-1.5 cursor-col-resize hover:bg-sky-500/20 transition-colors z-30 group"
      >
        <div className="absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 w-0.5 h-8 bg-zinc-800 rounded-full group-hover:bg-sky-500/50"></div>
      </div>

      {/* ─── Header ─── */}
      <div className="px-5 py-4 flex items-start justify-between flex-shrink-0 relative border-b border-zinc-800/60">
        <div className="flex flex-col gap-2 min-w-0 pr-8">
          <div className="flex items-center gap-1.5 text-[11px] text-zinc-500 truncate font-medium tracking-wide">
            <span>Bilgi Haritası</span>
            <span className="text-zinc-700">/</span>
            <span className="text-zinc-300 truncate">
              {activePage?.title || "Topic"}
            </span>
          </div>
          <h3 className="text-lg font-bold text-zinc-100 truncate flex items-center gap-2">
            <BookOpen className="w-4 h-4 text-zinc-400" />
            <span>{activePage?.title || "Wiki"}</span>
          </h3>
        </div>
        <button
          onClick={onClose}
          className="absolute top-4 right-5 text-zinc-500 hover:text-zinc-300 hover:bg-zinc-800 transition-colors duration-150 p-1.5 rounded-lg"
        >
          <X className="w-4 h-4" />
        </button>
      </div>

      {/* ─── Content Area ─── */}
      <div
        className={`flex-1 overflow-y-auto px-5 sidebar-scrollbar relative ${showCopilot ? "pb-[380px]" : "pb-24"}`}
      >
        {loading && (
          <div className="flex items-center justify-center h-40">
            <Loader2 className="w-5 h-5 text-zinc-600 animate-spin" />
          </div>
        )}

        {!loading && error && (
          <div className="text-center py-12">
            <p className="text-sm text-zinc-500 mb-1">
              Wiki henüz oluşturulmadı.
            </p>
            <p className="text-xs text-zinc-600">
              Bu konuda sohbet ettikçe wiki otomatik oluşacak.
            </p>
          </div>
        )}

        {!loading && !error && pages.length === 0 && (
          <div className="text-center py-12">
            <p className="text-sm text-zinc-500 mb-1">Özet bulunamadı.</p>
            <p className="text-xs text-zinc-600">
              Sistem konuyu anladığınızı teyid edince burası dolacaktır.
            </p>
          </div>
        )}

        {!loading && activePage && (
          <div className="max-w-2xl mx-auto pt-4">
            {/* Content Title */}
            <div className="flex items-center justify-between mb-5">
              <h1
                className={`${width > 600 ? "text-xl" : "text-base"} font-bold text-zinc-100 transition-all duration-300`}
              >
                {activePage.title}
              </h1>
            </div>

            {activePage?.blocks && activePage.blocks.length > 0 ? (
              <div className="space-y-3">
                {activePage.blocks.map((block, idx) => {
                  const parsedQuiz = tryParseQuiz(block.content);
                  const isQuiz =
                    block.type === "Quiz" || block.type === "quiz" || !!parsedQuiz;
                  const uniqueId = block.id || idx;
                  const isExpanded = isQuiz
                    ? !!expandedBlocks[uniqueId]
                    : true;

                  const textWithoutJson = parsedQuiz
                    ? block.content.replace(/```(?:json|quiz)?\s*[\s\S]+?\s*```/i, "").replace(/\{[\s\S]*"question"[\s\S]*"options"[\s\S]*"explanation"[\s\S]*\}/i, "")
                    : block.content;

                  return (
                    <div
                      key={uniqueId}
                      className={`rounded-xl border overflow-hidden transition-all duration-300 ${
                        isQuiz
                          ? "border-amber-500/20 bg-amber-500/5 hover:border-amber-500/40"
                          : "border-zinc-800/60 bg-zinc-900/40"
                      }`}
                    >
                      <div
                        className={`px-4 py-3 border-b flex items-center justify-between gap-2 ${isQuiz ? "cursor-pointer select-none hover:bg-amber-500/10" : ""} ${!isExpanded ? "border-transparent" : "border-zinc-800/40"}`}
                        onClick={() => isQuiz && toggleBlock(uniqueId)}
                      >
                        <div className="flex items-center gap-2">
                          <span
                            className={`text-[10px] font-bold uppercase tracking-widest ${isQuiz ? "text-amber-500" : "text-zinc-400"}`}
                          >
                            {isQuiz
                              ? "Pekiştirme Sorusu"
                              : block.title || `Bölüm ${idx + 1}`}
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
                            <div className="px-5 py-4">
                              <div
                                className="prose prose-invert prose-sm max-w-none
                              prose-headings:text-zinc-100 prose-headings:font-semibold
                              prose-p:text-zinc-300 prose-p:leading-relaxed prose-p:text-[14px]
                              prose-strong:text-zinc-100 prose-strong:font-semibold
                              prose-li:text-zinc-300 prose-li:text-[14px]
                              prose-code:text-sky-400 prose-code:bg-sky-500/10 prose-code:px-1.5 prose-code:py-0.5 prose-code:rounded-md prose-code:before:content-none prose-code:after:content-none
                              prose-pre:bg-[#0a0a0a] prose-pre:border prose-pre:border-zinc-800/80 prose-pre:rounded-xl prose-pre:shadow-xl
                            "
                              >
                                {textWithoutJson.trim() && (
                                  <ReactMarkdown remarkPlugins={[remarkGfm]}>
                                    {textWithoutJson}
                                  </ReactMarkdown>
                                )}
                              </div>
                              {parsedQuiz && (
                                <QuizCard
                                  quiz={parsedQuiz}
                                  messageId={block.id}
                                />
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
                className="prose prose-invert prose-sm max-w-none
                  prose-headings:text-zinc-100 prose-headings:font-semibold
                  prose-p:text-zinc-400 prose-p:leading-relaxed prose-p:text-[13px]
                  prose-strong:text-zinc-100
                  prose-code:text-sky-400 prose-code:bg-sky-500/10 prose-code:px-1.5 prose-code:py-0.5 prose-code:rounded-md prose-code:before:content-none prose-code:after:content-none
                  prose-pre:bg-[#0a0a0a] prose-pre:border prose-pre:border-zinc-800/80 prose-pre:rounded-xl
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

      {/* ═══════════════════════════════════════════════════════════
          COPILOT PANEL — Premium çekmece tasarım
          ═══════════════════════════════════════════════════════════ */}
      <div className="absolute bottom-0 left-0 right-0 z-20">
        <AnimatePresence>
          {showCopilot && (
            <motion.div
              initial={{ y: "100%" }}
              animate={{ y: 0 }}
              exit={{ y: "100%" }}
              transition={{ duration: 0.3, ease: [0.16, 1, 0.3, 1] }}
              className="bg-[#0c0c0c] border-t border-zinc-800 flex flex-col"
              style={{ height: "360px" }}
            >
              {/* Copilot Header */}
              <div className="px-4 py-3 border-b border-zinc-800/60 flex items-center justify-between flex-shrink-0">
                <div className="flex items-center gap-3">
                  <div className="flex items-center gap-1.5">
                    {/* Mode Toggle Buttons */}
                    <button
                      onClick={() => setCopilotMode("wiki-chat")}
                      className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition-all duration-200 ${
                        copilotMode === "wiki-chat"
                          ? "bg-zinc-800 text-zinc-100 shadow-sm"
                          : "text-zinc-500 hover:text-zinc-300 hover:bg-zinc-900"
                      }`}
                    >
                      <MessageCircle className="w-3.5 h-3.5" />
                      Belge Ajanı
                    </button>
                    <button
                      onClick={() => setCopilotMode("korteks")}
                      className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition-all duration-200 ${
                        copilotMode === "korteks"
                          ? "bg-zinc-800 text-zinc-100 shadow-sm"
                          : "text-zinc-500 hover:text-zinc-300 hover:bg-zinc-900"
                      }`}
                    >
                      <Globe className="w-3.5 h-3.5" />
                      Korteks Araştırmacı
                    </button>
                  </div>
                </div>
                <button
                  onClick={() => setShowCopilot(false)}
                  className="text-zinc-600 hover:text-zinc-300 p-1 rounded transition-colors"
                >
                  <ChevronDown className="w-4 h-4" />
                </button>
              </div>

              {/* Messages */}
              <div className="flex-1 overflow-y-auto px-4 py-3 space-y-3 sidebar-scrollbar">
                {messages.length === 0 && (
                  <div className="flex flex-col items-center justify-center h-full text-center gap-3 py-6">
                    {copilotMode === "wiki-chat" ? (
                      <>
                        <div className="w-10 h-10 rounded-xl bg-zinc-800/80 flex items-center justify-center">
                          <MessageCircle className="w-5 h-5 text-zinc-500" />
                        </div>
                        <div>
                          <p className="text-sm text-zinc-400 font-medium">
                            Belge Ajanı
                          </p>
                          <p className="text-xs text-zinc-600 mt-1 max-w-[240px]">
                            Bu doküman hakkında sorularınızı sorabilirsiniz.
                            Ajan sadece belgedeki bilgilerden yanıt verir.
                          </p>
                        </div>
                      </>
                    ) : (
                      <>
                        <div className="w-10 h-10 rounded-xl bg-zinc-800/80 flex items-center justify-center relative">
                          <Sparkles className="w-5 h-5 text-amber-500" />
                          <span className="absolute -top-1 -right-1 w-2.5 h-2.5 bg-amber-500 rounded-full animate-pulse" />
                        </div>
                        <div>
                          <p className="text-sm text-zinc-300 font-semibold">
                            Korteks Araştırmacı
                          </p>
                          <p className="text-xs text-zinc-600 mt-1 max-w-[260px]">
                            İnternette derin araştırma yapar. Sorduğunuz konuyu
                            web'de arayıp güncel kaynaklardan sentezlenmiş bir
                            rapor sunar.
                          </p>
                        </div>
                      </>
                    )}
                  </div>
                )}

                {messages.map((msg, i) => (
                  <div
                    key={i}
                    className={`flex ${msg.role === "user" ? "justify-end" : "justify-start"}`}
                  >
                    <div
                      className={`max-w-[88%] rounded-xl px-3.5 py-2.5 text-[13px] leading-relaxed ${
                        msg.role === "user"
                          ? "bg-zinc-800 text-zinc-100 border border-zinc-700/40"
                          : "bg-zinc-900/60 text-zinc-300 border border-zinc-800/60"
                      }`}
                    >
                      <div className="prose prose-invert prose-sm max-w-none prose-p:my-1 prose-p:text-[13px] prose-headings:text-sm prose-headings:mb-1 prose-headings:mt-2 prose-li:text-[13px] prose-code:text-sky-400 prose-code:text-xs">
                        <ReactMarkdown remarkPlugins={[remarkGfm]}>
                          {msg.content || "…"}
                        </ReactMarkdown>
                      </div>
                    </div>
                  </div>
                ))}

                {isStreaming && (
                  <div className="flex items-center gap-2.5 pl-1">
                    <div className="flex gap-1">
                      <span className="w-1.5 h-1.5 bg-zinc-500 rounded-full animate-bounce [animation-delay:0ms]" />
                      <span className="w-1.5 h-1.5 bg-zinc-500 rounded-full animate-bounce [animation-delay:150ms]" />
                      <span className="w-1.5 h-1.5 bg-zinc-500 rounded-full animate-bounce [animation-delay:300ms]" />
                    </div>
                    <span className="text-[11px] text-zinc-600">
                      {copilotMode === "korteks"
                        ? "Korteks araştırıyor..."
                        : "Ajan düşünüyor..."}
                    </span>
                  </div>
                )}
                <div ref={messagesEndRef} />
              </div>

              {/* Input */}
              <div className="px-3 py-2.5 border-t border-zinc-800/60 flex-shrink-0">
                {/* Korteks: Dosya + URL ek göstergeleri */}
                {copilotMode === "korteks" && (attachedFile || urlInput.trim()) && (
                  <div className="flex items-center gap-2 mb-2">
                    {attachedFile && (
                      <span className="flex items-center gap-1 text-[11px] text-amber-400 bg-amber-950/40 border border-amber-800/40 rounded px-2 py-0.5">
                        <Paperclip className="w-3 h-3" />
                        {attachedFile.name}
                        <button onClick={() => setAttachedFile(null)} className="ml-1 hover:text-amber-200">×</button>
                      </span>
                    )}
                    {urlInput.trim() && !attachedFile && (
                      <span className="flex items-center gap-1 text-[11px] text-sky-400 bg-sky-950/40 border border-sky-800/40 rounded px-2 py-0.5 max-w-[200px] truncate">
                        <Link className="w-3 h-3 flex-shrink-0" />
                        <span className="truncate">{urlInput}</span>
                        <button onClick={() => { setUrlInput(""); setShowUrlInput(false); }} className="ml-1 hover:text-sky-200">×</button>
                      </span>
                    )}
                  </div>
                )}

                {/* Korteks: URL input alanı */}
                {copilotMode === "korteks" && showUrlInput && (
                  <input
                    type="url"
                    value={urlInput}
                    onChange={(e) => setUrlInput(e.target.value)}
                    placeholder="https://... URL yapıştırın"
                    className="w-full mb-2 bg-zinc-900 border border-sky-800/60 focus:border-sky-600 rounded-lg px-3 py-1.5 text-xs text-zinc-200 placeholder-zinc-600 outline-none transition-colors"
                  />
                )}

                <div className="flex items-center gap-2">
                  {/* Korteks modunda ataç + URL butonları */}
                  {copilotMode === "korteks" && (
                    <>
                      <input
                        ref={fileInputRef}
                        type="file"
                        accept=".pdf,.txt,.md"
                        className="hidden"
                        onChange={(e) => {
                          const f = e.target.files?.[0];
                          if (f) { setAttachedFile(f); setUrlInput(""); setShowUrlInput(false); }
                          e.target.value = "";
                        }}
                      />
                      <button
                        onClick={() => fileInputRef.current?.click()}
                        title="PDF / TXT / MD yükle"
                        className={`p-1.5 rounded-lg transition-colors ${attachedFile ? "text-amber-400 bg-amber-950/40" : "text-zinc-600 hover:text-zinc-300 hover:bg-zinc-800"}`}
                      >
                        <Paperclip className="w-4 h-4" />
                      </button>
                      <button
                        onClick={() => { setShowUrlInput((v) => !v); setAttachedFile(null); }}
                        title="URL ekle"
                        className={`p-1.5 rounded-lg transition-colors ${showUrlInput ? "text-sky-400 bg-sky-950/40" : "text-zinc-600 hover:text-zinc-300 hover:bg-zinc-800"}`}
                      >
                        <Link className="w-4 h-4" />
                      </button>
                    </>
                  )}

                  <input
                    type="text"
                    value={input}
                    onChange={(e) => setInput(e.target.value)}
                    onKeyDown={(e) => e.key === "Enter" && handleSend()}
                    placeholder={
                      copilotMode === "korteks"
                        ? "Araştırma konusu yazın..."
                        : "Belge hakkında soru sorun..."
                    }
                    className="flex-1 bg-zinc-900 border border-zinc-800 focus:border-zinc-600 rounded-lg px-3 py-2 text-sm text-zinc-200 placeholder-zinc-600 outline-none transition-colors"
                  />
                  <button
                    onClick={handleSend}
                    disabled={isStreaming || !input.trim()}
                    className="p-2 rounded-lg bg-zinc-100 hover:bg-white text-zinc-950 transition-all duration-200 disabled:opacity-30 disabled:hover:bg-zinc-100"
                  >
                    <Send className="w-4 h-4" />
                  </button>
                </div>
              </div>
            </motion.div>
          )}
        </AnimatePresence>

        {/* ─── FAB Button ─── */}
        {!showCopilot && (
          <div className="px-4 py-3 border-t border-zinc-800/40 bg-zinc-950/95 backdrop-blur-sm">
            <button
              onClick={() => setShowCopilot(true)}
              className="w-full flex items-center justify-center gap-2.5 py-2.5 px-4 rounded-xl bg-zinc-900 hover:bg-zinc-800 border border-zinc-800 hover:border-zinc-700 transition-all duration-200 group"
            >
              <Sparkles className="w-4 h-4 text-amber-500 group-hover:text-amber-400 transition-colors" />
              <span className="text-sm text-zinc-300 font-medium group-hover:text-zinc-100 transition-colors">
                Orka'ya Sor
              </span>
            </button>
          </div>
        )}
      </div>
    </motion.div>
  );
}
