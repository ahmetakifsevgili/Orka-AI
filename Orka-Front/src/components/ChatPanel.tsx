/*
 * ChatPanel — uygulamanın kalbi.
 * - POST /Chat/message ile mesaj gönderir.
 * - activeTopic NULL olabilir: backend yeni topic otomatik oluşturur.
 * - messageType "quiz" → tryParseQuiz ile QuizData extract edilir.
 * - Her AI yanıtından sonra onTopicsRefresh çağrılır (sidebar senkronizasyonu).
 */

import {
  useState,
  useRef,
  useEffect,
  useCallback,
  type Dispatch,
  type SetStateAction,
} from "react";
import { Send, Sparkles, BookOpen, Bell, Globe } from "lucide-react";
import { AnimatePresence, motion } from "framer-motion";
import { useLocation } from "wouter";
import toast from "react-hot-toast";
import type { ChatMessage, ApiTopic, QuizData } from "@/lib/types";
import { ChatAPI, UserAPI, KorteksAPI } from "@/services/api";
import { tryParseQuiz } from "@/lib/quizParser";
import { THINKING_STATES, PLANNING_THINKING_STATES } from "@/lib/mockData";
import ChatMessageComponent from "./ChatMessage";
import ThinkingIndicator from "./ThinkingIndicator";
import OrcaLogo from "./OrcaLogo";

interface ChatPanelProps {
  activeTopic: ApiTopic | null;
  sessionId: string | null;
  onSessionStart: (id: string) => void;
  messages: ChatMessage[];
  setMessages: Dispatch<SetStateAction<ChatMessage[]>>;
  sessionLoading: boolean;
  onOpenWiki: (topicId: string) => void;
  /** AI yanıtı geldikten sonra sidebar topic listesini yeniler. */
  onTopicsRefresh: () => void;
  /** Backend yanıtı yeni bir topic oluşturduysa (null topic modunda) çağrılır. */
  onTopicAutoCreated?: (topicId: string) => void;
  currentSubtopic?: { title: string; index: number; total: number; progress: number } | null;
  defaultMode?: "plan" | "chat";
  /** IDE'den gelen mesaj — mount sonrası otomatik gönderilir ve sıfırlanır. */
  pendingMessage?: string | null;
  /** pendingMessage tüketildikten sonra parent'i bilgilendirir */
  onPendingMessageConsumed?: () => void;
  /** IDE sayfasının otomatik veya manuel split view modunda açılmasını sağlar; quiz sorusu opsiyonel iletilir */
  onOpenIDE?: (question?: string) => void;
}

