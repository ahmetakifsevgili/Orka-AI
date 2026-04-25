/*
 * ChatMessage — AI ve kullanıcı mesajlarını render eder.
 * AI mesajı "quiz" tipindeyse react-markdown ÇALIŞMAZ; QuizCard gösterilir.
 * Diğer AI mesajları prose-invert + react-markdown + syntax highlighting ile gösterilir.
 */

import { useState, useCallback, useEffect, useRef, memo } from "react";
import { motion } from "framer-motion";
import { BookOpen, CheckCircle } from "lucide-react";
import type { ChatMessage as ChatMessageType } from "@/lib/types";
import { tryParseQuiz } from "@/lib/quizParser";
import QuizCard from "./QuizCard";
import OrcaLogo from "./OrcaLogo";
import MarkdownRender from "./MarkdownRender";
import ResearchToolbar from "./ResearchToolbar";

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

// ChatMessage uses unified MarkdownRender for consistency.

// ── Main component ─────────────────────────────────────────────────────────

function ChatMessageInner({ message, onSubmitAnswer, userName = "Sen", onOpenWiki, onOpenIDE }: ChatMessageProps) {
  const isUser = message.role === "user";
  const isTopicComplete = message.type === "topic_complete";
  const isResearch = message.type === "research";

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
              <p className="text-[13px] soft-text-muted leading-relaxed mb-4">
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
              <span className="text-xs font-medium text-foreground">{userName}</span>
              <span className="text-[10px] soft-text-muted">
                {formatTime(message.timestamp)}
              </span>
            </div>
            <div className="soft-surface border rounded-xl rounded-tr-sm px-4 py-3">
              {message.attachments && message.attachments.length > 0 && (
                <div className="mb-3 grid gap-2">
                  {message.attachments.map((attachment) => (
                    <figure
                      key={`${attachment.url}-${attachment.name}`}
                      className="overflow-hidden rounded-lg border soft-border bg-surface-muted"
                    >
                      <img
                        src={attachment.url}
                        alt={attachment.name}
                        className="max-h-64 w-full object-cover"
                      />
                      <figcaption className="px-2.5 py-1.5 text-[10px] soft-text-muted truncate">
                        {attachment.name}
                      </figcaption>
                    </figure>
                  ))}
                </div>
              )}
              <p className="text-[15px] text-foreground leading-relaxed whitespace-pre-wrap">
                {message.content}
              </p>
            </div>
          </div>
          <div className="flex-shrink-0 w-7 h-7 rounded-full soft-muted border soft-border flex items-center justify-center mt-1 overflow-hidden">
            <img src="https://api.dicebear.com/7.x/notionists/svg?seed=Felix&backgroundColor=transparent" alt="User" className="w-full h-full object-cover" />
          </div>
        </div>
      ) : displayedContent.length === 0 ? null : (
        // ── AI mesajı (Boşken avatar çizilmez çünkü isThinking animasyonu dönüyor) ──
        <div className="flex items-start gap-3 max-w-full">
          {/* Avatar */}
          <div className="flex-shrink-0 w-7 h-7 rounded-full soft-muted border soft-border flex items-center justify-center mt-1">
            <OrcaLogo className="w-3.5 h-3.5 text-foreground" />
          </div>

          <div className="flex-1 min-w-0">
            {/* Header */}
            <div className="flex items-center gap-2 mb-2">
              <span className="text-xs font-medium text-foreground">Orka AI</span>
              <span className="text-[10px] soft-text-muted">
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
                <div className="soft-surface border rounded-xl px-5 py-4 mb-3">
                  <div
                    className="prose prose-orka max-w-none
                      prose-headings:font-semibold
                      prose-h2:text-[17px] prose-h2:mt-5 prose-h2:mb-2 prose-h2:pb-1.5 prose-h2:border-b prose-h2:border-border
                      prose-h3:text-[15px] prose-h3:mt-4 prose-h3:mb-2
                      prose-p:leading-relaxed prose-p:my-2.5 prose-p:text-[15px]
                      prose-li:my-1 prose-li:text-[15px]
                      prose-ul:my-2.5 prose-ol:my-2.5
                      prose-a:underline prose-a:underline-offset-2
                      prose-blockquote:border-l-2 prose-blockquote:border-border prose-blockquote:bg-surface-muted prose-blockquote:rounded-r-lg prose-blockquote:py-2 prose-blockquote:px-4 prose-blockquote:italic prose-blockquote:my-3
                      prose-table:border-collapse prose-table:my-3 prose-table:w-full
                      prose-thead:bg-surface-muted
                      prose-th:border prose-th:border-border prose-th:px-3 prose-th:py-2 prose-th:text-left prose-th:text-[12px] prose-th:font-semibold prose-th:uppercase prose-th:tracking-wider
                      prose-td:border prose-td:border-border prose-td:px-3 prose-td:py-2 prose-td:text-[13px]
                      prose-pre:!bg-transparent prose-pre:!border-0 prose-pre:!p-0 prose-pre:!m-0
                    "
                  >
                    <MarkdownRender>
                      {cleanedText}
                    </MarkdownRender>
                    {isResearch && !message.isStreaming && cleanedText.length > 200 && (
                      <ResearchToolbar content={cleanedText} topic={message.researchTopic} />
                    )}
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
