/*
 * ChatMessage — AI ve kullanıcı mesajlarını render eder.
 * AI mesajı "quiz" tipindeyse react-markdown ÇALIŞMAZ; QuizCard gösterilir.
 * Diğer AI mesajları prose-invert + react-markdown + syntax highlighting ile gösterilir.
 */

import { useState, useCallback, useEffect, useRef, memo } from "react";
import { motion } from "framer-motion";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { Prism as SyntaxHighlighter } from "react-syntax-highlighter";
import { vscDarkPlus } from "react-syntax-highlighter/dist/esm/styles/prism";
import { Check, Copy, BookOpen, CheckCircle } from "lucide-react";
import type { ChatMessage as ChatMessageType } from "@/lib/types";
import { tryParseQuiz } from "@/lib/quizParser";
import QuizCard from "./QuizCard";
import OrcaLogo from "./OrcaLogo";

interface ChatMessageProps {
  message: ChatMessageType;
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
    <div className="relative my-4 rounded-xl overflow-hidden border border-zinc-700/60 bg-[#1e1e1e]">
      {/* Header bar */}
      <div className="flex items-center justify-between px-4 py-2 bg-zinc-800/80 border-b border-zinc-700/40">
        <span className="text-[11px] font-mono text-zinc-400 uppercase tracking-wider">
          {language}
        </span>
        <button
          onClick={handleCopy}
          className="flex items-center gap-1.5 text-[11px] text-zinc-400 hover:text-zinc-100 transition-colors duration-150 px-2 py-0.5 rounded hover:bg-zinc-700/50"
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
    <code className="text-zinc-300 bg-zinc-800 px-1.5 py-0.5 rounded text-xs font-mono">
      {children}
    </code>
  );
}

// ── Main component ─────────────────────────────────────────────────────────

function ChatMessageInner({ message, onSubmitAnswer, userName = "Sen", onOpenWiki, onOpenIDE }: ChatMessageProps) {
  const isUser = message.role === "user";
  const isTopicComplete = message.type === "topic_complete";

  // Hooks must always be called — conditional returns happen after
  const [displayedContent, setDisplayedContent] = useState(isUser ? message.content : "");

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
              <span className="text-xs font-medium text-zinc-300">{userName}</span>
              <span className="text-[10px] text-zinc-600">
                {formatTime(message.timestamp)}
              </span>
            </div>
            <div className="bg-zinc-800 rounded-2xl rounded-tr-sm px-4 py-3">
              <p className="text-[15px] text-zinc-100 leading-relaxed whitespace-pre-wrap">
                {message.content}
              </p>
            </div>
          </div>
          <div className="flex-shrink-0 w-7 h-7 rounded-full bg-zinc-700 border border-zinc-600 flex items-center justify-center mt-1 overflow-hidden">
            <img src="https://api.dicebear.com/7.x/notionists/svg?seed=Felix&backgroundColor=transparent" alt="User" className="w-full h-full object-cover" />
          </div>
        </div>
      ) : displayedContent.length === 0 ? null : (
        // ── AI mesajı (Boşken avatar çizilmez çünkü isThinking animasyonu dönüyor) ──
        <div className="flex items-start gap-3 max-w-full">
          {/* Avatar */}
          <div className="flex-shrink-0 w-7 h-7 rounded-full bg-zinc-800 border border-zinc-700/50 flex items-center justify-center mt-1">
            <OrcaLogo className="w-3.5 h-3.5 text-zinc-300" />
          </div>

          <div className="flex-1 min-w-0">
            {/* Header */}
            <div className="flex items-center gap-2 mb-2">
              <span className="text-xs font-medium text-zinc-300">Orka AI</span>
              <span className="text-[10px] text-zinc-600">
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
                <div className="bg-zinc-800/50 border border-zinc-700/60 rounded-xl px-5 py-4 mb-3">
                  <div
                    className="prose prose-invert max-w-none
                      prose-headings:text-zinc-100 prose-headings:font-semibold
                      prose-h2:text-[17px] prose-h2:mt-5 prose-h2:mb-2 prose-h2:pb-1.5 prose-h2:border-b prose-h2:border-zinc-700/60
                      prose-h3:text-[15px] prose-h3:mt-4 prose-h3:mb-2
                      prose-p:text-zinc-200 prose-p:leading-relaxed prose-p:my-2.5 prose-p:text-[15px]
                      prose-strong:text-zinc-100
                      prose-li:text-zinc-200 prose-li:my-1 prose-li:text-[15px]
                      prose-ul:my-2.5 prose-ol:my-2.5
                      prose-a:text-zinc-300 prose-a:underline prose-a:underline-offset-2
                      prose-blockquote:border-l-2 prose-blockquote:border-zinc-600 prose-blockquote:bg-zinc-900/50 prose-blockquote:rounded-r-lg prose-blockquote:py-2 prose-blockquote:px-4 prose-blockquote:text-zinc-400 prose-blockquote:italic prose-blockquote:my-3
                      prose-table:border-collapse prose-table:my-3 prose-table:w-full
                      prose-thead:bg-zinc-800/60
                      prose-th:border prose-th:border-zinc-700 prose-th:px-3 prose-th:py-2 prose-th:text-left prose-th:text-[12px] prose-th:font-semibold prose-th:text-zinc-200 prose-th:uppercase prose-th:tracking-wider
                      prose-td:border prose-td:border-zinc-800 prose-td:px-3 prose-td:py-2 prose-td:text-[13px] prose-td:text-zinc-300
                      prose-pre:!bg-transparent prose-pre:!border-0 prose-pre:!p-0 prose-pre:!m-0
                    "
                  >
                    <ReactMarkdown
                      remarkPlugins={[remarkGfm]}
                      components={{
                        code({ className, children }) {
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
                      }}
                    >
                      {cleanedText}
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
                onSubmitAnswer={onSubmitAnswer}
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
          </div>
        </div>
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
    prev.userName === next.userName
  );
});