export default function ChatPanel({
  activeTopic,
  sessionId,
  onSessionStart,
  messages,
  setMessages,
  sessionLoading,
  onOpenWiki,
  onTopicsRefresh,
  onTopicAutoCreated,
  currentSubtopic,
  defaultMode = "chat",
  pendingMessage,
  onPendingMessageConsumed,
  onOpenIDE,
}: ChatPanelProps) {
  const [, navigate] = useLocation();
  const [input, setInput] = useState("");
  const [isPlanMode, setIsPlanMode] = useState(defaultMode === "plan");
  const [isKorteksMode, setIsKorteksMode] = useState(false);
  const [isThinking, setIsThinking] = useState(false);
  const [thinkingState, setThinkingState] = useState(THINKING_STATES[0]);

  // Yeni topic modu aktifleştirildiğinde reset at
  useEffect(() => {
    if (!activeTopic && messages.length === 0) {
      setIsPlanMode(defaultMode === "plan");
    }
  }, [defaultMode, activeTopic, messages.length]);
  const [userName, setUserName] = useState<string>("User");
  const [userInitial, setUserInitial] = useState<string>("U");
  const scrollRef = useRef<HTMLDivElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  // IDE → Chat mesaj tetikleyici için sendMessage ref'i
  const sendMessageRef = useRef<((content: string) => void) | null>(null);

  // Fetch user info
  useEffect(() => {
    const token = localStorage.getItem("orka_token");
    if (!token) return;
    UserAPI.getMe().then((res) => {
      const first = res.data?.firstName || "";
      const last = res.data?.lastName || "";
      const fullName = `${first} ${last}`.trim() || "User";
      setUserName(fullName);
      setUserInitial(first?.[0]?.toUpperCase() || "U");
    }).catch(() => {});
  }, []);

  // ── Thinking state rotator ─────────────────────────────────────────────
  useEffect(() => {
    if (!isThinking) return;
    const currentStates = isPlanMode ? PLANNING_THINKING_STATES : THINKING_STATES;
    
    let i = 0;
    setThinkingState(currentStates[0]);

    const id = setInterval(() => {
      i = (i + 1) % currentStates.length;
      setThinkingState(currentStates[i]);
    }, 900);
    return () => clearInterval(id);
  }, [isThinking, isPlanMode]);

  // ── Auto-scroll (only if user is near bottom) ───────────────────────────
  const isNearBottomRef = useRef(true);
  const isInitialLoadRef = useRef(true);
  
  const handleScroll = useCallback(() => {
    if (!scrollRef.current) return;
    const { scrollTop, scrollHeight, clientHeight } = scrollRef.current;
    isNearBottomRef.current = scrollHeight - scrollTop - clientHeight < 150;
  }, []);

  const scrollToBottom = useCallback((force = false) => {
    if (scrollRef.current && (force || isNearBottomRef.current))
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
  }, []);

  // Force scroll to bottom when messages first load (history) or new message added
  useEffect(() => {
    if (messages.length > 0 && isInitialLoadRef.current) {
      // Initial load — force to bottom with a slight delay for DOM to render
      isInitialLoadRef.current = false;
      requestAnimationFrame(() => scrollToBottom(true));
      return;
    }
    scrollToBottom();
  }, [messages.length, isThinking, scrollToBottom]);

  // Reset initial load flag when topic changes
  useEffect(() => {
    isInitialLoadRef.current = true;
  }, [activeTopic?.id]);

  const resetTextarea = () => {
    if (textareaRef.current) textareaRef.current.style.height = "auto";
  };

  // ── Core send logic ────────────────────────────────────────────────────
  const sendMessage = useCallback(
    async (content: string) => {
      if (!content || isThinking) return;

      const userMsg: ChatMessage = {
        id: `local-user-${Date.now()}`,
        role: "user",
        type: "text",
        content,
        timestamp: new Date(),
      };
      
      const assistantId = `local-ai-${Date.now()}`;
      const placeholderMsg: ChatMessage = {
        id: assistantId,
        role: "ai",
        type: "text",
        content: "",
        timestamp: new Date(),
        isStreaming: true,
      };

      setMessages((prev) => [...prev, userMsg, placeholderMsg]);
      setIsThinking(true);
      
      const isPlanRequest = isPlanMode || content.toLowerCase().includes("plan") || content.toLowerCase().includes("müfredat");
      const initStates = isPlanRequest ? PLANNING_THINKING_STATES : THINKING_STATES;
      setThinkingState(initStates[0]);

      let completedTopicId: string | null = null;

      try {
        const response = await ChatAPI.streamMessage({
          topicId: activeTopic?.id ?? undefined,
          sessionId: sessionId ?? undefined,
          content,
          isPlanMode: isPlanMode,
        });

        if (!response.ok) throw new Error("Stream connection failed");

        // Session ID header check (for new sessions)
        const newSessionId = response.headers.get("X-Orka-SessionId");
        if (newSessionId && !sessionId) {
          onSessionStart(newSessionId);
        }



        const reader = response.body?.getReader();
        const decoder = new TextDecoder();
        let currentContent = "";

        if (reader) {
          setIsThinking(false); 
          while (true) {
            const { done, value } = await reader.read();
            if (done) break;

            const chunk = decoder.decode(value, { stream: true });
            const lines = chunk.split("\n");

            for (const line of lines) {
              if (line.startsWith("data: ")) {
                const data = line.substring(6).replace(/\r$/, "");
                if (data === "[DONE]") break;
                if (data.startsWith("[ERROR]:")) {
                  const errMsg = data.replace("[ERROR]:", "").trim() || "Yanıt akışında bir hata oluştu.";
                  toast.error(errMsg, { duration: 6000 });
                  setIsThinking(false);
                  setMessages((prev) =>
                    prev.map(m =>
                      m.id === assistantId
                        ? { ...m, content: currentContent || errMsg, isStreaming: false }
                        : m
                    )
                  );
                  return;
                }
                
                // [THINKING:... ] chunk'ları — chat'e yazma, sadece thinking state güncelle
                if (data.startsWith("[THINKING:")) {
                  const thinkingText = data.replace(/^\[THINKING:\s*/, "").replace(/\]$/, "");
                  setThinkingState(thinkingText);
                  setIsThinking(true);
                  continue;
                }
                
                // First append the decoded chunk to our content buffer
                currentContent += data.replaceAll("[NEWLINE]", "\n");

                // Konu tamamlanma sinyali — frontend'e wiki kısayol butonu göstermek için
                const topicMatch = currentContent.match(/\[TOPIC_COMPLETE:([^\]]+)\]/i);
                if (topicMatch) {
                  completedTopicId = topicMatch[1];
                  currentContent = currentContent.replace(/\[TOPIC_COMPLETE:[^\]]*\]/gi, "");
                }

                // Plan_ready detector
                if (currentContent.includes("[PLAN_READY]")) {
                  currentContent = currentContent.replace(/\[PLAN_READY\]/g, "");

                  // Play sound if enabled
                  const userStr = localStorage.getItem("orka_user");
                  const user = userStr ? JSON.parse(userStr) : null;
                  if (user?.settings?.soundsEnabled !== false) {
                     const audio = new Audio("/assets/notification.wav");
                     audio.play().catch(console.error);
                  }
                  
                  // Extract Topic Title cleanly or default to generic string
                  const match = currentContent.match(/\*\*(.*?)\*\*/);
                  const subjectName = match ? match[1] : "Seçili Eğitim";
                  toast.success(`${subjectName} konunuzun Çalışma müfredatı oluşmuştur 🎯`, {
                    duration: 5000,
                    icon: "📚"
                  });
                }

                // IDE Open Detector (Robust)
                if (/\[IDE_OPEN\]/i.test(currentContent)) {
                  currentContent = currentContent.replace(/\[IDE_OPEN\]/gi, "");
                  if (onOpenIDE) onOpenIDE();
                }

                setMessages((prev) => 
                  prev.map(m => m.id === assistantId ? { ...m, content: currentContent } : m)
                );
              }
            }
          }
        }

        // Finalize: Check for Quiz/Rich content or Topic Completion
        const quizData = tryParseQuiz(currentContent);
        if (completedTopicId) {
          // Konu tamamlandı: AI mesajını bitir, ardından tamamlama kartı ekle
          setMessages((prev) =>
            prev.map(m => m.id === assistantId ? { ...m, isStreaming: false } : m)
          );
          const completionCard: ChatMessage = {
            id: `completion-${Date.now()}`,
            role: "ai",
            type: "topic_complete",
            content: "",
            completedTopicId,
            timestamp: new Date(),
          };
          setMessages((prev) => [...prev, completionCard]);
        } else if (quizData) {
          setMessages((prev) =>
            prev.map(m => m.id === assistantId ? { ...m, type: "quiz", quiz: quizData, isStreaming: false } : m)
          );
        } else {
          setMessages((prev) =>
            prev.map(m => m.id === assistantId ? { ...m, isStreaming: false } : m)
          );
        }

        // Force sidebar refresh to reflect potential hierarchy changes (Deep Plan, new topics)
        onTopicsRefresh();
        
        // If it was a null-topic start, the header will also have the new SessionId & TopicId
        const finalSessionId = response.headers.get("X-Orka-SessionId");
        const finalTopicId = response.headers.get("X-Orka-TopicId");
        
        if (finalSessionId && !sessionId) {
            onSessionStart(finalSessionId);
            if (finalTopicId && onTopicAutoCreated) {
                onTopicAutoCreated(finalTopicId);
            }
            // Re-fetch all topics to ensure the auto-created one is there
            setTimeout(() => onTopicsRefresh(), 500);
        }
      } catch (err) {
        console.error("Streaming error:", err);
        toast.error("Yanıt akışında bir sorun oluştu.");
        setMessages((prev) => 
          prev.map(m => m.id === assistantId ? { ...m, content: "Üzgünüm, bir bağlantı hatası oluştu." } : m)
        );
      } finally {
        setIsThinking(false);
      }
    },
    [
      isThinking,
      isPlanMode,
      activeTopic,
      sessionId,
      onSessionStart,
      setMessages,
      onTopicsRefresh,
    ]
  );

  // sendMessage ref'i güncel tut — IDE→Chat tetikleyici için
  useEffect(() => {
    sendMessageRef.current = sendMessage;
  }, [sendMessage]);

  // IDE'den gelen pending mesajı otomatik gönder
  useEffect(() => {
    if (!pendingMessage || isThinking) return;
    const fn = sendMessageRef.current;
    if (!fn) return;
    fn(pendingMessage);
    onPendingMessageConsumed?.();
  }, [pendingMessage]);

  // ── Korteks stream send ────────────────────────────────────────────────
  const sendKorteksMessage = useCallback(
    async (content: string) => {
      if (!content || isThinking) return;
      const userMsg: ChatMessage = {
        id: `local-user-${Date.now()}`,
        role: "user",
        type: "text",
        content,
        timestamp: new Date(),
      };
      const assistantId = `local-ai-${Date.now()}`;
      const placeholderMsg: ChatMessage = {
        id: assistantId,
        role: "ai",
        type: "text",
        content: "",
        timestamp: new Date(),
        isStreaming: true,
      };
      setMessages((prev) => [...prev, userMsg, placeholderMsg]);
      setIsThinking(true);
      setThinkingState("Korteks derin arastirma yapıyor...");
      let currentContent = "";
      try {
        const response = await KorteksAPI.stream({
          topic: content,
          topicId: activeTopic?.id ?? undefined,
        });
        if (!response.ok) throw new Error("Korteks stream failed");
        const reader = response.body?.getReader();
        const decoder = new TextDecoder();
        if (reader) {
          setIsThinking(false);
          while (true) {
            const { done, value } = await reader.read();
            if (done) break;
            const chunk = decoder.decode(value, { stream: true });
            for (const line of chunk.split("\n")) {
              if (!line.startsWith("data: ")) continue;
              const data = line.substring(6).replace(/\r$/, "");
              if (data === "[DONE]") break;
              if (data.startsWith("[ERROR]:")) {
                toast.error(data.replace("[ERROR]:", "").trim(), { duration: 6000 });
                break;
              }
              if (data.startsWith("[THINKING:")) {
                setThinkingState(data.replace(/^\[THINKING:\s*/, "").replace(/\]$/, ""));
                setIsThinking(true);
                continue;
              }
              currentContent += data;
              setMessages((prev) =>
                prev.map((m) => (m.id === assistantId ? { ...m, content: currentContent } : m))
              );
            }
          }
        }
        setMessages((prev) =>
          prev.map((m) => (m.id === assistantId ? { ...m, isStreaming: false } : m))
        );
        onTopicsRefresh();
      } catch (err) {
        console.error("Korteks error:", err);
        toast.error("Korteks araştırması başarısız oldu.");
        setMessages((prev) =>
          prev.map((m) => (m.id === assistantId ? { ...m, content: "Korteks araştırması sırasında bir hata oluştu.", isStreaming: false } : m))
        );
      } finally {
        setIsThinking(false);
      }
    },
    [isThinking, activeTopic, setMessages, onTopicsRefresh]
  );

  // ── Textarea send ──────────────────────────────────────────────────────
  const handleSend = useCallback(
    (text?: string) => {
      const content = (text ?? input).trim();
      if (!content) return;
      setInput("");
      resetTextarea();
      if (isKorteksMode) {
        sendKorteksMessage(content);
      } else {
        sendMessage(content);
      }
    },
    [input, isKorteksMode, sendMessage, sendKorteksMessage]
  );

  // ── Quiz answer callback ───────────────────────────────────────────────
  const handleQuizAnswer = useCallback(
    (formattedAnswer: string) => {
      sendMessage(formattedAnswer);
    },
    [sendMessage]
  );

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

  // ── Session loading spinner ────────────────────────────────────────────
  if (sessionLoading) {
    return (
      <div className="flex-1 flex items-center justify-center bg-zinc-900 h-full">
        <div className="flex items-center gap-2.5">
          {[0, 1, 2].map((i) => (
            <div
              key={i}
              className="w-1.5 h-1.5 rounded-full bg-zinc-600 animate-pulse"
              style={{ animationDelay: `${i * 0.15}s` }}
            />
          ))}
        </div>
      </div>
    );
  }

  return (
    <div className="flex-1 flex flex-col bg-zinc-900 h-full overflow-hidden">
      {/* Topic Header — topic varsa adını, yoksa AI assistant markasını göster */}
      <div className="flex-shrink-0 flex items-center justify-between px-6 py-3 border-b border-zinc-800/50">
        <div className="flex items-center gap-2.5">
          {activeTopic ? (
            <>
              <span className="text-base">{activeTopic.emoji}</span>
              <span className="text-sm font-medium text-zinc-200">
                {activeTopic.title}
              </span>
              {currentSubtopic && (
                <div className="flex items-center gap-2 ml-2 pl-2 border-l border-zinc-700/50">
                  <span className="text-[11px] text-zinc-500">{'>'}</span>
                  <span className="text-xs text-zinc-400">{currentSubtopic.title}</span>
                  <div className="flex items-center gap-1.5 ml-2">
                    <div className="w-16 h-1 bg-zinc-800 rounded-full overflow-hidden">
                      <div className="h-full bg-emerald-500/60 transition-all duration-500" style={{ width: `${currentSubtopic.progress}%` }} />
                    </div>
                    <span className="text-[9px] text-zinc-600">{currentSubtopic.index}/{currentSubtopic.total}</span>
                  </div>
                </div>
              )}
            </>
          ) : (
            <>
              <OrcaLogo className="w-4 h-4 text-zinc-500" />
              <span className="text-sm font-medium text-zinc-400">
                Orka AI
              </span>
            </>
          )}
        </div>
        <div className="flex items-center gap-4">
          {activeTopic && (
            <button
              onClick={() => onOpenWiki(activeTopic.id)}
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-medium text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800/80 transition-colors duration-150"
            >
              <BookOpen className="w-3.5 h-3.5" />
              Wiki
            </button>
          )}

        </div>
      </div>

      {/* Aktif Konu Göstergesi (U1) */}
      <AnimatePresence>
        {currentSubtopic && (
          <motion.div
            initial={{ opacity: 0, y: -10 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -10 }}
            className="absolute top-0 inset-x-0 z-10 flex justify-center pt-4 pointer-events-none"
          >
            <div className="bg-zinc-900/90 backdrop-blur border border-zinc-800 rounded-full px-5 py-2 shadow-lg shadow-black/20 flex items-center gap-3">
              <BookOpen className="w-4 h-4 text-emerald-500" />
              <div className="flex items-center gap-2 text-xs font-medium">
                <span className="text-zinc-400">{activeTopic?.title}</span>
                <span className="text-zinc-600">❯</span>
                <span className="text-zinc-100">{currentSubtopic.title}</span>
              </div>
              
              <div className="flex items-center gap-2 ml-4 pl-4 border-l border-zinc-800">
                <div className="text-[10px] text-zinc-500 font-mono">
                  {currentSubtopic.index} / {currentSubtopic.total}
                </div>
                <div className="w-16 h-1.5 bg-zinc-800 rounded-full overflow-hidden">
                  <div 
                    className="h-full bg-emerald-500 rounded-full" 
                    style={{ width: `${currentSubtopic.progress}%` }}
                  />
                </div>
              </div>
            </div>
          </motion.div>
        )}
      </AnimatePresence>
      
      {/* Messages */}
      <div ref={scrollRef} onScroll={handleScroll} className="flex-1 overflow-y-auto relative">
        <div className="max-w-3xl mx-auto w-full px-6 py-8">
          {messages.length === 0 ? (
            <WelcomeState onPromptClick={(p) => handleSend(p)} />
          ) : (
            <div className="space-y-1">
              {messages.map((msg) => (
                <ChatMessageComponent
                  key={msg.id}
                  message={msg}
                  onSubmitAnswer={(answer) => {
                    sendMessage(answer);
                  }}
                  onOpenWiki={onOpenWiki}
                  onOpenIDE={onOpenIDE}
                />
              ))}
            </div>
          )}

          <AnimatePresence>
            {isThinking && (
              <motion.div
                initial={{ opacity: 0, y: 8 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0 }}
                className="flex items-center gap-3 mt-4"
              >
                <div className="flex-shrink-0 w-8 h-8 rounded-full bg-zinc-800 border border-zinc-700 flex items-center justify-center">
                  <OrcaLogo className="w-4 h-4 text-zinc-300" animated={true} />
                </div>
                <div className="pt-0">
                  <ThinkingIndicator state={thinkingState} />
                </div>
              </motion.div>
            )}
          </AnimatePresence>
        </div>
      </div>

      {/* Floating Input Frame — Claude/Gemini Style */}
      <div className="flex-shrink-0 relative pointer-events-none">
        <div className="max-w-3xl mx-auto w-full px-6 pb-8 pt-2 pointer-events-auto">
          <motion.div 
            layout
            initial={false}
            className={`
              glass-panel rounded-2xl border transition-all duration-500 shadow-2xl
              ${isThinking ? "opacity-90 grayscale-[0.2]" : "opacity-100"}
              ${isPlanMode ? "glow-silver-active" : (input.length > 0 ? "glow-silver border-zinc-500/20" : "border-zinc-700/40 shadow-black/40")}
              bg-zinc-900/80 backdrop-blur-xl overflow-hidden
            `}
          >
            <div className="px-4 pt-3 flex flex-col">
              <textarea
                ref={textareaRef}
                value={input}
                onChange={handleTextareaChange}
                onKeyDown={handleKeyDown}
                placeholder={
                  isKorteksMode
                    ? "Arastirmamı istediğin konuyu yaz, web'de derinlemesine arastırayım..."
                    : isPlanMode
                    ? "Bana bir konu ver, senin icin en güncel müfredatı olusturayım..."
                    : "Bir sey sor veya müfredat olusturmak icin Plan Modu'nu ac..."
                }
                rows={1}
                disabled={isThinking}
                className="w-full bg-transparent resize-none outline-none text-[14px] text-zinc-100 placeholder-zinc-500 leading-relaxed min-h-[44px] max-h-[200px] py-1"
              />
              
              <div className="flex items-center justify-between pb-3 pt-2 border-t border-zinc-800/30 mt-1">
                <div className="flex items-center gap-2">
                  <button
                    onClick={() => { setIsPlanMode((prev) => !prev); setIsKorteksMode(false); }}
                    className={`
                      flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[11px] font-semibold transition-all duration-200 border
                      ${isPlanMode
                        ? "bg-white/10 border-white/20 text-white shadow-sm shadow-white/5"
                        : "bg-zinc-800/40 border-zinc-700/50 text-zinc-500 hover:text-zinc-300 hover:border-zinc-600"}
                    `}
                    title="Plan Modu — Ogrenme mufredati olusturur"
                  >
                    <Sparkles className={`w-3.5 h-3.5 ${isPlanMode ? "text-emerald-400" : ""}`} />
                    <span>Plan Modu</span>
                  </button>

                  <button
                    onClick={() => { setIsKorteksMode((prev) => !prev); setIsPlanMode(false); }}
                    className={`
                      flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[11px] font-semibold transition-all duration-200 border
                      ${isKorteksMode
                        ? "bg-emerald-900/30 border-emerald-700/50 text-emerald-400 shadow-sm"
                        : "bg-zinc-800/40 border-zinc-700/50 text-zinc-500 hover:text-zinc-300 hover:border-zinc-600"}
                    `}
                    title="Korteks — Web'de derin arastirma yapar ve wiki'ye kaydeder"
                  >
                    <Globe className={`w-3.5 h-3.5 ${isKorteksMode ? "text-emerald-400" : ""}`} />
                    <span>Korteks</span>
                  </button>

                  {(isPlanMode || isKorteksMode) && (
                    <motion.span
                      initial={{ opacity: 0, x: -10 }}
                      animate={{ opacity: 1, x: 0 }}
                      className={`text-[10px] font-medium tracking-tight uppercase ${isKorteksMode ? "text-emerald-600" : "text-zinc-400"}`}
                    >
                      {isKorteksMode ? "Deep Research Active" : "Planning Engine Active"}
                    </motion.span>
                  )}
                </div>

                <div className="flex items-center gap-3">
                   <span className="text-[10px] text-zinc-600 hidden sm:inline-block">
                     {input.length > 0 ? "Shift+Enter yeni satır" : ""}
                   </span>
                   <button
                    onClick={() => handleSend()}
                    disabled={!input.trim() || isThinking}
                    className={`
                      flex items-center justify-center w-8 h-8 rounded-lg transition-all duration-200
                      ${!input.trim() || isThinking 
                        ? "bg-zinc-800 text-zinc-600 opacity-50 cursor-not-allowed" 
                        : "bg-zinc-100 hover:bg-white text-zinc-950 shadow-lg shadow-white/5"}
                    `}
                  >
                    <Send className="w-4 h-4" />
                  </button>
                </div>
              </div>
            </div>
          </motion.div>
          <p className="text-[9px] text-zinc-600 mt-3 text-center tracking-wide uppercase opacity-50 font-medium">
            Orka AI · SOLID Planning Engine v4.2 · Streaming Enabled
          </p>
        </div>
      </div>
    </div>
  );
}

