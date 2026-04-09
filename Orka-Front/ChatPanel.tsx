/*
 * Design: "Sessiz Lüks" — flex-1, bg-zinc-900.
 * Real chatbot feel: distinct message bubbles, timestamps, avatars.
 * Welcome state with quick prompts when empty.
 * Sticky input area at bottom with Raycast-style command feel.
 */

import { useState, useRef, useEffect, useCallback } from "react";
import { Send, Sparkles } from "lucide-react";
import { AnimatePresence, motion } from "framer-motion";
import type { ChatMessage as ChatMessageType, Topic } from "@/lib/types";
import { generateAIResponse, QUICK_PROMPTS, THINKING_STATES } from "@/lib/mockData";
import ChatMessageComponent from "./ChatMessage";
import ThinkingIndicator from "./ThinkingIndicator";
import OrcaLogo from "./OrcaLogo";

interface ChatPanelProps {
  messages: ChatMessageType[];
  setMessages: React.Dispatch<React.SetStateAction<ChatMessageType[]>>;
  topics: Topic[];
  onNewTopic: (topic: Topic) => void;
  ensureConversation: () => string;
}

export default function ChatPanel({
  messages,
  setMessages,
  topics,
  onNewTopic,
  ensureConversation,
}: ChatPanelProps) {
  const [input, setInput] = useState("");
  const [isThinking, setIsThinking] = useState(false);
  const [thinkingState, setThinkingState] = useState("");
  const scrollRef = useRef<HTMLDivElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const scrollToBottom = useCallback(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, []);

  useEffect(() => {
    scrollToBottom();
  }, [messages, isThinking, scrollToBottom]);

  const handleSend = async (text?: string) => {
    const messageText = text || input.trim();
    if (!messageText || isThinking) return;

    // Ensure a conversation exists
    ensureConversation();

    // Add user message
    const userMsg: ChatMessageType = {
      id: `msg-${Date.now()}`,
      role: "user",
      type: "text",
      content: messageText,
      timestamp: new Date(),
    };
    setMessages((prev) => [...prev, userMsg]);
    setInput("");

    // Reset textarea height
    if (textareaRef.current) {
      textareaRef.current.style.height = "auto";
    }

    // Thinking sequence
    setIsThinking(true);
    for (let i = 0; i < THINKING_STATES.length; i++) {
      setThinkingState(THINKING_STATES[i]);
      await new Promise((r) => setTimeout(r, 600));
    }

    // Generate AI response
    const response = generateAIResponse(messageText, topics);

    const aiMsg: ChatMessageType = {
      id: `msg-${Date.now() + 1}`,
      role: "ai",
      type: response.type,
      content: response.content,
      quiz: response.quiz,
      timestamp: new Date(),
    };

    setMessages((prev) => [...prev, aiMsg]);
    setIsThinking(false);

    // Add new topic if generated
    if (response.newTopic) {
      onNewTopic(response.newTopic);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };

  const handleTextareaChange = (e: React.ChangeEvent<HTMLTextAreaElement>) => {
    setInput(e.target.value);
    const el = e.target;
    el.style.height = "auto";
    el.style.height = Math.min(el.scrollHeight, 120) + "px";
  };

  return (
    <div className="flex-1 flex flex-col bg-zinc-900 h-full overflow-hidden">
      {/* Message Scroll Area */}
      <div ref={scrollRef} className="flex-1 overflow-y-auto">
        <div className="max-w-3xl mx-auto w-full px-6 py-8">
          {messages.length === 0 ? (
            <WelcomeState onPromptClick={(prompt) => handleSend(prompt)} />
          ) : (
            <div className="space-y-1">
              {messages.map((msg) => (
                <ChatMessageComponent key={msg.id} message={msg} />
              ))}
            </div>
          )}

          <AnimatePresence>
            {isThinking && (
              <motion.div
                initial={{ opacity: 0, y: 8 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0 }}
                className="flex gap-3 mt-4"
              >
                <div className="flex-shrink-0 w-8 h-8 rounded-full bg-zinc-800 border border-zinc-700 flex items-center justify-center">
                  <OrcaLogo className="w-4 h-4 text-zinc-300" />
                </div>
                <div className="pt-1.5">
                  <ThinkingIndicator state={thinkingState} />
                </div>
              </motion.div>
            )}
          </AnimatePresence>
        </div>
      </div>

      {/* Sticky Input Area */}
      <div className="flex-shrink-0 border-t border-zinc-800/50 bg-zinc-900 px-6 py-4">
        <div className="max-w-3xl mx-auto w-full">
          <div className="flex items-end gap-3 px-4 py-3 bg-zinc-800/60 rounded-xl border border-zinc-700/50 hover:border-zinc-600/50 transition-colors duration-150 focus-within:border-zinc-600">
            <textarea
              ref={textareaRef}
              value={input}
              onChange={handleTextareaChange}
              onKeyDown={handleKeyDown}
              placeholder="Bir şey sorun veya /plan ile öğrenme yolu oluşturun..."
              rows={1}
              className="flex-1 bg-transparent resize-none outline-none text-sm text-zinc-100 placeholder-zinc-500 leading-relaxed"
            />
            <button
              onClick={() => handleSend()}
              disabled={!input.trim() || isThinking}
              className="text-zinc-500 hover:text-zinc-300 transition-colors duration-150 disabled:opacity-30 flex-shrink-0 pb-0.5"
            >
              <Send className="w-4 h-4" />
            </button>
          </div>
          <p className="text-[10px] text-zinc-600 mt-2 text-center">
            Enter ile gönder · Shift+Enter yeni satır · /plan ile yapılandırılmış öğrenme
          </p>
        </div>
      </div>
    </div>
  );
}

function WelcomeState({ onPromptClick }: { onPromptClick: (prompt: string) => void }) {
  return (
    <div className="flex flex-col items-center justify-center min-h-[60vh]">
      <motion.div
        initial={{ opacity: 0, y: 12 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.4, ease: "easeOut" }}
        className="text-center"
      >
        <div className="flex items-center justify-center mb-6">
          <div className="w-12 h-12 rounded-xl bg-zinc-800 border border-zinc-700 flex items-center justify-center">
            <OrcaLogo className="w-6 h-6 text-zinc-300" />
          </div>
        </div>
        <h1 className="text-2xl font-semibold text-zinc-100 mb-2">
          Bugün ne öğrenmek istiyorsun?
        </h1>
        <p className="text-sm text-zinc-500 mb-8 max-w-md">
          Bana herhangi bir şey sor veya{" "}
          <code className="text-zinc-400 bg-zinc-800 px-1.5 py-0.5 rounded text-xs">
            /plan
          </code>{" "}
          ile yapılandırılmış öğrenme yolu oluştur.
        </p>

        <div className="grid grid-cols-2 gap-2 max-w-lg mx-auto">
          {QUICK_PROMPTS.map((qp) => (
            <button
              key={qp.label}
              onClick={() => onPromptClick(qp.prompt)}
              className="flex items-center gap-2.5 px-4 py-3 rounded-lg border border-zinc-800 bg-zinc-900/50 hover:bg-zinc-800/50 hover:border-zinc-700 text-left transition-colors duration-150 group"
            >
              <span className="text-base">{qp.icon}</span>
              <div>
                <p className="text-sm text-zinc-300 group-hover:text-zinc-100 transition-colors duration-150">
                  {qp.label}
                </p>
              </div>
            </button>
          ))}
        </div>

        <div className="flex items-center gap-1.5 mt-8 text-[10px] text-zinc-600">
          <Sparkles className="w-3 h-3" />
          Orka AI Öğrenme Motoru ile desteklenmektedir
        </div>
      </motion.div>
    </div>
  );
}
