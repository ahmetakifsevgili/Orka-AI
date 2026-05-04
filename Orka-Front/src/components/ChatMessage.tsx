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
import { Check, Copy, BookOpen, CheckCircle, Volume2, Bookmark, BookmarkCheck } from "lucide-react";
import toast from "react-hot-toast";
import type { ChatMessage as ChatMessageType } from "@/lib/types";
import { tryParseQuiz } from "@/lib/quizParser";
import { BookmarksAPI } from "@/services/api";
import QuizCard from "./QuizCard";
import OrcaLogo from "./OrcaLogo";
import ClassroomAudioPlayer from "./ClassroomAudioPlayer";

interface ChatMessageProps {
  message: ChatMessageType;
  topicId?: string;
  sessionId?: string;
  /** QuizCard'dan gelen cevap metni; ChatPanel backend'e iletir. */
  onSubmitAnswer?: (text: string) => void;
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
    <code className="text-[#2d5870] bg-[#eaf4f7] border border-[#dcecf3] px-1.5 py-0.5 rounded-md text-[13px] font-mono">
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
      securityLevel: "loose",
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

function MermaidBlock({ code }: { code: string }) {
  const ref = useRef<HTMLDivElement>(null);
  const idRef = useRef("m_" + Math.random().toString(36).slice(2, 9));

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const m = await getMermaid();
        const { svg } = await m.render(idRef.current, code.trim());
        if (!cancelled && ref.current) ref.current.innerHTML = svg;
      } catch (err) {
        if (!cancelled && ref.current) {
          ref.current.innerHTML = `<pre class="text-xs text-amber-400 p-3">Mermaid hata: ${(err as Error).message}</pre>`;
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [code]);

  return (
    <div
      ref={ref}
      className="my-4 p-4 rounded-xl bg-[#f7f9fa] border border-[#dcecf3] overflow-x-auto shadow-sm"
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
  if (!href || !/^https?:\/\//i.test(href)) {
    return <a href={href}>{children}</a>;
  }
  let host = "";
  try {
    host = new URL(href).hostname.replace(/^www\./, "");
  } catch {
    host = href;
  }
  return (
    <a
      href={href}
      target="_blank"
      rel="noopener noreferrer"
      title={href}
      className="inline-flex items-center gap-1 text-emerald-400 hover:text-emerald-300 underline decoration-emerald-500/40 decoration-dotted underline-offset-2 transition"
    >
      {children}
      <span
        className="inline-flex items-center gap-1 ml-0.5 px-1.5 py-0.5 rounded-full bg-emerald-500/10 border border-emerald-500/20 text-[10px] font-mono text-emerald-300/80 align-text-bottom"
        aria-hidden="true"
      >
        <img
          src={`https://www.google.com/s2/favicons?domain=${host}&sz=16`}
          alt=""
          className="w-3 h-3 rounded-sm"
          loading="lazy"
        />
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

// ── Main component ─────────────────────────────────────────────────────────

function ChatMessageInner({ message, topicId, sessionId, onSubmitAnswer, userName = "Sen", onOpenWiki, onOpenIDE }: ChatMessageProps) {
  const isUser = message.role === "user";
  const isTopicComplete = message.type === "topic_complete";

  // Hooks must always be called — conditional returns happen after
  const [displayedContent, setDisplayedContent] = useState(isUser ? message.content : "");
  const [audioOpen, setAudioOpen] = useState(false);
  const [bookmarked, setBookmarked] = useState(false);
  const [bookmarkBusy, setBookmarkBusy] = useState(false);

  const isPersistedMessage = !message.id.startsWith("local-");

  const handleBookmark = useCallback(async () => {
    if (!isPersistedMessage || bookmarkBusy) return;
    setBookmarkBusy(true);
    try {
      const result = await BookmarksAPI.create({ messageId: message.id });
      setBookmarked(true);
      toast.success(result.alreadyExisted ? "Zaten kayıtlıydı." : "Mesaj kaydedildi.");
    } catch (err: unknown) {
      console.error("[ChatMessage] Bookmark create failed:", err);
      toast.error("Mesaj kaydedilemedi.");
    } finally {
      setBookmarkBusy(false);
    }
  }, [isPersistedMessage, bookmarkBusy, message.id]);

  // Resolve quiz data without strict type checking (AI often forgets type indicator but sends json)
  const quizData = message.quiz ?? tryParseQuiz(message.content);

  // Track previous isStreaming to detect the streaming→done transition
  const prevStreamingRef = useRef(message.isStreaming);

  // Sync displayed content with message content — no typewriter animation for performance
  useEffect(() => {
    prevStreamingRef.current = message.isStreaming;
    setDisplayedContent(message.content);
  }, [message.content, message.isStreaming]);

  // ── Konu Tamamlama Kartı ───────────────────────────────────────────────
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
              <span className="text-xs font-medium text-[#344054]">{userName}</span>
              <span className="text-[10px] text-[#98a2b3]">
                {formatTime(message.timestamp)}
              </span>
            </div>
            <div className="bg-[#dcecf3]/80 border border-[#9ec7d9]/45 rounded-2xl rounded-tr-sm px-4 py-3 shadow-sm">
              <p className="text-[15px] text-[#172033] leading-relaxed whitespace-pre-wrap">
                {message.content}
              </p>
            </div>
          </div>
          <div className="flex-shrink-0 w-7 h-7 rounded-full bg-white/75 border border-[#526d82]/15 flex items-center justify-center mt-1 overflow-hidden">
            <img src="https://api.dicebear.com/7.x/notionists/svg?seed=Felix&backgroundColor=transparent" alt="User" className="w-full h-full object-cover" />
          </div>
        </div>
      ) : displayedContent.length === 0 ? null : (
        // ── AI mesajı (Boşken avatar çizilmez çünkü isThinking animasyonu dönüyor) ──
        <div className="flex items-start gap-3 max-w-full">
          {/* Avatar */}
          <div className="flex-shrink-0 w-7 h-7 rounded-full bg-white/75 border border-[#526d82]/15 shadow-sm flex items-center justify-center mt-1">
            <OrcaLogo className="w-3.5 h-3.5 text-[#344054]" />
          </div>

          <div className="flex-1 min-w-0">
            {/* Header */}
            <div className="flex items-center gap-2 mb-2">
              <span className="text-xs font-medium text-[#344054]">Orka AI</span>
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
                <div className="bg-white/70 border border-[#526d82]/14 rounded-[1.25rem] px-5 py-4 mb-3 shadow-[0_14px_38px_rgba(66,91,112,0.09)] backdrop-blur-xl">
                  <div
                    className="prose max-w-none
                      prose-headings:text-[#172033] prose-headings:font-semibold
                      prose-h2:text-[17px] prose-h2:mt-5 prose-h2:mb-2 prose-h2:pb-1.5 prose-h2:border-b prose-h2:border-[#526d82]/10
                      prose-h3:text-[15px] prose-h3:mt-4 prose-h3:mb-2
                      prose-p:text-[#344054] prose-p:leading-relaxed prose-p:my-2.5 prose-p:text-[15px]
                      prose-strong:text-[#172033]
                      prose-li:text-[#344054] prose-li:my-1 prose-li:text-[15px]
                      prose-ul:my-2.5 prose-ol:my-2.5
                      prose-a:text-[#2d5870] prose-a:underline prose-a:underline-offset-2
                      prose-blockquote:border-l-4 prose-blockquote:border-[#9ec7d9] prose-blockquote:bg-[#f7f9fa] prose-blockquote:rounded-r-xl prose-blockquote:py-3 prose-blockquote:px-5 prose-blockquote:text-[#5f6f7b] prose-blockquote:italic prose-blockquote:my-4 prose-blockquote:shadow-sm
                      prose-table:border-collapse prose-table:my-4 prose-table:w-full prose-table:rounded-xl prose-table:overflow-hidden prose-table:shadow-sm prose-table:border prose-table:border-[#dcecf3]
                      prose-thead:bg-[#eaf4f7]
                      prose-th:border-b prose-th:border-[#dcecf3] prose-th:px-4 prose-th:py-3 prose-th:text-left prose-th:text-[12px] prose-th:font-bold prose-th:text-[#2d5870] prose-th:uppercase prose-th:tracking-wider
                      prose-td:border-b prose-td:border-[#eef1f3] prose-td:px-4 prose-td:py-3 prose-td:text-[13px] prose-td:text-[#344054]
                      prose-pre:!bg-transparent prose-pre:!border-0 prose-pre:!p-0 prose-pre:!m-0
                    "
                  >
                    <ReactMarkdown
                      remarkPlugins={[remarkGfm, remarkMath]}
                      rehypePlugins={[rehypeKatex]}
                      components={{
                        code({ className, children }) {
                          const langMatch = /language-(\w+)/.exec(className || "");
                          const lang = langMatch?.[1];

                          // V4: mermaid bloğu özel render
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
                          // Pollinations + diğer görselleri sar — yüklenmezse fallback göster
                          return (
                            <img
                              src={src}
                              alt={alt || ""}
                              loading="lazy"
                              className="my-4 rounded-xl border border-[#dcecf3] max-w-full bg-[#f7f9fa]"
                              onError={(e) => {
                                const target = e.currentTarget;
                                target.onerror = null;
                                target.style.display = "none";
                                const fallback = document.createElement("div");
                                fallback.className = "my-4 rounded-xl border border-[#dcecf3] bg-[#f7f9fa] px-4 py-3 text-xs text-[#98a2b3] flex items-center gap-2";
                                fallback.innerHTML = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="8.5" cy="8.5" r="1.5"/><path d="m21 15-5-5L5 21"/></svg> Görsel yüklenemedi`;
                                target.parentNode?.insertBefore(fallback, target.nextSibling);
                              }}
                            />
                          );
                        },
                      }}
                    >
                      {withSourceLinks(cleanedText)}
                    </ReactMarkdown>
                  </div>
                </div>
              );
            })()}

            {/* Render the QuizCard if quiz data exists */}
            {quizData && (
              <QuizCard
                quiz={quizData}
                messageId={message.id}
                topicId={topicId}
                sessionId={sessionId}
                onSubmitAnswer={onSubmitAnswer}
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
            {!quizData && displayedContent.trim().length > 40 && !message.isStreaming && (
              <div className="mt-2 flex items-center gap-2">
                <button
                  onClick={() => setAudioOpen(true)}
                  className="flex items-center gap-1.5 px-2.5 py-1 rounded-md text-[11px] text-zinc-500 hover:text-emerald-400 hover:bg-emerald-500/10 transition border border-transparent hover:border-emerald-500/20"
                  title="Bu mesajı sesli dinle"
                >
                  <Volume2 className="w-3 h-3" />
                  Sesli dinle
                </button>
                {isPersistedMessage && (
                  <button
                    onClick={handleBookmark}
                    disabled={bookmarkBusy || bookmarked}
                    className={`flex items-center gap-1.5 px-2.5 py-1 rounded-md text-[11px] transition border border-transparent ${
                      bookmarked
                        ? "text-amber-500 bg-amber-500/10 border-amber-500/20"
                        : "text-zinc-500 hover:text-amber-500 hover:bg-amber-500/10 hover:border-amber-500/20"
                    } ${bookmarkBusy ? "opacity-60 cursor-wait" : ""}`}
                    title={bookmarked ? "Kaydedildi" : "Bu mesajı kaydet"}
                  >
                    {bookmarked ? <BookmarkCheck className="w-3 h-3" /> : <Bookmark className="w-3 h-3" />}
                    {bookmarked ? "Kaydedildi" : "Kaydet"}
                  </button>
                )}
              </div>
            )}
          </div>
        </div>
      )}

      {audioOpen && (
        <ClassroomAudioPlayer
          text={displayedContent}
          topicId={topicId}
          sessionId={sessionId}
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
    prev.message.isStreaming === next.message.isStreaming &&
    prev.message.type === next.message.type &&
    prev.topicId === next.topicId &&
    prev.sessionId === next.sessionId &&
    prev.userName === next.userName
  );
});
