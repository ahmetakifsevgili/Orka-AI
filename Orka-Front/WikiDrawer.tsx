/*
 * Design: Full-width wiki panel with breadcrumbs, rich tables,
 * code blocks, key points, edit button, last updated info,
 * and Orka AI chat bubble for contextual help.
 */

import { useState, useRef, useEffect } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { X, Pencil, MessageCircle, Send, ChevronRight } from "lucide-react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import type { WikiContent } from "@/lib/types";

interface WikiDrawerProps {
  wiki: WikiContent;
  onClose: () => void;
}

export default function WikiDrawer({ wiki, onClose }: WikiDrawerProps) {
  const [chatOpen, setChatOpen] = useState(false);
  const [chatMessages, setChatMessages] = useState<
    { role: "user" | "ai"; text: string }[]
  >([]);
  const [chatInput, setChatInput] = useState("");
  const chatEndRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [chatMessages]);

  const handleChatSend = () => {
    if (!chatInput.trim()) return;
    const userMsg = chatInput.trim();
    setChatMessages((prev) => [...prev, { role: "user", text: userMsg }]);
    setChatInput("");

    // Simulate AI response about wiki content
    setTimeout(() => {
      setChatMessages((prev) => [
        ...prev,
        {
          role: "ai",
          text: `Based on the "${wiki.title}" content: This is a great question! The key concept here relates to ${wiki.keyPoints[0]?.toLowerCase() || "the fundamentals we covered"}. Would you like me to elaborate on any specific part?`,
        },
      ]);
    }, 1200);
  };

  // Build breadcrumb from wiki data
  const breadcrumbs = ["Home", "Wiki", "AI Concepts", wiki.title];

  return (
    <motion.div
      initial={{ x: "100%", opacity: 0 }}
      animate={{ x: 0, opacity: 1 }}
      exit={{ x: "100%", opacity: 0 }}
      transition={{ duration: 0.25, ease: "easeOut" }}
      className="w-[480px] flex-shrink-0 bg-zinc-950 border-l border-zinc-800 flex flex-col h-full relative"
    >
      {/* Header with close */}
      <div className="px-5 py-4 border-b border-zinc-800 flex items-center justify-between flex-shrink-0">
        <h3 className="text-sm font-semibold text-zinc-100 truncate pr-4">
          {wiki.title}
        </h3>
        <button
          onClick={onClose}
          className="text-zinc-500 hover:text-zinc-300 transition-colors duration-150 flex-shrink-0 p-1"
        >
          <X className="w-4 h-4" />
        </button>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-y-auto px-6 py-5">
        {/* Breadcrumbs */}
        <nav className="flex items-center gap-1.5 mb-5 flex-wrap">
          {breadcrumbs.map((crumb, i) => (
            <span key={i} className="flex items-center gap-1.5">
              {i > 0 && (
                <span className="text-zinc-700">/</span>
              )}
              <span
                className={`text-xs ${
                  i === breadcrumbs.length - 1
                    ? "text-zinc-300 font-medium"
                    : "text-zinc-500 hover:text-zinc-300 cursor-pointer transition-colors duration-150"
                }`}
              >
                {crumb}
              </span>
            </span>
          ))}
        </nav>

        {/* Wiki Title */}
        <h1 className="text-xl font-bold text-zinc-100 mb-5 leading-tight">
          AI Knowledge Wiki: {wiki.title}
        </h1>

        {/* Markdown Content with enhanced table styling */}
        <div className="wiki-content">
          <ReactMarkdown
            remarkPlugins={[remarkGfm]}
            components={{
              h2: ({ children }) => (
                <h2 className="text-lg font-bold text-zinc-100 mt-6 mb-3">
                  {children}
                </h2>
              ),
              h3: ({ children }) => (
                <h3 className="text-sm font-semibold text-zinc-200 mt-5 mb-2">
                  {children}
                </h3>
              ),
              p: ({ children }) => (
                <p className="text-sm text-zinc-300 leading-relaxed mb-3">
                  {children}
                </p>
              ),
              strong: ({ children }) => (
                <strong className="text-zinc-100 font-semibold">{children}</strong>
              ),
              ul: ({ children }) => (
                <ul className="space-y-1.5 mb-4 ml-1">{children}</ul>
              ),
              ol: ({ children }) => (
                <ol className="space-y-1.5 mb-4 ml-1 list-decimal list-inside">{children}</ol>
              ),
              li: ({ children }) => (
                <li className="text-sm text-zinc-300 leading-relaxed flex gap-2">
                  <span className="text-zinc-600 flex-shrink-0">•</span>
                  <span>{children}</span>
                </li>
              ),
              code: ({ className, children }) => {
                const isBlock = className?.includes("language-");
                if (isBlock) {
                  return (
                    <div className="relative my-4 rounded-lg overflow-hidden border border-zinc-800">
                      <div className="flex items-center justify-between px-4 py-2 bg-zinc-800/50 border-b border-zinc-800">
                        <span className="text-[10px] text-zinc-500 font-mono uppercase">
                          {className?.replace("language-", "") || "code"}
                        </span>
                        <button className="text-[10px] text-zinc-500 hover:text-zinc-300 transition-colors">
                          Copy
                        </button>
                      </div>
                      <pre className="p-4 bg-zinc-900/80 overflow-x-auto">
                        <code className="text-[13px] leading-relaxed text-zinc-300 font-mono">
                          {children}
                        </code>
                      </pre>
                    </div>
                  );
                }
                return (
                  <code className="text-[13px] bg-zinc-800 text-zinc-200 px-1.5 py-0.5 rounded font-mono">
                    {children}
                  </code>
                );
              },
              pre: ({ children }) => <>{children}</>,
              table: ({ children }) => (
                <div className="my-4 rounded-lg overflow-hidden border border-zinc-700">
                  <table className="w-full text-sm">{children}</table>
                </div>
              ),
              thead: ({ children }) => (
                <thead className="bg-zinc-800/80 border-b border-zinc-700">
                  {children}
                </thead>
              ),
              tbody: ({ children }) => (
                <tbody className="divide-y divide-zinc-800">{children}</tbody>
              ),
              tr: ({ children }) => (
                <tr className="hover:bg-zinc-800/30 transition-colors duration-100">
                  {children}
                </tr>
              ),
              th: ({ children }) => (
                <th className="px-4 py-2.5 text-left text-xs font-semibold text-zinc-200 uppercase tracking-wider">
                  {children}
                </th>
              ),
              td: ({ children }) => (
                <td className="px-4 py-2.5 text-sm text-zinc-300 border-l border-zinc-800 first:border-l-0">
                  {children}
                </td>
              ),
              blockquote: ({ children }) => (
                <blockquote className="border-l-2 border-zinc-700 pl-4 my-4 text-sm text-zinc-400 italic">
                  {children}
                </blockquote>
              ),
            }}
          >
            {wiki.content}
          </ReactMarkdown>
        </div>

        {/* Key Points */}
        {wiki.keyPoints.length > 0 && (
          <div className="mt-6 p-4 rounded-lg bg-zinc-900/50 border border-zinc-800">
            <h4 className="text-xs font-semibold text-zinc-300 uppercase tracking-wider mb-3">
              Key Points
            </h4>
            <ul className="space-y-2">
              {wiki.keyPoints.map((point, i) => (
                <li
                  key={i}
                  className="text-sm text-zinc-400 leading-relaxed flex gap-2.5"
                >
                  <span className="text-zinc-600 font-bold flex-shrink-0">•</span>
                  <span><strong className="text-zinc-300">{point.split("—")[0]?.split("–")[0]}</strong>{point.includes("—") ? " —" + point.split("—")[1] : point.includes("–") ? " –" + point.split("–")[1] : ""}</span>
                </li>
              ))}
            </ul>
          </div>
        )}

        {/* Last Updated + Edit */}
        <div className="mt-5 flex items-center justify-between">
          <p className="text-[11px] text-zinc-600">
            Last updated: {wiki.lastUpdated.toLocaleDateString("en-US", {
              month: "short",
              day: "numeric",
              year: "numeric",
            })}{" "}
            by AI Platform Team
          </p>
          <button className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg border border-zinc-700 text-xs text-zinc-400 hover:text-zinc-200 hover:border-zinc-600 transition-colors duration-150">
            <Pencil className="w-3 h-3" />
            Edit Page
          </button>
        </div>
      </div>

      {/* Orka AI Chat Bubble */}
      <AnimatePresence>
        {chatOpen && (
          <motion.div
            initial={{ opacity: 0, y: 20, scale: 0.95 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 20, scale: 0.95 }}
            transition={{ duration: 0.2, ease: "easeOut" }}
            className="absolute bottom-16 right-4 w-72 bg-zinc-900 border border-zinc-700 rounded-xl shadow-2xl overflow-hidden z-50"
          >
            {/* Chat Header */}
            <div className="px-4 py-3 border-b border-zinc-800 flex items-center justify-between">
              <div className="flex items-center gap-2">
                <div className="w-6 h-6 rounded-md bg-zinc-800 flex items-center justify-center">
                  <span className="text-[9px] font-bold text-zinc-300">AI</span>
                </div>
                <div>
                  <p className="text-xs font-medium text-zinc-200">Orka Assistant</p>
                  <p className="text-[10px] text-zinc-500">Ask about this wiki</p>
                </div>
              </div>
              <button
                onClick={() => setChatOpen(false)}
                className="text-zinc-500 hover:text-zinc-300 transition-colors"
              >
                <X className="w-3.5 h-3.5" />
              </button>
            </div>

            {/* Chat Messages */}
            <div className="h-48 overflow-y-auto px-3 py-3 space-y-2.5">
              {chatMessages.length === 0 && (
                <div className="text-center py-6">
                  <p className="text-xs text-zinc-500">
                    Ask me anything about
                  </p>
                  <p className="text-xs text-zinc-400 font-medium mt-0.5">
                    "{wiki.title}"
                  </p>
                </div>
              )}
              {chatMessages.map((msg, i) => (
                <div
                  key={i}
                  className={`flex ${msg.role === "user" ? "justify-end" : "justify-start"}`}
                >
                  <div
                    className={`max-w-[85%] px-3 py-2 rounded-xl text-xs leading-relaxed ${
                      msg.role === "user"
                        ? "bg-zinc-700 text-zinc-100 rounded-br-sm"
                        : "bg-zinc-800 text-zinc-300 rounded-bl-sm"
                    }`}
                  >
                    {msg.text}
                  </div>
                </div>
              ))}
              <div ref={chatEndRef} />
            </div>

            {/* Chat Input */}
            <div className="px-3 py-2.5 border-t border-zinc-800">
              <div className="flex items-center gap-2 bg-zinc-800 rounded-lg px-3 py-2">
                <input
                  value={chatInput}
                  onChange={(e) => setChatInput(e.target.value)}
                  onKeyDown={(e) => e.key === "Enter" && handleChatSend()}
                  placeholder="Ask about this topic..."
                  className="flex-1 bg-transparent text-xs text-zinc-100 placeholder-zinc-500 outline-none"
                />
                <button
                  onClick={handleChatSend}
                  disabled={!chatInput.trim()}
                  className="text-zinc-500 hover:text-zinc-300 transition-colors disabled:opacity-30"
                >
                  <Send className="w-3.5 h-3.5" />
                </button>
              </div>
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Floating Chat Button */}
      <button
        onClick={() => setChatOpen(!chatOpen)}
        className={`absolute bottom-4 right-4 w-10 h-10 rounded-full flex items-center justify-center shadow-lg transition-all duration-200 z-40 ${
          chatOpen
            ? "bg-zinc-700 text-zinc-200"
            : "bg-zinc-800 text-zinc-400 hover:text-zinc-200 hover:bg-zinc-700 border border-zinc-700"
        }`}
        title="Ask Orka AI about this wiki"
      >
        <MessageCircle className="w-4.5 h-4.5" />
      </button>
    </motion.div>
  );
}