// ── Sub-components ─────────────────────────────────────────────────────────

function WelcomeState({ onPromptClick }: { onPromptClick: (p: string) => void }) {
  return (
    <div className="flex flex-col items-center justify-center min-h-[40vh]">
      <motion.div
        initial={{ opacity: 0, y: 12 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.4, ease: "easeOut" }}
        className="text-center"
      >
        <div className="flex items-center justify-center mb-6">
          <div className="w-12 h-12 rounded-2xl bg-zinc-800 border border-zinc-700/50 flex items-center justify-center shadow-2xl shadow-black/40">
            <OrcaLogo className="w-6 h-6 text-zinc-100" />
          </div>
        </div>
        <h1 className="text-xl font-semibold text-zinc-100 mb-2">
          Bugün ne öğrenmek istiyorsun?
        </h1>
        <p className="text-xs text-zinc-500 mb-8 max-w-[280px] mx-auto leading-relaxed">
          Bana herhangi bir şey sor veya{" "}
          <code className="text-zinc-300 bg-zinc-800 px-1.5 py-0.5 rounded text-[10px] font-mono">
            /plan
          </code>{" "}
          yazarak kişiselleştirilmiş bir müfredat oluştur.
        </p>
        
        <div className="flex items-center justify-center gap-2 mt-4 py-2 px-4 rounded-full border border-zinc-800/50 bg-zinc-900/30 w-fit mx-auto">
          <Sparkles className="w-3 h-3 text-zinc-400" />
          <span className="text-[10px] font-medium text-zinc-500 tracking-wide uppercase">
            SOLID Thinking Engine Ready
          </span>
        </div>
      </motion.div>
    </div>
  );
}
