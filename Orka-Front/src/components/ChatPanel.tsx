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
import {
  Send,
  Sparkles,
  BookOpen,
  Globe,
  Volume2,
  VolumeX,
  Image as ImageIcon,
  X,
  StopCircle,
  Users,
} from "lucide-react";
import ClassroomAudioPlayer from "./ClassroomAudioPlayer";
import { AnimatePresence, motion } from "framer-motion";
import { useLocation } from "wouter";
import toast from "react-hot-toast";
import type { ChatMessage, ApiTopic, QuizData } from "@/lib/types";
import { ChatAPI, UserAPI, KorteksAPI, UploadAPI } from "@/services/api";
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
  const [isVoiceMode, setIsVoiceMode] = useState(false);
  const [isClassroomMode, setIsClassroomMode] = useState(false);
  const [isSpeaking, setIsSpeaking] = useState(false);
  const [activeSpeaker, setActiveSpeaker] = useState<string | null>(null);
  const [isThinking, setIsThinking] = useState(false);
  const [thinkingState, setThinkingState] = useState(THINKING_STATES[0]);
  const [showRemedialPrompt, setShowRemedialPrompt] = useState(false);
  const [remedialCountdown, setRemedialCountdown] = useState(10);
  const [pendingImage, setPendingImage] = useState<{
    file: File;
    previewUrl: string;
  } | null>(null);

  // Yeni topic modu aktifleştirildiğinde reset at
  useEffect(() => {
    if (!activeTopic && messages.length === 0) {
      setIsPlanMode(defaultMode === "plan");
    }
  }, [defaultMode, activeTopic, messages.length]);

  useEffect(() => {
    return () => {
      if (pendingImage?.previewUrl) URL.revokeObjectURL(pendingImage.previewUrl);
    };
  }, [pendingImage?.previewUrl]);
  const [userName, setUserName] = useState<string>("User");
  const [userInitial, setUserInitial] = useState<string>("U");
  const scrollRef = useRef<HTMLDivElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const imageInputRef = useRef<HTMLInputElement>(null);
  const streamRunIdRef = useRef(0);
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
    }).catch(() => { });
  }, []);

  // Voice dictation ref — allows ClassroomAudioPlayer to call sendMessage
  // without a circular dependency (sendMessage is declared later).
  const voiceDictationRef = useRef<((text: string) => void) | null>(null);

  useEffect(() => {
    (window as any).handleVoiceDictation = (text: string) => {
      setInput("");
      voiceDictationRef.current?.(text);
    };
    return () => {
      delete (window as any).handleVoiceDictation;
    };
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
    async (
      content: string,
      options?: {
        force?: boolean;
        image?: { file: File; previewUrl: string } | null;
      }
    ) => {
      const imageToSend = options?.image ?? null;
      if ((!content && !imageToSend) || (isThinking && !options?.force)) return;
      const runId = ++streamRunIdRef.current;

      const userMsg: ChatMessage = {
        id: `local-user-${Date.now()}`,
        role: "user",
        type: "text",
        content: content || "Bu görseli incele.",
        attachments: imageToSend
          ? [{ type: "image", url: imageToSend.previewUrl, name: imageToSend.file.name }]
          : undefined,
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
        let response: Response;
        if (imageToSend) {
          setThinkingState("Görsel yükleniyor...");
          const uploaded = await UploadAPI.image(imageToSend.file);
          if (runId !== streamRunIdRef.current) return;
          response = await ChatAPI.streamMultimodal({
            topicId: activeTopic?.id ?? undefined,
            sessionId: sessionId ?? undefined,
            isPlanMode,
            contentItems: [
              ...(content ? [{ type: "Text" as const, text: content }] : []),
              { type: "ImageUrl" as const, imageUrl: uploaded.imageUrl },
            ],
          });
        } else {
          response = await ChatAPI.streamMessage({
            topicId: activeTopic?.id ?? undefined,
            sessionId: sessionId ?? undefined,
            content,
            isPlanMode: isPlanMode,
            isVoiceMode: isVoiceMode,
          });
        }

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
          if (isVoiceMode) setIsSpeaking(true);
          while (true) {
            const { done, value } = await reader.read();
            if (runId !== streamRunIdRef.current) {
              await reader.cancel().catch(() => {});
              return;
            }
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
                if (currentContent.includes("[REMEDIAL_OFFER]")) {
                  currentContent = currentContent.replace(/\[REMEDIAL_OFFER\]/g, "");
                  setShowRemedialPrompt(true);
                  setRemedialCountdown(10);
                }

                if (currentContent.includes("[PLAN_READY]")) {
                  currentContent = currentContent.replace(/\[PLAN_READY\]/g, "");
                  setIsPlanMode(false); // Otomatik olarak plan modundan çıkıştır

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

                // IDE_OPEN tag will be removed here, but the trigger will happen at the end
                if (/\[IDE_OPEN\]/i.test(currentContent)) {
                  currentContent = currentContent.replace(/\[IDE_OPEN\]/gi, "\n<!-- IDE TRIGGERED -->\n");
                }

                // Pollinations URL Encoding Fix
                currentContent = currentContent.replace(
                  /(https:\/\/image\.pollinations\.ai\/prompt\/)([^?)]+)/g,
                  (match, p1, p2) => p1 + encodeURIComponent(p2.replace(/ /g, ' '))
                );

                setMessages((prev) =>
                  prev.map(m => m.id === assistantId ? { ...m, content: currentContent } : m)
                );
              }
            }
          }
        }

        // Finalize: Check for Quiz/Rich content or Topic Completion
        const quizData = tryParseQuiz(currentContent);
        
        // IDE Open processing at the end of stream — remove internal marker from displayed content
        if (currentContent.includes("<!-- IDE TRIGGERED -->")) {
          let questionCode = "";
          const taskMatch = currentContent.match(/(?:## GÖREV|GÖREV)[\s\S]*?(?=\n\n(?:##|\[)|\n$|$)/i);
          if (taskMatch) {
            questionCode = taskMatch[0].trim();
          }
          // Clean internal marker from the message before displaying
          currentContent = currentContent.replace(/\n<!-- IDE TRIGGERED -->\n/g, "").trim();
          if (onOpenIDE) onOpenIDE(questionCode);
        }

        if (completedTopicId) {
          // Konu tamamlandı: AI mesajını bitir, ardından tamamlama kartı ekle
          setMessages((prev) =>
            prev.map(m => m.id === assistantId ? { ...m, content: currentContent, isStreaming: false } : m)
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
        if (runId === streamRunIdRef.current) {
          setIsThinking(false);
          if (!isVoiceMode) {
            setIsSpeaking(false);
          }
        }
      }
    },
    [
      isThinking,
      isPlanMode,
      isVoiceMode,
      activeTopic,
      sessionId,
      onSessionStart,
      setMessages,
      onTopicsRefresh,
    ]
  );

  const sendClassroomMessage = useCallback(
    async (content: string, force = false) => {
      const topic = content || activeTopic?.title || "Serbest konu";
      if ((!topic && !activeTopic) || (isThinking && !force)) return;
      const runId = ++streamRunIdRef.current;

      const userMsg: ChatMessage = {
        id: `local-user-${Date.now()}`,
        role: "user",
        type: "text",
        content: `Sınıfı başlat: ${topic}`,
        timestamp: new Date(),
      };
      const assistantId = `classroom-ai-${Date.now()}`;
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
      setThinkingState("Sınıf oturumu hazırlanıyor...");

      try {
        const response = await ChatAPI.streamClassroom({
          topic,
          topicId: activeTopic?.id ?? undefined,
          sessionId: sessionId ?? undefined,
          isVoiceMode,
        });

        if (!response.ok) throw new Error("Classroom stream failed");

        const newSessionId = response.headers.get("X-Orka-SessionId");
        if (newSessionId && !sessionId) onSessionStart(newSessionId);

        const reader = response.body?.getReader();
        const decoder = new TextDecoder();
        let currentContent = "";

        if (reader) {
          setIsThinking(false);
          while (true) {
            const { done, value } = await reader.read();
            if (runId !== streamRunIdRef.current) {
              await reader.cancel().catch(() => {});
              return;
            }
            if (done) break;

            const chunk = decoder.decode(value, { stream: true });
            for (const line of chunk.split("\n")) {
              if (!line.startsWith("data: ")) continue;
              const data = line.substring(6).replace(/\r$/, "");
              if (!data || data === "[DONE]") continue;
              if (data.startsWith("[ERROR]:")) {
                throw new Error(data.replace("[ERROR]:", "").trim());
              }

              const normalized = data
                .replaceAll("[NEWLINE]", "\n")
                .replace(/\[TUTOR\]:/gi, "\n\n**Orka Hoca:** ")
                .replace(/\[PEER\]:/gi, "\n\n**Akran:** ");
              currentContent += normalized;
              setMessages((prev) =>
                prev.map((m) =>
                  m.id === assistantId ? { ...m, content: currentContent.trimStart() } : m
                )
              );
            }
          }
        }

        setMessages((prev) =>
          prev.map((m) => (m.id === assistantId ? { ...m, isStreaming: false } : m))
        );
        onTopicsRefresh();
      } catch (err) {
        console.error("Classroom stream error:", err);
        toast.error("Sınıf oturumu başlatılamadı.");
        setMessages((prev) =>
          prev.map((m) =>
            m.id === assistantId
              ? { ...m, content: "Sınıf oturumu sırasında bir bağlantı hatası oluştu.", isStreaming: false }
              : m
          )
        );
      } finally {
        if (runId === streamRunIdRef.current) setIsThinking(false);
      }
    },
    [activeTopic, isThinking, isVoiceMode, onSessionStart, onTopicsRefresh, sessionId, setMessages]
  );

  const interruptAndSend = useCallback(
    async (content: string, image?: { file: File; previewUrl: string } | null) => {
      if ((!content && !image) || !sessionId) {
        setIsThinking(false);
        if (isClassroomMode) {
          await sendClassroomMessage(content, true);
        } else {
          await sendMessage(content, { force: true, image });
        }
        return;
      }

      streamRunIdRef.current += 1;
      setMessages((prev) =>
        prev.map((m) =>
          m.isStreaming
            ? {
                ...m,
                isStreaming: false,
                content: m.content
                  ? `${m.content}\n\n_Yanıt burada kesildi._`
                  : "_Yanıt kesildi._",
              }
            : m
        )
      );
      setIsThinking(false);

      try {
        await ChatAPI.interruptStream(sessionId, content || "Yeni kullanıcı girdisi");
      } catch (err) {
        console.warn("Interrupt failed, continuing with a fresh stream", err);
      }

      if (isClassroomMode) {
        await sendClassroomMessage(content, true);
      } else {
        await sendMessage(content, { force: true, image });
      }
    },
    [isClassroomMode, sendClassroomMessage, sendMessage, sessionId, setMessages]
  );

  // sendMessage ref'i güncel tut — IDE→Chat tetikleyici ve sesli dikte için
  useEffect(() => {
    sendMessageRef.current = sendMessage;
    voiceDictationRef.current = sendMessage;
  }, [sendMessage]);

  // IDE'den gelen pending mesajı otomatik gönder
  useEffect(() => {
    if (!pendingMessage || isThinking) return;
    const fn = sendMessageRef.current;
    if (!fn) return;
    fn(pendingMessage);
    onPendingMessageConsumed?.();
  }, [pendingMessage]);

  // Remedial Lesson Handlers & Timer
  useEffect(() => {
    let timer: NodeJS.Timeout;
    if (showRemedialPrompt && remedialCountdown > 0) {
      timer = setInterval(() => setRemedialCountdown(c => c - 1), 1000);
    } else if (showRemedialPrompt && remedialCountdown === 0) {
      setShowRemedialPrompt(false);
      if (sendMessageRef.current) sendMessageRef.current("[REMEDIAL_ACCEPT]");
    }
    return () => clearInterval(timer);
  }, [showRemedialPrompt, remedialCountdown]);

  const handleAcceptRemedial = useCallback(() => {
    setShowRemedialPrompt(false);
    if (sendMessageRef.current) sendMessageRef.current("[REMEDIAL_ACCEPT]");
  }, []);

  const handleDeclineRemedial = useCallback(() => {
    setShowRemedialPrompt(false);
    if (sendMessageRef.current) sendMessageRef.current("[REMEDIAL_DECLINE]");
  }, []);

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
        const startResponse = await KorteksAPI.startResearch({
          query: content,
          topicId: activeTopic?.id ?? undefined,
        });

        const jobId = startResponse.jobId;
        setIsThinking(true);
        setThinkingState("Korteks Swarm araştırma görevini başlattı...");

        const intervalId = setInterval(async () => {
          try {
            const status = await KorteksAPI.getJobStatus(jobId);
            
            if (status.error) {
              clearInterval(intervalId);
              toast.error(status.error);
              setMessages((prev) =>
                prev.map((m) => (m.id === assistantId ? { ...m, content: `Hata: ${status.error}`, isStreaming: false } : m))
              );
              setIsThinking(false);
              return;
            }

            if (status.phase === "Completed") {
              clearInterval(intervalId);
              setMessages((prev) =>
                prev.map((m) => (m.id === assistantId ? { ...m, content: status.result || "", isStreaming: false, type: "research", researchTopic: content } : m))
              );
              setIsThinking(false);
              onTopicsRefresh();
            } else {
              setThinkingState(status.phase + ": " + (status.logs?.split('\n').filter((l: string) => l.trim().length > 0).pop() || "İşleniyor..."));
            }
          } catch (pollErr) {
            console.error("Poll error", pollErr);
          }
        }, 3000);

      } catch (err) {
        console.error("Korteks error:", err);
        toast.error("Korteks araştırması başlatılamadı.");
        setMessages((prev) =>
          prev.map((m) => (m.id === assistantId ? { ...m, content: "Korteks araştırması sırasında bir hata oluştu.", isStreaming: false } : m))
        );
        setIsThinking(false);
      }
    },
    [isThinking, activeTopic, setMessages, onTopicsRefresh]
  );

  // ── Textarea send ──────────────────────────────────────────────────────
  const handleSend = useCallback(
    (text?: string) => {
      const content = (text ?? input).trim();
      const imageToSend = pendingImage;
      if (!content && !imageToSend) return;
      setInput("");
      setPendingImage(null);
      resetTextarea();
      if (isThinking) {
        void interruptAndSend(content, imageToSend);
        return;
      }
      if (isClassroomMode) {
        sendClassroomMessage(content);
        return;
      }
      if (isKorteksMode) {
        sendKorteksMessage(content);
      } else {
        sendMessage(content, { image: imageToSend });
      }
    },
    [
      input,
      pendingImage,
      isThinking,
      interruptAndSend,
      isClassroomMode,
      sendClassroomMessage,
      isKorteksMode,
      sendKorteksMessage,
      sendMessage,
    ]
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

  const handleImageSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = "";
    if (!file) return;
    if (!file.type.startsWith("image/")) {
      toast.error("Sadece görsel dosyaları eklenebilir.");
      return;
    }
    if (file.size > 10 * 1024 * 1024) {
      toast.error("Görsel 10MB sınırını aşmamalı.");
      return;
    }
    setPendingImage((prev) => {
      if (prev?.previewUrl) URL.revokeObjectURL(prev.previewUrl);
      return { file, previewUrl: URL.createObjectURL(file) };
    });
    setIsKorteksMode(false);
    setIsClassroomMode(false);
  };

  const clearPendingImage = () => {
    setPendingImage((prev) => {
      if (prev?.previewUrl) URL.revokeObjectURL(prev.previewUrl);
      return null;
    });
  };

  // ── Session loading spinner ────────────────────────────────────────────
  if (sessionLoading) {
    return (
      <div className="flex-1 flex items-center justify-center soft-page h-full">
        <div className="flex items-center gap-2.5">
          {[0, 1, 2].map((i) => (
            <div
              key={i}
              className="w-1.5 h-1.5 rounded-full bg-foreground/30 animate-pulse"
              style={{ animationDelay: `${i * 0.15}s` }}
            />
          ))}
        </div>
      </div>
    );
  }

  return (
    <div className="flex-1 flex flex-col soft-page h-full overflow-hidden">
      {/* Topic Header — topic varsa adını, yoksa AI assistant markasını göster */}
      <div className="flex-shrink-0 flex items-center justify-between px-6 py-3 border-b soft-border soft-surface">
        <div className="flex items-center gap-2.5">
          {activeTopic ? (
            <>
              <span className="text-base">{activeTopic.emoji}</span>
              <span className="text-sm font-medium text-foreground">
                {activeTopic.title}
              </span>
              {currentSubtopic && (
                <div className="flex items-center gap-2 ml-2 pl-2 border-l soft-border">
                  <span className="text-[11px] soft-text-muted">{'>'}</span>
                  <span className="text-xs soft-text-muted">{currentSubtopic.title}</span>
                  <div className="flex items-center gap-1.5 ml-2">
                    <div className="w-16 h-1 soft-muted rounded-full overflow-hidden">
                      <div className="h-full bg-emerald-500/60 transition-all duration-500" style={{ width: `${currentSubtopic.progress}%` }} />
                    </div>
                    <span className="text-[9px] soft-text-muted">{currentSubtopic.index}/{currentSubtopic.total}</span>
                  </div>
                </div>
              )}
            </>
          ) : (
            <>
              <OrcaLogo className="w-4 h-4 soft-text-muted" />
              <span className="text-sm font-medium soft-text-muted">
                Orka AI
              </span>
            </>
          )}
        </div>
        <div className="flex items-center gap-4">
          {activeTopic && (
            <button
              onClick={() => onOpenWiki(activeTopic.id)}
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-medium soft-text-muted hover:text-foreground hover:bg-surface-muted transition-colors duration-150"
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
            <div className="soft-surface border rounded-full px-5 py-2 soft-shadow flex items-center gap-3">
              <BookOpen className="w-4 h-4 text-emerald-500" />
              <div className="flex items-center gap-2 text-xs font-medium">
                <span className="soft-text-muted">{activeTopic?.title}</span>
                <span className="soft-text-muted">/</span>
                <span className="text-foreground">{currentSubtopic.title}</span>
              </div>

              <div className="flex items-center gap-2 ml-4 pl-4 border-l soft-border">
                <div className="text-[10px] soft-text-muted font-mono">
                  {currentSubtopic.index} / {currentSubtopic.total}
                </div>
                <div className="w-16 h-1.5 soft-muted rounded-full overflow-hidden">
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
                <div className="flex-shrink-0 w-8 h-8 rounded-full soft-muted border soft-border flex items-center justify-center">
                  <OrcaLogo className="w-4 h-4 text-foreground" animated={true} />
                </div>
                <div className="pt-0">
                  <ThinkingIndicator state={thinkingState} />
                </div>
              </motion.div>
            )}
          </AnimatePresence>

          {/* Remedial Timer HUD */}
          <AnimatePresence>
            {showRemedialPrompt && (
              <motion.div
                initial={{ opacity: 0, y: 20 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: 20 }}
                className="mt-4 p-4 rounded-xl border border-amber-500/25 bg-amber-500/10 flex flex-col gap-3 relative overflow-hidden"
              >
                <div className="absolute top-0 left-0 h-1 bg-amber-500/20 w-full">
                  <motion.div 
                     initial={{ width: "100%" }}
                     animate={{ width: "0%" }}
                     transition={{ duration: 10, ease: "linear" }}
                     className="h-full bg-amber-500"
                  />
                </div>
                <div className="flex items-start gap-4 z-10">
                  <div className="p-2 bg-amber-500/15 rounded-lg shrink-0 mt-1">
                     <BookOpen className="w-5 h-5 text-amber-700 dark:text-amber-300" />
                  </div>
                  <div className="flex flex-col gap-1">
                    <h3 className="text-sm font-semibold text-foreground">Özel telafi dersi</h3>
                    <p className="text-xs soft-text-muted leading-relaxed">
                      Sıradaki konuya geçmeden önce hatalarını pekiştirmen için sana özel bir telafi dersi oluşturabilirim. 
                      İstemiyorsan <span className="font-bold text-foreground">{remedialCountdown} sn</span> içinde reddedebilirsin.
                    </p>
                  </div>
                </div>
                <div className="flex justify-end gap-2 mt-2 z-10">
                  <button onClick={handleDeclineRemedial} className="px-4 py-2 rounded-lg text-[11px] font-semibold soft-text-muted hover:text-foreground hover:bg-surface-muted transition-colors">
                    Hayır, sıradaki konuya geç
                  </button>
                  <button onClick={handleAcceptRemedial} className="px-4 py-2 rounded-lg text-[11px] font-semibold text-emerald-950 bg-emerald-500 hover:bg-emerald-400 transition-colors">
                    Evet, Telafi Dersi İsterim
                  </button>
                </div>
              </motion.div>
            )}
          </AnimatePresence>
        </div>
      </div>

      {/* Voice Classroom Player & HUD */}
      {isVoiceMode && sessionId && (
        <div className="flex-shrink-0 px-6 py-2 border-t soft-border soft-surface">
          {/* Audio Engine */}
          <ClassroomAudioPlayer
            sessionId={sessionId}
            onInterrupt={(elapsedMs) => {
              setIsSpeaking(false);
              setActiveSpeaker(null);
              console.log("Voice interrupted at", elapsedMs, "ms");
            }}
            isSpeaking={isSpeaking}
            onSpeakerActive={(speaker) => setActiveSpeaker(speaker)}
            onAudioEnded={() => {
              setIsSpeaking(false);
              setActiveSpeaker(null);
            }}
          />

          {/* Podcast Avatars HUD */}
          <div className="flex items-center justify-center gap-8 py-3 w-full">
            {/* HOCA AVATARI */}
            <div className="flex flex-col items-center gap-2">
              <motion.div 
                animate={{
                  boxShadow: activeSpeaker === "Hoca" ? ["0px 0px 0px 0px rgba(16,185,129,0.4)", "0px 0px 0px 10px rgba(16,185,129,0)", "0px 0px 0px 0px rgba(16,185,129,0)"] : "none",
                  scale: activeSpeaker === "Hoca" ? [1, 1.05, 1] : 1
                }}
                transition={{ duration: 1.5, repeat: activeSpeaker === "Hoca" ? Infinity : 0 }}
                className={`relative w-16 h-16 rounded-full flex items-center justify-center border transition-colors duration-300 ${activeSpeaker === "Hoca" ? "border-emerald-500 bg-emerald-500/10" : "soft-border soft-muted"}`}
              >
                <span className="text-2xl">👨‍🏫</span>
                {activeSpeaker === "Hoca" && (
                  <motion.div className="absolute inset-0 rounded-full border-2 border-emerald-400" layoutId="speaking-ring" />
                )}
              </motion.div>
              <span className={`text-xs font-bold ${activeSpeaker === "Hoca" ? "text-emerald-600 dark:text-emerald-300" : "soft-text-muted"}`}>Orka Hoca</span>
            </div>

            {/* LIVE INDICATOR */}
            <div className="flex flex-col items-center justify-center h-full px-4">
               {activeSpeaker ? (
                 <motion.div className="flex gap-1 items-end h-4" initial="hidden" animate="visible" variants={{
                   visible: { transition: { staggerChildren: 0.1, repeat: Infinity, repeatType: "reverse" } }
                 }}>
                   {[...Array(5)].map((_, i) => (
                     <motion.div key={i} className={`w-1 rounded-full ${activeSpeaker === "Hoca" ? "bg-emerald-500" : "bg-amber-500"}`}
                       variants={{
                         hidden: { height: "20%" },
                         visible: { height: ["20%", "100%", "20%"], transition: { duration: 0.8, ease: "easeInOut" } }
                       }}
                     />
                   ))}
                 </motion.div>
               ) : (
                 <div className="text-[10px] font-medium soft-text-muted tracking-wider">BEKLENİYOR</div>
               )}
            </div>

            {/* ASISTAN AVATARI */}
            <div className="flex flex-col items-center gap-2">
              <motion.div 
                animate={{
                  boxShadow: activeSpeaker === "Asistan" ? ["0px 0px 0px 0px rgba(244,63,94,0.4)", "0px 0px 0px 10px rgba(244,63,94,0)", "0px 0px 0px 0px rgba(244,63,94,0)"] : "none",
                  scale: activeSpeaker === "Asistan" ? [1, 1.05, 1] : 1
                }}
                transition={{ duration: 1.5, repeat: activeSpeaker === "Asistan" ? Infinity : 0 }}
                className={`flex w-16 h-16 rounded-full items-center justify-center border transition-colors duration-300 ${activeSpeaker === "Asistan" ? "border-amber-500 bg-amber-500/10" : "soft-border soft-muted"}`}
              >
                <span className="text-2xl">👩‍🎓</span>
              </motion.div>
              <span className={`text-xs font-bold ${activeSpeaker === "Asistan" ? "text-amber-700 dark:text-amber-300" : "soft-text-muted"}`}>Asistan</span>
            </div>
          </div>
        </div>
      )}

      {/* Floating Input Frame — Claude/Gemini Style */}
      <div className="flex-shrink-0 relative pointer-events-none">
        <div className="max-w-3xl mx-auto w-full px-6 pb-8 pt-2 pointer-events-auto">
          <motion.div
            layout
            initial={false}
            className={`
              glass-panel rounded-xl border transition-all duration-300 soft-shadow
              ${isThinking ? "opacity-90 grayscale-[0.2]" : "opacity-100"}
              ${isPlanMode ? "glow-silver-active" : (input.length > 0 ? "glow-silver border-soft-border" : "border-soft-border")}
              overflow-hidden
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
                      : (messages.length > 0 && messages[messages.length - 1].role === "ai" && messages[messages.length - 1].type === "quiz" && !messages[messages.length - 1].isStreaming)
                        ? "Lütfen öncelikle yukarıdaki soruyu cevaplayın..."
                        : "Bir sey sor veya müfredat olusturmak icin Plan Modu'nu ac..."
                }
                rows={1}
                disabled={messages.length > 0 && messages[messages.length - 1].role === "ai" && messages[messages.length - 1].type === "quiz" && !messages[messages.length - 1].isStreaming}
                className="w-full bg-transparent resize-none outline-none text-[14px] text-foreground placeholder:text-muted-foreground leading-relaxed min-h-[44px] max-h-[200px] py-1 disabled:opacity-50 disabled:cursor-not-allowed"
              />

              {pendingImage && (
                <div className="mb-3 flex items-center gap-3 rounded-lg border soft-border soft-muted p-2">
                  <img
                    src={pendingImage.previewUrl}
                    alt={pendingImage.file.name}
                    className="h-14 w-14 rounded-md object-cover border soft-border"
                  />
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-xs font-medium text-foreground">
                      {pendingImage.file.name}
                    </p>
                    <p className="text-[10px] soft-text-muted">
                      {(pendingImage.file.size / 1024 / 1024).toFixed(2)} MB
                    </p>
                  </div>
                  <button
                    onClick={clearPendingImage}
                    className="h-7 w-7 rounded-md soft-text-muted hover:text-foreground hover:bg-surface-muted flex items-center justify-center"
                    title="Görseli kaldır"
                  >
                    <X className="h-3.5 w-3.5" />
                  </button>
                </div>
              )}

              <div className="flex items-center justify-between pb-3 pt-2 border-t soft-border mt-1">
                <div className="flex items-center gap-2">
                  <input
                    ref={imageInputRef}
                    type="file"
                    accept="image/*"
                    className="hidden"
                    onChange={handleImageSelect}
                  />
                  <button
                    onClick={() => imageInputRef.current?.click()}
                    disabled={isKorteksMode || isClassroomMode}
                    className={`flex items-center justify-center w-8 h-8 rounded-lg border transition-all duration-200 ${
                      pendingImage
                        ? "bg-emerald-500/10 border-emerald-500/25 text-emerald-700 dark:text-emerald-300"
                        : "soft-muted border-soft-border soft-text-muted hover:text-foreground"
                    } disabled:opacity-40 disabled:cursor-not-allowed`}
                    title="Görsel ekle"
                  >
                    <ImageIcon className="w-3.5 h-3.5" />
                  </button>

                  <button
                    onClick={() => { setIsPlanMode((prev) => !prev); setIsKorteksMode(false); setIsClassroomMode(false); }}
                    className={`
                      flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[11px] font-semibold transition-all duration-200 border
                      ${isPlanMode
                        ? "bg-emerald-500/10 border-emerald-500/25 text-emerald-700 dark:text-emerald-300"
                        : "soft-muted border-soft-border soft-text-muted hover:text-foreground"}
                    `}
                    title="Plan Modu — Ogrenme mufredati olusturur"
                  >
                    <Sparkles className={`w-3.5 h-3.5 ${isPlanMode ? "text-emerald-400" : ""}`} />
                    <span>Plan Modu</span>
                  </button>

                  <button
                    onClick={() => { setIsKorteksMode((prev) => !prev); setIsPlanMode(false); setIsClassroomMode(false); clearPendingImage(); }}
                    className={`
                      flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[11px] font-semibold transition-all duration-200 border
                      ${isKorteksMode
                        ? "bg-emerald-500/10 border-emerald-500/25 text-emerald-700 dark:text-emerald-300"
                        : "soft-muted border-soft-border soft-text-muted hover:text-foreground"}
                    `}
                    title="Korteks — Web'de derin arastirma yapar ve wiki'ye kaydeder"
                  >
                    <Globe className={`w-3.5 h-3.5 ${isKorteksMode ? "text-emerald-400" : ""}`} />
                    <span>Korteks</span>
                  </button>

                  <button
                    onClick={() => setIsVoiceMode((prev) => !prev)}
                    className={`
                      flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[11px] font-semibold transition-all duration-200 border
                      ${isVoiceMode
                        ? "bg-amber-500/10 border-amber-500/25 text-amber-700 dark:text-amber-300"
                        : "soft-muted border-soft-border soft-text-muted hover:text-foreground"}
                    `}
                    title="Sesli Sınıf — Hoca ve Asistan seslendirmesini aktifleştirir"
                  >
                    {isVoiceMode ? <Volume2 className="w-3.5 h-3.5 text-amber-700 dark:text-amber-300" /> : <VolumeX className="w-3.5 h-3.5" />}
                    <span>Sesli Sınıf</span>
                  </button>

                  <button
                    onClick={() => { setIsClassroomMode((prev) => !prev); setIsPlanMode(false); setIsKorteksMode(false); clearPendingImage(); }}
                    className={`
                      flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[11px] font-semibold transition-all duration-200 border
                      ${isClassroomMode
                        ? "bg-emerald-500/10 border-emerald-500/25 text-emerald-700 dark:text-emerald-300"
                        : "soft-muted border-soft-border soft-text-muted hover:text-foreground"}
                    `}
                    title="Otonom sınıf - Tutor ve akran ajan konuşması"
                  >
                    <Users className={`w-3.5 h-3.5 ${isClassroomMode ? "text-emerald-400" : ""}`} />
                    <span>Sınıf</span>
                  </button>

                  {(isPlanMode || isKorteksMode || isVoiceMode || isClassroomMode || pendingImage) && (
                    <motion.span
                      initial={{ opacity: 0, x: -10 }}
                      animate={{ opacity: 1, x: 0 }}
                      className={`text-[10px] font-medium tracking-tight uppercase ${isKorteksMode || isClassroomMode || pendingImage ? "text-emerald-600" : isVoiceMode ? "text-amber-700 dark:text-amber-300" : "soft-text-muted"}`}
                    >
                      {pendingImage ? "Görsel hazır" : isKorteksMode ? "Araştırma açık" : isClassroomMode ? "Sınıf açık" : isVoiceMode ? "Sesli sınıf açık" : "Plan modu açık"}
                    </motion.span>
                  )}
                </div>

                <div className="flex items-center gap-3">
                  <span className="text-[10px] soft-text-muted hidden sm:inline-block">
                    {input.length > 0 ? "Shift+Enter yeni satır" : ""}
                  </span>
                  <button
                    onClick={() => handleSend()}
                    disabled={(!input.trim() && !pendingImage) || (messages.length > 0 && messages[messages.length - 1].role === "ai" && messages[messages.length - 1].type === "quiz" && !messages[messages.length - 1].isStreaming)}
                    className={`
                      flex items-center justify-center w-8 h-8 rounded-lg transition-all duration-200
                      ${((!input.trim() && !pendingImage) || (messages.length > 0 && messages[messages.length - 1].role === "ai" && messages[messages.length - 1].type === "quiz" && !messages[messages.length - 1].isStreaming))
                        ? "soft-muted soft-text-muted opacity-50 cursor-not-allowed"
                        : isThinking
                          ? "bg-amber-500 hover:bg-amber-400 text-amber-950"
                          : "bg-foreground hover:opacity-90 text-background"}
                    `}
                    title={isThinking ? "Yanıtı kes ve yeni mesajı gönder" : "Gönder"}
                  >
                    {isThinking ? <StopCircle className="w-4 h-4" /> : <Send className="w-4 h-4" />}
                  </button>
                </div>
              </div>
            </div>
          </motion.div>
          <p className="text-[9px] soft-text-muted mt-3 text-center tracking-wide uppercase opacity-70 font-medium">
            Orka AI
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
          <div className="w-12 h-12 rounded-xl soft-surface border flex items-center justify-center soft-shadow">
            <OrcaLogo className="w-6 h-6 text-foreground" />
          </div>
        </div>
        <h1 className="text-xl font-semibold text-foreground mb-2">
          Bugün ne öğrenmek istiyorsun?
        </h1>
        <p className="text-xs soft-text-muted mb-8 max-w-[280px] mx-auto leading-relaxed">
          Bana herhangi bir şey sor veya{" "}
          <code className="text-foreground soft-muted px-1.5 py-0.5 rounded text-[10px] font-mono">
            /plan
          </code>{" "}
          yazarak kişiselleştirilmiş bir müfredat oluştur.
        </p>

        <div className="flex items-center justify-center gap-2 mt-4 py-2 px-4 rounded-full border soft-border soft-surface w-fit mx-auto">
          <Sparkles className="w-3 h-3 soft-text-muted" />
          <span className="text-[10px] font-medium soft-text-muted tracking-wide uppercase">
            Öğrenmeye hazır
          </span>
        </div>
      </motion.div>
    </div>
  );
}
