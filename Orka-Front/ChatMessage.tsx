/*
 * Design: Agent-style chat messages.
 * User: right-aligned compact bubble.
 * AI: left-aligned card with subtle border, avatar header, markdown body.
 * Code blocks: copy button top-right, language label top-left.
 * Clear visual separation between messages.
 */

import { useState, useCallback } from "react";
import { motion } from "framer-motion";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { Check, Copy } from "lucide-react";
import type { ChatMessage as ChatMessageType } from "@/lib/types";
import QuizCard from "./QuizCard";
import OrcaLogo from "./OrcaLogo";

interface ChatMessageProps {
  message: ChatMessageType;
}

function formatTime(date: Date): string {
  return date.toLocaleTimeString("tr-TR", {
    hour: "2-digit",
    minute: "2-digit",
  });
}

/* Code block with copy button and language label */
function CodeBlock({
  children,
  className,
}: {
  children: React.ReactNode;
  className?: string;
}) {
  const [copied, setCopied] = useState(false);
  const language = className?.replace("language-", "") || "";
  const codeText =
    typeof children === "string"
      ? children
      : String(children).replace(/\n$/, "");

  const handleCopy = useCallback(() => {
    navigator.clipboard.writeText(codeText).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  }, [codeText]);

  return (
    <div className="relative group my-3 rounded-lg overflow-hidden border border-zinc-800 bg-zinc-950">
      {/* Header bar */}
      <div className="flex items-center justify-between px-3 py-1.5 bg-zinc-900/80 border-b border-zinc-800">
        <span className="text-[10px] font-mono text-zinc-500 uppercase tracking-wider">
          {language || "code"}
        </span>
        <button
          onClick={handleCopy}
          className="flex items-center gap-1 text-[10px] text-zinc-500 hover:text-zinc-300 transition-colors duration-150"
        >
          {copied ? (
            <>
              <Check className="w-3 h-3 text-green-500" />
              <span className="text-green-500">Kopyalandı</span>
            </>
          ) : (
            <>
              <Copy className="w-3 h-3" />
              <span>Kopyala</span>
            </>
          )}
        </button>
      </div>
      {/* Code content */}
      <pre className="!m-0 !border-0 !rounded-none overflow-x-auto p-3">
        <code className={`text-xs leading-relaxed text-zinc-300 ${className || ""}`}>
          {children}
        </code>
      </pre>
    </div>
  );
}

/* Inline code */
function InlineCode({ children }: { children: React.ReactNode }) {
  return (
    <code className="text-zinc-300 bg-zinc-800 px-1.5 py-0.5 rounded text-xs font-mono">
      {children}
    </code>
  );
}

export default function ChatMessage({ message }: ChatMessageProps) {
  const isUser = message.role === "user";

  return (
    <motion.div
      initial={{ opacity: 0, y: 6 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.2, ease: "easeOut" }}
      className="py-3"
    >
      {isUser ? (
        /* User Message — compact right-aligned bubble */
        <div className="flex justify-end items-end gap-2">
          <div className="flex flex-col items-end max-w-lg">
            <div className="bg-zinc-800 rounded-2xl rounded-br-sm px-4 py-2.5">
              <p className="text-sm text-zinc-100 leading-relaxed whitespace-pre-wrap">
                {message.content}
              </p>
            </div>
            <span className="text-[10px] text-zinc-600 mt-1 mr-1">
              {formatTime(message.timestamp)}
            </span>
          </div>
        </div>
      ) : (
        /* AI Message — card with border, avatar header, markdown body */
        <div className="flex items-start gap-3 max-w-full">
          {/* Avatar */}
          <div className="flex-shrink-0 w-7 h-7 rounded-full bg-zinc-800 border border-zinc-700/50 flex items-center justify-center mt-1">
            <OrcaLogo className="w-3.5 h-3.5 text-zinc-300" />
          </div>

          {/* Card */}
          <div className="flex-1 min-w-0">
            {/* Header */}
            <div className="flex items-center gap-2 mb-2">
              <span className="text-xs font-medium text-zinc-300">Orka AI</span>
              <span className="text-[10px] text-zinc-600">
                {formatTime(message.timestamp)}
              </span>
            </div>

            {/* Content Card */}
            <div className="bg-zinc-800/40 border border-zinc-800 rounded-xl px-5 py-4">
              <div
                className="prose prose-invert prose-sm max-w-none
                  prose-headings:text-zinc-100 prose-headings:font-semibold
                  prose-h2:text-base prose-h2:mt-5 prose-h2:mb-2 prose-h2:pb-1.5 prose-h2:border-b prose-h2:border-zinc-800
                  prose-h3:text-sm prose-h3:mt-4 prose-h3:mb-2
                  prose-p:text-zinc-300 prose-p:leading-relaxed prose-p:my-2 prose-p:text-[13px]
                  prose-strong:text-zinc-100
                  prose-li:text-zinc-300 prose-li:my-0.5 prose-li:text-[13px]
                  prose-ul:my-2 prose-ol:my-2
                  prose-a:text-zinc-300 prose-a:underline prose-a:underline-offset-2
                  prose-blockquote:border-l-2 prose-blockquote:border-zinc-600 prose-blockquote:bg-zinc-900/50 prose-blockquote:rounded-r-lg prose-blockquote:py-2 prose-blockquote:px-4 prose-blockquote:text-zinc-400 prose-blockquote:italic prose-blockquote:my-3
                  prose-table:border-collapse prose-table:my-3 prose-table:w-full prose-table:rounded-lg prose-table:overflow-hidden
                  prose-thead:bg-zinc-800/60
                  prose-th:border prose-th:border-zinc-700 prose-th:px-3 prose-th:py-2 prose-th:text-left prose-th:text-[11px] prose-th:font-semibold prose-th:text-zinc-200 prose-th:uppercase prose-th:tracking-wider
                  prose-td:border prose-td:border-zinc-800 prose-td:px-3 prose-td:py-2 prose-td:text-xs prose-td:text-zinc-400
                  prose-pre:!bg-transparent prose-pre:!border-0 prose-pre:!p-0 prose-pre:!m-0
                "
              >
                <ReactMarkdown
                  remarkPlugins={[remarkGfm]}
                  components={{
                    // Override code blocks with copy button
                    code({ className, children, ...props }) {
                      const isBlock =
                        className?.startsWith("language-") ||
                        (typeof children === "string" && children.includes("\n"));
                      if (isBlock) {
                        return (
                          <CodeBlock className={className}>{children}</CodeBlock>
                        );
                      }
                      return <InlineCode>{children}</InlineCode>;
                    },
                    // Override pre to pass through (CodeBlock handles it)
                    pre({ children }) {
                      return <>{children}</>;
                    },
                  }}
                >
                  {message.content}
                </ReactMarkdown>
              </div>
            </div>

            {/* Quiz Card (outside the text card) */}
            {message.type === "quiz" && message.quiz && (
              <div className="mt-3">
                <QuizCard quiz={message.quiz} messageId={message.id} />
              </div>
            )}
          </div>
        </div>
      )}
    </motion.div>
  );
}
