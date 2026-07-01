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
import { ArrowUp, Sparkles, BookOpen, Bell, Globe, CheckCircle2, Edit3, RotateCcw, Loader2, Check, Plus, Paperclip, FileText } from "lucide-react";
import { AnimatePresence, motion } from "framer-motion";
import { useLocation } from "wouter";
import toast from "react-hot-toast";
import type { ChatMessage, ApiTopic, QuizData, StudyIntentPreview, ChatResponseMetadata, TeachingArtifact } from "@/lib/types";
import { ChatAPI, UserAPI, KorteksAPI, TopicsAPI, QuizAPI, TutorAPI } from "@/services/api";
import { tryParseQuiz } from "@/lib/quizParser";
import { THINKING_STATES, PLANNING_THINKING_STATES } from "@/lib/mockData";
import ChatMessageComponent from "./ChatMessage";
import ThinkingIndicator from "./ThinkingIndicator";
import OrcaLogo from "./OrcaLogo";
import ToolCapabilityStrip from "./ToolCapabilityStrip";
import { AgentStatusRail, ArtifactCanvas } from "./AgenticWorkspace";
import { useLanguage } from "@/contexts/LanguageContext";
import { useLearningWorkspaceState } from "@/hooks/useLearningWorkspaceState";

type PlanFlowStage = "idle" | "intent" | "topic" | "research" | "quiz" | "plan" | "done" | "error";

type TutorStreamEvent = {
  type?: string;
  data?: Record<string, unknown>;
  message?: string;
  content?: string;
  metadata?: ChatResponseMetadata;
};

function parseTutorStreamEvent(raw: string): TutorStreamEvent | null {
  const decoded = raw.replaceAll("[NEWLINE]", "\n").trim();
  if (!decoded.startsWith("{")) return null;
  try {
    const parsed = JSON.parse(decoded) as TutorStreamEvent;
    return typeof parsed?.type === "string" ? parsed : null;
  } catch {
    return null;
  }
}

function eventValue<T = unknown>(event: TutorStreamEvent, key: string): T | undefined {
  const direct = (event as Record<string, unknown>)[key];
  if (direct !== undefined) return direct as T;
  return event.data?.[key] as T | undefined;
}

type PlanCompletion = {
  planGenerated: boolean;
  generatedPlanRootTopicId?: string;
  generatedTopicIds?: string[];
  message?: string;
  score?: number;
  total?: number;
  skipped?: boolean;
};

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
  const [showAttachmentMenu, setShowAttachmentMenu] = useState(false);
  const [isThinking, setIsThinking] = useState(false);
  const [thinkingState, setThinkingState] = useState(THINKING_STATES[0]);
  const [planFlowStage, setPlanFlowStage] = useState<PlanFlowStage>("idle");
  const [planFlowDetail, setPlanFlowDetail] = useState<string | null>(null);
  const [pendingPlanIntent, setPendingPlanIntent] = useState<StudyIntentPreview | null>(null);
  const [pendingPlanRawRequest, setPendingPlanRawRequest] = useState("");

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
    if (planFlowStage !== "idle") return;
    const currentStates = isPlanMode ? PLANNING_THINKING_STATES : THINKING_STATES;

    let i = 0;
    setThinkingState(currentStates[0]);

    const id = setInterval(() => {
      i = (i + 1) % currentStates.length;
      setThinkingState(currentStates[i]);
    }, 900);
    return () => clearInterval(id);
  }, [isThinking, isPlanMode, planFlowStage]);

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

  const startStructuredPlanDiagnostic = useCallback(
    async (content: string) => {
      if (!content || isThinking) return;

      const userMsg: ChatMessage = {
        id: `local-user-${Date.now()}`,
        role: "user",
        type: "text",
        content,
        timestamp: new Date(),
      };

      setMessages((prev) => [...prev, userMsg]);
      setIsThinking(true);
      setIsPlanMode(false);
      setIsKorteksMode(false);
      setPendingPlanIntent(null);
      setPendingPlanRawRequest(content);
      setPlanFlowStage("intent");
      setPlanFlowDetail("Once calisma niyetini ayiriyorum. Onay vermeden Korteks arastirmasi, quiz veya plan baslamayacak.");

      try {
        const intent = await QuizAPI.analyzePlanIntent({
          rawRequest: content,
          topicId: activeTopic?.id,
          existingTopicTitle: activeTopic?.title,
        });
        setPendingPlanIntent(intent);
        setPlanFlowStage("idle");
        setPlanFlowDetail(null);
      } catch (err) {
        console.error("Study intent analysis error:", err);
        setPlanFlowStage("error");
        setPlanFlowDetail("Niyet analizi baslatilamadi; Korteks ve quiz cagrisi yapilmadi.");
        toast.error("Niyet analizi baslatilamadi. Backend durumunu kontrol ediyoruz.");
        setMessages((prev) => [
          ...prev,
          {
            id: `plan-error-${Date.now()}`,
            role: "ai",
            type: "text",
            content:
              "Niyet analizinde bir sorun olustu. Korteks arastirmasi ve quiz baslatilmadi; once ne calismak istedigini netlestirmemiz gerekiyor.",
            timestamp: new Date(),
          },
        ]);
      } finally {
        setIsThinking(false);
      }
    },
    [activeTopic, isThinking, setMessages]
  );

  const confirmPlanIntent = useCallback(
    async (intent: StudyIntentPreview) => {
      if (!intent || isThinking) return;

      setIsThinking(true);
      setPlanFlowStage("topic");
      setPlanFlowDetail("Onaylanan niyete gore konu aciliyor. Ham kullanici cumlesi Korteks'e gonderilmeyecek.");

      try {
        let topicId = activeTopic?.id;
        let topicTitle = activeTopic?.title ?? `${intent.mainTopic}: ${intent.focusArea}`;

        if (!topicId) {
          const title = `${intent.mainTopic}: ${intent.focusArea}`.replace(/\s+/g, " ").trim().slice(0, 90) || "Yeni calisma hedefi";
          const created = await TopicsAPI.create({
            title,
            emoji: "📘",
            category: "Plan",
          });
          topicId = created.data.id;
          topicTitle = created.data.title;
          onTopicAutoCreated?.(topicId!);
          onTopicsRefresh();
        }

        if (!topicId) {
          throw new Error("Topic could not be prepared for plan diagnostic.");
        }

        setPlanFlowStage("research");
        setPlanFlowDetail(`Korteks arastiriyor: ${intent.researchIntent}. Kaynak, YouTube, on kosul, alt kavram, yaygin hata ve pratik sirasi toplanacak.`);

        const start = await QuizAPI.startPlanDiagnostic({
          topicId,
          sessionId: topicId === activeTopic?.id ? sessionId ?? undefined : undefined,
          topicTitle,
          intentRequestId: intent.intentRequestId,
          rawStudyRequest: pendingPlanRawRequest || intent.rawRequest,
          approvedMainTopic: intent.mainTopic,
          approvedFocusArea: intent.focusArea,
          approvedStudyGoal: intent.studyGoal,
          approvedResearchIntent: intent.researchIntent,
        });

        const quizData = tryParseQuiz(start.questionsJson);
        if (!quizData) {
          throw new Error("Plan diagnostic quiz payload could not be parsed.");
        }

        setPendingPlanIntent(null);
        setPlanFlowStage("quiz");
        setPlanFlowDetail("Seviye testi hazir. Sorular tek quiz kartinda akacak; cevaplar chat mesaji olmayacak.");

        setMessages((prev) => [
          ...prev,
          {
            id: `plan-quiz-${Date.now()}`,
            role: "ai",
            type: "quiz",
            content:
              "Seviye testini burada coz. Orka cevaplarini chat mesaji gibi gondermeyecek; sonuc plan uretimi icin kullanilacak.",
            quiz: quizData,
            metadata: {
              groundingMode: start.groundingMode,
              planDiagnostic: {
                planRequestId: start.planRequestId,
                quizRunId: start.quizRunId,
                topicId: start.topicId,
                topicTitle: start.topicTitle,
                status: start.status,
                quizQuestionCount: start.quizQuestionCount,
                conceptGraphQualityStatus: start.conceptGraphQualityStatus,
                assessmentQualityStatus: start.assessmentQualityStatus,
                qualityReportId: start.qualityReportId,
                intentRequestId: start.intentRequestId,
                approvedMainTopic: start.approvedMainTopic,
                approvedFocusArea: start.approvedFocusArea,
                approvedStudyGoal: start.approvedStudyGoal,
                approvedResearchIntent: start.approvedResearchIntent,
              },
            },
            timestamp: new Date(),
          },
        ]);
        onTopicsRefresh();
      } catch (err) {
        console.error("Plan diagnostic start error:", err);
        setPlanFlowStage("error");
        setPlanFlowDetail("Onaylanan niyetle plan akisi baslatilamadi; quiz veya plan sahte olarak uretilmedi.");
        toast.error("Plan akisi baslatilamadi. Backend durumunu kontrol ediyoruz.");
      } finally {
        setIsThinking(false);
      }
    },
    [
      activeTopic,
      isThinking,
      onTopicAutoCreated,
      onTopicsRefresh,
      pendingPlanRawRequest,
      sessionId,
      setMessages,
    ]
  );

  const revisePlanIntent = useCallback(
    async (correction: string) => {
      const nextText = correction.trim();
      if (!nextText || isThinking) return;

      setIsThinking(true);
      setPlanFlowStage("intent");
      setPlanFlowDetail("Duzeltmeyi tekrar niyet analizinden geciriyorum. Hala Korteks cagrisi yok.");
      try {
        const intent = await QuizAPI.analyzePlanIntent({
          rawRequest: pendingPlanRawRequest || nextText,
          correction: nextText,
          topicId: activeTopic?.id,
          existingTopicTitle: activeTopic?.title,
        });
        setPendingPlanIntent(intent);
        setPlanFlowStage("idle");
        setPlanFlowDetail(null);
      } catch (err) {
        console.error("Study intent revision error:", err);
        toast.error("Niyet duzeltmesi analiz edilemedi.");
        setPlanFlowStage("error");
      } finally {
        setIsThinking(false);
      }
    },
    [activeTopic, isThinking, pendingPlanRawRequest]
  );

  const resetPlanIntent = useCallback(() => {
    setPendingPlanIntent(null);
    setPendingPlanRawRequest("");
    setIsPlanMode(true);
    setPlanFlowStage("idle");
    setPlanFlowDetail(null);
    textareaRef.current?.focus();
  }, []);
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
      setPlanFlowStage("idle");
      setPlanFlowDetail(null);

      const isPlanRequest = isPlanMode || content.toLowerCase().includes("plan") || content.toLowerCase().includes("mufredat");
      const initStates = isPlanRequest ? PLANNING_THINKING_STATES : THINKING_STATES;
      setThinkingState(initStates[0]);

      let completedTopicId: string | null = null;
      let streamMetadata: ChatResponseMetadata | null = null;
      let streamArtifacts: TeachingArtifact[] = [];

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
          let buffer = "";
          let currentEventType = "message";
          
          while (true) {
            const { done, value } = await reader.read();
            if (done) break;

            const chunk = decoder.decode(value, { stream: true });
            buffer += chunk;
            const lines = buffer.split("\n");
            buffer = lines.pop() || "";

            for (const line of lines) {
              if (line.startsWith("event: ")) {
                currentEventType = line.substring(7).trim();
                continue;
              }
              if (line.startsWith("data: ")) {
                const data = line.substring(6).replace(/\r$/, "");
                if (currentEventType === "done" || data === "[DONE]") break;
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

                const tutorEvent = parseTutorStreamEvent(data);
                if (tutorEvent) {
                  const type = tutorEvent.type;
                  if (type === "thinking") {
                    const message = eventValue<string>(tutorEvent, "message") ?? "Tutor state hazırlanıyor";
                    setThinkingState(message);
                    setIsThinking(true);
                    continue;
                  }

                  if (type === "tool_started" || type === "tool_finished") {
                    const toolId = eventValue<string>(tutorEvent, "toolId") ?? "tool";
                    const status = eventValue<string>(tutorEvent, "status") ?? (type === "tool_started" ? "başladı" : "hazır");
                    const toolCallId = eventValue<string>(tutorEvent, "toolCallId");
                    const success = eventValue<boolean>(tutorEvent, "success") ?? status === "ready";
                    const provider = eventValue<string>(tutorEvent, "provider");
                    const safeMessage = eventValue<string>(tutorEvent, "safeMessage");
                    const previousMetadata: ChatResponseMetadata = streamMetadata ?? {};
                    const previousStatuses = previousMetadata.toolStatuses ?? [];
                    const statusId = toolCallId ?? `planned-${toolId}`;
                    streamMetadata = {
                      ...previousMetadata,
                      toolStatuses: statusId
                        ? [
                            ...previousStatuses.filter(t => t.id !== statusId && !(toolCallId && t.id === `planned-${toolId}`)),
                            { id: statusId, toolId, status, success, provider, safeMessage },
                          ]
                        : previousStatuses,
                    };
                    setThinkingState(`${toolId}: ${status}`);
                    setMessages((prev) =>
                      prev.map(m => m.id === assistantId ? { ...m, metadata: streamMetadata } : m)
                    );
                    continue;
                  }

                  if (type === "artifact_ready") {
                    const artifactId = eventValue<string>(tutorEvent, "artifactId");
                    const artifactType = eventValue<string>(tutorEvent, "artifactType") ?? "artifact";
                    const previousMetadata: ChatResponseMetadata = streamMetadata ?? {};
                    const previousArtifactIds = previousMetadata.artifactIds ?? [];
                    streamMetadata = {
                      ...previousMetadata,
                      artifactIds: artifactId
                        ? Array.from(new Set([...previousArtifactIds, artifactId]))
                        : previousArtifactIds,
                    };
                    if (artifactId) {
                      try {
                        const artifact = await TutorAPI.getArtifact(artifactId);
                        streamArtifacts = [
                          ...streamArtifacts.filter(a => a.id !== artifact.id),
                          artifact,
                        ];
                      } catch {
                        streamMetadata = {
                          ...streamMetadata,
                          providerWarnings: Array.from(new Set([...(streamMetadata.providerWarnings ?? []), "artifact_fetch_failed"])),
                        };
                      }
                    }
                    setThinkingState(`${artifactType} hazirlandi`);
                    setMessages((prev) =>
                      prev.map(m => m.id === assistantId ? { ...m, metadata: streamMetadata, artifacts: streamArtifacts } : m)
                    );
                    continue;
                  }

                  if (type === "metadata") {
                    streamMetadata = (eventValue<ChatResponseMetadata>(tutorEvent, "metadata") ?? tutorEvent.metadata ?? streamMetadata) || null;
                    setMessages((prev) =>
                      prev.map(m => m.id === assistantId ? { ...m, metadata: streamMetadata } : m)
                    );
                    continue;
                  }

                  if (type === "final") {
                    const tutorTurnStateId = eventValue<string>(tutorEvent, "tutorTurnStateId");
                    const tutorActionTraceId = eventValue<string>(tutorEvent, "tutorActionTraceId");
                    const artifactIds = eventValue<string[]>(tutorEvent, "artifactIds");
                    streamMetadata = {
                      ...(streamMetadata ?? {}),
                      tutorTurnStateId: tutorTurnStateId ?? streamMetadata?.tutorTurnStateId,
                      tutorActionTraceId: tutorActionTraceId ?? streamMetadata?.tutorActionTraceId,
                      artifactIds: artifactIds ?? streamMetadata?.artifactIds,
                    };
                    if (artifactIds?.length) {
                      const missing = artifactIds.filter(id => !streamArtifacts.some(a => a.id === id));
                      for (const artifactId of missing) {
                        try {
                          const artifact = await TutorAPI.getArtifact(artifactId);
                          streamArtifacts = [...streamArtifacts, artifact];
                        } catch {
                          // Artifact fetch is best-effort; metadata still keeps the id.
                        }
                      }
                    }
                    setMessages((prev) =>
                      prev.map(m => m.id === assistantId ? { ...m, metadata: streamMetadata, artifacts: streamArtifacts } : m)
                    );
                    continue;
                  }

                  if (type === "token") {
                    const token = eventValue<string>(tutorEvent, "content") ?? tutorEvent.content ?? "";
                    currentContent += token;
                  } else {
                    continue;
                  }
                } else {
                  // Prevent raw JSON metadata or error payloads from leaking into user chat bubble if parse fails
                  const trimmed = data.trim();
                  const isJsonLike = trimmed.startsWith("{") || trimmed.startsWith("[") || /^\s*\{/i.test(trimmed) || trimmed.includes('"type":') || trimmed.includes('"content":');
                  if (!isJsonLike) {
                    currentContent += data.replaceAll("[NEWLINE]", "\n");
                  } else {
                    console.warn("SSE stream parser discarded unparsed JSON chunk:", trimmed);
                  }
                }

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
                  const subjectName = match ? match[1] : "Secili egitim";
                  toast.success(`${subjectName} calisma mufredati olustu.`, {
                    duration: 5000,
                    icon: "OK"
                  });
                }

                // IDE Open Detector (Robust)
                if (/\[IDE_OPEN\]/i.test(currentContent)) {
                  currentContent = currentContent.replace(/\[IDE_OPEN\]/gi, "");
                  if (onOpenIDE) onOpenIDE();
                }

                // Clean the content before displaying, delaying any trailing partial tag
                let displayContent = currentContent;
                const trailingOpenBracketMatch = currentContent.match(/\[[^\]]*$/);
                if (trailingOpenBracketMatch) {
                  displayContent = currentContent.substring(0, trailingOpenBracketMatch.index);
                }

                setMessages((prev) =>
                  prev.map(m => m.id === assistantId ? { ...m, content: displayContent } : m)
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
          prev.map(m => m.id === assistantId ? { ...m, content: "Baglanti tarafinda bir sorun olustu. Backend durumunu kontrol edip tekrar deneyelim." } : m)
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

  // ── Korteks stream send ──────���─────────────────────────────────────────
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
      setThinkingState("Korteks derin arastirma yapiyor...");
      let currentContent = "";
      try {
        const response = await KorteksAPI.stream({
          topic: content,
          topicId: activeTopic?.id ?? undefined,
        });
        if (!response.ok) throw new Error("Korteks stream failed");
        const reader = response.body?.getReader();
        const decoder = new TextDecoder();
        let currentEventType = "message";
        if (reader) {
          setIsThinking(false);
          let buffer = "";
          while (true) {
            const { done, value } = await reader.read();
            if (done) break;
            const chunk = decoder.decode(value, { stream: true });
            buffer += chunk;
            const lines = buffer.split("\n");
            buffer = lines.pop() || "";
            for (const line of lines) {
              if (line.startsWith("event: ")) {
                currentEventType = line.substring(7).trim();
                continue;
              }
              if (!line.startsWith("data: ")) continue;
              const data = line.substring(6).replace(/\r$/, "");
              if (currentEventType === "done" || data === "[DONE]") break;
              if (data.startsWith("[ERROR]:")) {
                toast.error(data.replace("[ERROR]:", "").trim(), { duration: 6000 });
                break;
              }
              if (data.startsWith("[THINKING:")) {
                setThinkingState(data.replace(/^\[THINKING:\s*/, "").replace(/\]$/, ""));
                setIsThinking(true);
                continue;
              }
              const trimmed = data.trim();
              const isJsonLike = trimmed.startsWith("{") || trimmed.startsWith("[") || /^\s*\{/i.test(trimmed) || trimmed.includes('"type":') || trimmed.includes('"content":');
              if (!isJsonLike) {
                currentContent += data;
              } else {
                console.warn("Korteks SSE parser discarded unparsed JSON chunk:", trimmed);
              }
              // Clean the content before displaying, delaying any trailing partial tag
              let displayContent = currentContent;
              const trailingOpenBracketMatch = currentContent.match(/\[[^\]]*$/);
              if (trailingOpenBracketMatch) {
                displayContent = currentContent.substring(0, trailingOpenBracketMatch.index);
              }

              setMessages((prev) =>
                prev.map((m) => (m.id === assistantId ? { ...m, content: displayContent } : m))
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
        toast.error("Korteks arastirmasi basarisiz oldu.");
        setMessages((prev) =>
          prev.map((m) => (m.id === assistantId ? { ...m, content: "Korteks arastirmasi sirasinda bir hata olustu. Bu sonuc plana aktarilmadi.", isStreaming: false } : m))
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
      } else if (isPlanMode) {
        startStructuredPlanDiagnostic(content);
      } else {
        sendMessage(content);
      }
    },
    [input, isKorteksMode, isPlanMode, sendMessage, sendKorteksMessage, startStructuredPlanDiagnostic]
  );

  // ── Quiz answer callback ───────────────────────────────────────────────
  const handleQuizFlowComplete = useCallback(
    (completion: PlanCompletion) => {
      if (!completion.planGenerated) return;

      setPlanFlowStage("done");
      setPlanFlowDetail("Plan hazır. Sol menüdeki öğrenme yolundan ilk derse geçebilirsin.");
      onTopicsRefresh();

      const scoreLine = completion.skipped
        ? "Seviye testi atlandı; Orka sıfırdan başlayan güvenli bir plan üretti."
        : typeof completion.score === "number" && typeof completion.total === "number"
          ? `Seviye testi tamamlandı: ${completion.score}/${completion.total}.`
          : "Seviye testi tamamlandı.";

      setMessages((prev) => [
        ...prev,
        {
          id: `plan-ready-${Date.now()}`,
          role: "ai",
          type: "text",
          content:
            `${scoreLine}\n\nPlan oluştu. Şimdi sol menüdeki öğrenme yolundan ilk derse geçebilir veya Tutor'a \"ilk derse başla\" yazabilirsin. Quiz cevabın chat mesajı olarak gönderilmedi.`,
          timestamp: new Date(),
        },
      ]);
    },
    [onTopicsRefresh, setMessages]
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

  const assistantMessages = messages.filter((message) => message.role === "ai");
  const latestAssistantMessage = assistantMessages.length > 0 ? assistantMessages[assistantMessages.length - 1] : null;
  const latestMetadata = latestAssistantMessage?.metadata ?? null;
  const canvasArtifacts = assistantMessages.flatMap((message) => message.artifacts ?? []).slice(-4);
  const workspaceState = useLearningWorkspaceState({
    topicId: activeTopic?.id,
    sessionId: sessionId ?? undefined,
    metadata: latestMetadata,
  });

  // ── Session loading spinner ────────────────────────────────────────────
  if (sessionLoading) {
    return (
      <div className="flex-1 flex items-center justify-center h-full" style={{ background: "var(--orka-bg)" }}>
        <div className="flex items-center gap-1.5">
          {[0, 1, 2].map((i) => (
            <div
              key={i}
              className="w-1 h-1 rounded-full animate-pulse"
              style={{ background: "var(--orka-text-4)", animationDelay: `${i * 0.15}s` }}
            />
          ))}
        </div>
      </div>
    );
  }

  const setCommandPrompt = (prompt: string) => {
    setInput(prompt);
    requestAnimationFrame(() => textareaRef.current?.focus());
  };

  return (
    <div className="flex-1 flex flex-col h-full overflow-hidden" style={{ background: "var(--orka-bg)" }}>
      {/* Topic Header */}
      <div
        className="flex-shrink-0 flex items-center justify-between px-5 py-2.5"
        style={{ borderBottom: "1px solid var(--orka-border)" }}
      >
        <div className="flex items-center gap-2">
          {activeTopic ? (
            <>
              <span className="text-sm leading-none">{activeTopic.emoji}</span>
              <span className="text-[13px] font-medium" style={{ color: "var(--orka-text)" }}>
                {activeTopic.title}
              </span>
              {currentSubtopic && (
                <div
                  className="flex items-center gap-1.5 ml-2 pl-2"
                  style={{ borderLeft: "1px solid var(--orka-border)" }}
                >
                  <span className="text-[11px]" style={{ color: "var(--orka-text-4)" }}>›</span>
                  <span className="text-[12px]" style={{ color: "var(--orka-text-3)" }}>{currentSubtopic.title}</span>
                  <div className="flex items-center gap-1 ml-1">
                    <div
                      className="w-12 h-px rounded-full overflow-hidden"
                      style={{ background: "var(--orka-surface-3)" }}
                    >
                      <div
                        className="h-full rounded-full transition-all duration-500"
                        style={{ width: `${currentSubtopic.progress}%`, background: "var(--orka-teal)", opacity: 0.6 }}
                      />
                    </div>
                    <span className="text-[10px]" style={{ color: "var(--orka-text-5)" }}>{currentSubtopic.index}/{currentSubtopic.total}</span>
                  </div>
                </div>
              )}
            </>
          ) : (
            <>
              <OrcaLogo className="w-3.5 h-3.5" style={{ color: "var(--orka-text-4)" }} />
              <span className="text-[13px] font-medium" style={{ color: "var(--orka-text-2)" }}>
                Orka AI
              </span>
            </>
          )}
        </div>
        <div className="flex items-center gap-3">
          <div className="hidden lg:block">
            <ToolCapabilityStrip compact />
          </div>
          {activeTopic && (
            <button
              onClick={() => onOpenWiki(activeTopic.id)}
              className="flex items-center gap-1.5 px-2.5 py-1 rounded-md text-[12px] font-medium transition-colors"
              style={{ color: "var(--orka-text-3)" }}
              onMouseEnter={(e) => {
                (e.currentTarget as HTMLButtonElement).style.color = "var(--orka-text)";
                (e.currentTarget as HTMLButtonElement).style.background = "var(--orka-surface-2)";
              }}
              onMouseLeave={(e) => {
                (e.currentTarget as HTMLButtonElement).style.color = "var(--orka-text-3)";
                (e.currentTarget as HTMLButtonElement).style.background = "transparent";
              }}
            >
              <BookOpen className="w-3.5 h-3.5" />
              Wiki
            </button>
          )}
        </div>
      </div>

      <div className="flex min-h-0 flex-1">
        <div className="flex min-w-0 flex-1 flex-col">
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
                      topicId={activeTopic?.id}
                      sessionId={sessionId ?? undefined}
                      onPlanComplete={handleQuizFlowComplete}
                      onOpenWiki={onOpenWiki}
                      onOpenIDE={onOpenIDE}
                    />
                  ))}
                  {pendingPlanIntent && (
                    <PlanIntentConfirmationCard
                      intent={pendingPlanIntent}
                      isBusy={isThinking}
                      onConfirm={confirmPlanIntent}
                      onRevise={revisePlanIntent}
                      onReset={resetPlanIntent}
                    />
                  )}
                </div>
              )}

              <AnimatePresence>
                {isThinking && (
                  <motion.div
                    initial={{ opacity: 0, y: 6 }}
                    animate={{ opacity: 1, y: 0 }}
                    exit={{ opacity: 0 }}
                    className="flex items-center gap-3 mt-4"
                  >
                    <div
                      className="flex-shrink-0 w-7 h-7 rounded-lg flex items-center justify-center"
                      style={{
                        background: "var(--orka-teal-bg)",
                        border: "1px solid var(--orka-teal-border)",
                      }}
                    >
                      <OrcaLogo className="w-3.5 h-3.5" style={{ color: "var(--orka-teal)" }} animated={true} />
                    </div>
                    <div>
                      {planFlowStage !== "idle" ? (
                        <PlanFlowIndicator stage={planFlowStage} detail={planFlowDetail} />
                      ) : (
                        <ThinkingIndicator state={thinkingState} />
                      )}
                    </div>
                  </motion.div>
                )}
              </AnimatePresence>
            </div>
          </div>

          {/* Input Frame */}
          <div className="flex-shrink-0 pointer-events-none">
            <div className="max-w-3xl mx-auto w-full px-5 pb-6 pt-2 pointer-events-auto">
              <motion.div
                layout
                initial={false}
                className={`glass-panel rounded-xl overflow-hidden transition-all duration-300 ${isThinking ? "opacity-75" : "opacity-100"} ${isPlanMode ? "glow-silver-active" : input.length > 0 ? "glow-silver" : ""}`}
              >
                <div className="px-3.5 py-3 flex items-end gap-2.5">
                  {/* Plus menu */}
                  <div className="relative flex-none">
                    <button
                      onClick={() => setShowAttachmentMenu((prev) => !prev)}
                      className="flex items-center justify-center w-7 h-7 rounded-lg transition-colors"
                      style={{ color: "var(--orka-text-4)" }}
                      onMouseEnter={(e) => (e.currentTarget.style.color = "var(--orka-text-2)")}
                      onMouseLeave={(e) => (e.currentTarget.style.color = "var(--orka-text-4)")}
                      title="Mod sec"
                    >
                      <Plus className="w-4 h-4" />
                    </button>
                    <AnimatePresence>
                      {showAttachmentMenu && (
                        <motion.div
                          initial={{ opacity: 0, y: 8, scale: 0.96 }}
                          animate={{ opacity: 1, y: 0, scale: 1 }}
                          exit={{ opacity: 0, y: 8, scale: 0.96 }}
                          transition={{ duration: 0.12 }}
                          className="absolute bottom-full left-0 mb-2 w-52 rounded-xl overflow-hidden z-50"
                          style={{
                            background: "var(--orka-surface-2)",
                            border: "1px solid var(--orka-border-2)",
                            boxShadow: "var(--orka-shadow-lg)",
                          }}
                        >
                          <div className="p-1">
                            <button
                              onClick={() => { setShowAttachmentMenu(false); toast.success("Dosya ekleme yakinda."); }}
                              className="w-full flex items-center gap-2.5 px-3 py-2 text-[13px] rounded-lg text-left transition-colors"
                              style={{ color: "var(--orka-text-2)" }}
                              onMouseEnter={(e) => (e.currentTarget.style.background = "var(--orka-surface-3)")}
                              onMouseLeave={(e) => (e.currentTarget.style.background = "transparent")}
                            >
                              <FileText className="w-3.5 h-3.5 flex-none" style={{ color: "var(--orka-text-4)" }} />
                              Dosya Ekle
                            </button>
                            <div className="h-px mx-2 my-1" style={{ background: "var(--orka-border)" }} />
                            <button
                              onClick={() => { setIsPlanMode((p) => !p); setIsKorteksMode(false); setShowAttachmentMenu(false); }}
                              className="w-full flex items-center gap-2.5 px-3 py-2 text-[13px] rounded-lg text-left transition-colors"
                              style={{
                                color: isPlanMode ? "var(--orka-teal)" : "var(--orka-text-2)",
                                background: isPlanMode ? "var(--orka-teal-bg)" : "transparent",
                              }}
                              onMouseEnter={(e) => { if (!isPlanMode) (e.currentTarget as HTMLButtonElement).style.background = "var(--orka-surface-3)"; }}
                              onMouseLeave={(e) => { if (!isPlanMode) (e.currentTarget as HTMLButtonElement).style.background = "transparent"; }}
                            >
                              <Sparkles className="w-3.5 h-3.5 flex-none" />
                              <span className="flex-1">Planlama Modu</span>
                              {isPlanMode && <Check className="w-3.5 h-3.5" />}
                            </button>
                            <button
                              onClick={() => { setIsKorteksMode((p) => !p); setIsPlanMode(false); setShowAttachmentMenu(false); }}
                              className="w-full flex items-center gap-2.5 px-3 py-2 text-[13px] rounded-lg text-left transition-colors"
                              style={{
                                color: isKorteksMode ? "var(--orka-teal)" : "var(--orka-text-2)",
                                background: isKorteksMode ? "var(--orka-teal-bg)" : "transparent",
                              }}
                              onMouseEnter={(e) => { if (!isKorteksMode) (e.currentTarget as HTMLButtonElement).style.background = "var(--orka-surface-3)"; }}
                              onMouseLeave={(e) => { if (!isKorteksMode) (e.currentTarget as HTMLButtonElement).style.background = "transparent"; }}
                            >
                              <Globe className="w-3.5 h-3.5 flex-none" />
                              <span className="flex-1">Derin Arastirma</span>
                              {isKorteksMode && <Check className="w-3.5 h-3.5" />}
                            </button>
                          </div>
                        </motion.div>
                      )}
                    </AnimatePresence>
                  </div>

                  {/* Textarea column */}
                  <div className="flex-1 flex flex-col min-h-[36px]">
                    <AnimatePresence>
                      {(isPlanMode || isKorteksMode) && (
                        <motion.div initial={{ opacity: 0, y: 4 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0 }} className="mb-1">
                          <span
                            className="inline-block text-[10px] font-medium tracking-wider uppercase px-2 py-0.5 rounded-full w-max"
                            style={{
                              background: "var(--orka-teal-bg)",
                              color: "var(--orka-teal)",
                              border: "1px solid var(--orka-teal-border)",
                            }}
                          >
                            {isKorteksMode ? "Derin Arastirma" : "Planlama Modu"}
                          </span>
                        </motion.div>
                      )}
                    </AnimatePresence>
                    <textarea
                      id="tour-chat-input"
                      ref={textareaRef}
                      value={input}
                      onChange={handleTextareaChange}
                      onKeyDown={handleKeyDown}
                      placeholder={
                        isKorteksMode
                          ? "Arastirmami istedigin konuyu yaz..."
                          : isPlanMode
                          ? "Bir konu ver; niyeti netlestireyim..."
                          : "Bir sey sor..."
                      }
                      rows={1}
                      disabled={isThinking}
                      className="w-full bg-transparent resize-none outline-none text-[13.5px] leading-relaxed max-h-[200px]"
                      style={{
                        color: "var(--orka-text)",
                      }}
                    />
                  </div>

                  {/* Send button */}
                  <button
                    onClick={() => handleSend()}
                    disabled={!input.trim() || isThinking}
                    className="flex-none flex items-center justify-center w-7 h-7 rounded-lg transition-all duration-150"
                    style={{
                      background: !input.trim() || isThinking ? "var(--orka-surface-3)" : "var(--orka-teal)",
                      color: !input.trim() || isThinking ? "var(--orka-text-5)" : "#041210",
                      cursor: !input.trim() || isThinking ? "not-allowed" : "pointer",
                    }}
                  >
                    <ArrowUp className="w-3.5 h-3.5" strokeWidth={2.5} />
                  </button>
                </div>

                {/* Quick actions */}
                <div
                  className="hidden md:flex flex-wrap gap-1.5 px-3.5 pb-3 pt-0"
                  style={{ borderTop: "1px solid var(--orka-border-3)" }}
                >
                  {[
                    { label: "Kaynaktan acikla", prompt: "Bu konuyu kaynaklarima dayanarak acikla." },
                    { label: "Ornek ver", prompt: "Bunu gercek hayattan bir ornekle anlat." },
                    { label: "Gorselleştir", prompt: "Bunu bir diagram veya tablo ile gostermeye calis." },
                    { label: "Pratik soru", prompt: "Bu kavram icin kisa bir pratik soru uret." },
                  ].map((action) => (
                    <button
                      key={action.label}
                      type="button"
                      onClick={() => setCommandPrompt(action.prompt)}
                      className="rounded-lg px-2.5 py-1 text-[11px] font-medium transition-colors"
                      style={{
                        border: "1px solid var(--orka-border)",
                        color: "var(--orka-text-3)",
                        background: "transparent",
                      }}
                      onMouseEnter={(e) => {
                        (e.currentTarget as HTMLButtonElement).style.color = "var(--orka-text-2)";
                        (e.currentTarget as HTMLButtonElement).style.background = "var(--orka-surface-2)";
                      }}
                      onMouseLeave={(e) => {
                        (e.currentTarget as HTMLButtonElement).style.color = "var(--orka-text-3)";
                        (e.currentTarget as HTMLButtonElement).style.background = "transparent";
                      }}
                    >
                      {action.label}
                    </button>
                  ))}
                </div>
              </motion.div>
            </div>
          </div>
        </div>

        <aside className="hidden min-h-0 w-[360px] shrink-0 2xl:flex" style={{ borderLeft: "1px solid var(--orka-border)", background: "var(--orka-surface)" }}>
          <ArtifactCanvas artifacts={canvasArtifacts} learningArtifacts={workspaceState.recentArtifacts} />
        </aside>
        <AgentStatusRail
          metadata={latestMetadata}
          sessionId={sessionId ?? undefined}
          topicTitle={activeTopic?.title}
          workspaceState={workspaceState}
        />
      </div>
    </div>
  );
}

// ── Sub-components ─────────────────────────────────────────────────────────

function PlanIntentConfirmationCard({
  intent,
  isBusy,
  onConfirm,
  onRevise,
  onReset,
}: {
  intent: StudyIntentPreview;
  isBusy: boolean;
  onConfirm: (intent: StudyIntentPreview) => void;
  onRevise: (correction: string) => void;
  onReset: () => void;
}) {
  const [isEditing, setIsEditing] = useState(false);
  const [draft, setDraft] = useState(`${intent.mainTopic} ${intent.focusArea}`.trim());

  useEffect(() => {
    setDraft(`${intent.mainTopic} ${intent.focusArea}`.trim());
    setIsEditing(false);
  }, [intent.intentRequestId, intent.mainTopic, intent.focusArea]);

  return (
    <motion.div
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      className="my-4 ml-10 rounded-xl p-4"
      style={{
        background: "var(--orka-surface)",
        border: "1px solid var(--orka-border)",
        boxShadow: "var(--orka-shadow-md)",
      }}
    >
      <div className="mb-4 flex items-start gap-3">
        <div
          className="mt-0.5 rounded-lg p-1.5 flex-none"
          style={{ background: "var(--orka-teal-bg)", color: "var(--orka-teal)" }}
        >
          <Sparkles className="h-3.5 w-3.5" />
        </div>
        <div>
          <div className="text-[10px] font-semibold uppercase tracking-widest" style={{ color: "var(--orka-text-4)" }}>Niyet analizi</div>
          <h3 className="mt-1 text-[15px] font-semibold" style={{ color: "var(--orka-text)" }}>Korteks&apos;e gitmeden once onayla</h3>
          <p className="mt-1 text-[13px] leading-5" style={{ color: "var(--orka-text-3)" }}>
            {intent.confirmationText || "Calisma niyetini ayirdim. Onay verirsen Korteks arastirmasi baslayacak."}
          </p>
          <div
            className="mt-2 inline-flex rounded-md px-2.5 py-1 text-[11px] font-medium"
            style={{
              background: "var(--orka-amber-bg)",
              color: "var(--orka-amber)",
              border: "1px solid rgba(245,158,11,0.18)",
            }}
          >
            Onay yoksa arastirma, quiz ve plan baslamaz
          </div>
        </div>
      </div>

      <div className="grid gap-2 sm:grid-cols-2">
        <IntentField label="Ana alan" value={intent.mainTopic} />
        <IntentField label="Odak konu" value={intent.focusArea} />
        <IntentField label="Amac" value={intent.studyGoal} />
        <IntentField label="Korteks niyeti" value={intent.researchIntent} mono />
      </div>

      {intent.clarifyingNotes?.length > 0 && (
        <div
          className="mt-3 rounded-lg px-3 py-2.5 text-[12px] leading-5"
          style={{
            background: "var(--orka-surface-2)",
            border: "1px solid var(--orka-border-3)",
            color: "var(--orka-text-3)",
          }}
        >
          {intent.clarifyingNotes.slice(0, 3).map((note) => (
            <div key={note}>· {note}</div>
          ))}
        </div>
      )}

      <div
        className="mt-3 grid gap-1.5 rounded-lg p-2.5 text-[12px] sm:grid-cols-4"
        style={{
          background: "var(--orka-surface-2)",
          border: "1px solid var(--orka-border-3)",
        }}
      >
        {[
          ["1", "Niyet onayi"],
          ["2", "Korteks"],
          ["3", "Seviye testi"],
          ["4", "Kisisel plan"],
        ].map(([step, label]) => (
          <div key={step} className="flex items-center gap-2 rounded-md px-2.5 py-1.5" style={{ background: "var(--orka-surface-3)" }}>
            <span
              className="flex h-5 w-5 items-center justify-center rounded-full text-[10px] font-semibold flex-none"
              style={{ background: "var(--orka-teal-bg)", color: "var(--orka-teal)" }}
            >
              {step}
            </span>
            <span style={{ color: "var(--orka-text-3)" }}>{label}</span>
          </div>
        ))}
      </div>

      {isEditing && (
        <div className="mt-3">
          <label className="mb-1.5 block text-[10px] font-semibold uppercase tracking-widest" style={{ color: "var(--orka-text-4)" }}>
            Duzeltme
          </label>
          <textarea
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            className="min-h-[80px] w-full rounded-lg px-3 py-2.5 text-[13px] outline-none"
            style={{
              background: "var(--orka-surface-2)",
              border: "1px solid var(--orka-border)",
              color: "var(--orka-text)",
            }}
            placeholder="Ornek: Java algoritmalar calismak istiyorum"
          />
        </div>
      )}

      <div className="mt-4 flex flex-wrap items-center gap-2">
        <button
          onClick={() => onConfirm(intent)}
          disabled={isBusy}
          className="inline-flex items-center gap-1.5 rounded-lg px-3.5 py-2 text-[12.5px] font-medium transition disabled:cursor-not-allowed disabled:opacity-50"
          style={{ background: "var(--orka-teal)", color: "#041210" }}
        >
          <CheckCircle2 className="h-3.5 w-3.5" />
          Onayla ve arastir
        </button>
        <button
          onClick={() => (isEditing ? onRevise(draft) : setIsEditing(true))}
          disabled={isBusy}
          className="inline-flex items-center gap-1.5 rounded-lg px-3.5 py-2 text-[12.5px] font-medium transition disabled:cursor-not-allowed disabled:opacity-50"
          style={{
            background: "var(--orka-surface-2)",
            border: "1px solid var(--orka-border)",
            color: "var(--orka-text-2)",
          }}
        >
          <Edit3 className="h-3.5 w-3.5" />
          {isEditing ? "Analiz et" : "Duzelt"}
        </button>
        <button
          onClick={onReset}
          disabled={isBusy}
          className="inline-flex items-center gap-2 rounded-lg px-3.5 py-2 text-[12.5px] font-medium transition disabled:cursor-not-allowed disabled:opacity-50"
          style={{
            background: "var(--orka-surface-3)",
            border: "1px solid var(--orka-border)",
            color: "var(--orka-text-3)",
          }}
        >
          <RotateCcw className="h-3.5 w-3.5" />
          Yeniden yaz
        </button>
      </div>
    </motion.div>
  );
}

function IntentField({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div
      className="rounded-xl px-4 py-3"
      style={{
        background: "var(--orka-surface-2)",
        border: "1px solid var(--orka-border)",
      }}
    >
      <div className="text-[10px] font-semibold uppercase tracking-[0.16em] mb-1" style={{ color: "var(--orka-text-4)" }}>{label}</div>
      <div className={`text-[13px] font-medium ${mono ? "font-mono text-[12px]" : ""}`} style={{ color: "var(--orka-text-2)" }}>
        {value || "-"}
      </div>
    </div>
  );
}

function PlanFlowIndicator({ stage, detail }: { stage: PlanFlowStage; detail?: string | null }) {
  const steps: Array<{ id: PlanFlowStage; label: string; body: string }> = [
    { id: "intent", label: "Niyet ayriliyor", body: "Ham istek konu, odak ve arastirma niyetine cevrilir; onay olmadan Korteks calismaz." },
    { id: "topic", label: "Hedef okunuyor", body: "Konu, hedef ve baslangic niyeti ayriliyor." },
    { id: "research", label: "Bagiam taranıyor", body: "Kaynak, wiki, YouTube pedagojisi ve guvenli arac sinyalleri kontrol ediliyor." },
    { id: "quiz", label: "Seviye testi kuruluyor", body: "Sorular tek quiz yuzeyinde acilir; chate sistem komutu dusmez." },
    { id: "plan", label: "Ogrenme yolu uretiliyor", body: "Cevaplar, zayif kavramlar, IDE pratikleri ve tekrar baskisi plana cevrilir." },
  ];
  const currentIndex = Math.max(0, steps.findIndex((step) => step.id === stage));

  return (
    <div
      className="rounded-xl px-4 py-3"
      style={{
        background: "var(--orka-surface)",
        border: "1px solid var(--orka-border)",
        boxShadow: "var(--orka-shadow-sm)",
      }}
    >
      <div className="mb-3 text-[11px] font-semibold uppercase tracking-wider" style={{ color: "var(--orka-teal)" }}>Plan motoru calisiyor</div>
      <div className="grid gap-2 sm:grid-cols-2">
        {steps.map((step, index) => {
          const done = index < currentIndex || stage === "done";
          const active = index === currentIndex && stage !== "done" && stage !== "error";
          return (
            <div
              key={step.id}
              className="rounded-lg px-3 py-2 text-[11px]"
              style={{
                background: active
                  ? "rgba(110,215,206,0.07)"
                  : done
                    ? "rgba(74,222,128,0.06)"
                    : "var(--orka-surface-2)",
                border: active
                  ? "1px solid rgba(110,215,206,0.22)"
                  : done
                    ? "1px solid rgba(74,222,128,0.18)"
                    : "1px solid var(--orka-border-3)",
                color: active ? "var(--orka-teal)" : done ? "var(--orka-green)" : "var(--orka-text-4)",
              }}
            >
              <div className="font-semibold">{done ? "✓ " : active ? "• " : ""}{step.label}</div>
              <div className="mt-0.5 leading-4 opacity-70 text-[10px]">{step.body}</div>
            </div>
          );
        })}
      </div>
      {detail && <p className="mt-3 text-[11px] leading-5" style={{ color: "var(--orka-text-4)" }}>{detail}</p>}
    </div>
  );
}

function WelcomeState({ onPromptClick }: { onPromptClick: (p: string) => void }) {
  const { t } = useLanguage();
  const focus = localStorage.getItem("orka_study_focus") || "general";
  const focusPrompt = (() => {
    if (focus === "kpss") return t("starter_prompt_kpss");
    if (focus === "yks") return t("starter_prompt_yks");
    if (focus === "language") return t("starter_prompt_language");
    if (focus === "software") return t("starter_prompt_software");
    if (focus === "math") return t("starter_prompt_math");
    return t("starter_prompt_general");
  })();
  const starterPrompts = [
    { label: t("starter_learn_topic"), prompt: focusPrompt, hint: t("starter_hint_focus") },
    { label: t("starter_source"), prompt: t("starter_prompt_source"), hint: t("starter_hint_demo") },
    { label: t("starter_code"), prompt: t("starter_prompt_code"), hint: t("starter_hint_demo") },
    { label: t("starter_review"), prompt: t("starter_prompt_review"), hint: t("starter_hint_demo") },
  ];

  return (
    <div className="flex flex-col items-center justify-center min-h-[40vh] py-8">
      <motion.div
        initial={{ opacity: 0, y: 12 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.4, ease: "easeOut" }}
        className="text-center w-full max-w-[560px]"
      >
        {/* Logo mark */}
        <div className="flex items-center justify-center mb-6">
          <div
            className="w-11 h-11 rounded-2xl flex items-center justify-center"
            style={{
              background: "rgba(110,215,206,0.10)",
              border: "1px solid rgba(110,215,206,0.22)",
            }}
          >
            <OrcaLogo className="w-5.5 h-5.5" style={{ color: "#6ed7ce" }} />
          </div>
        </div>

        <h1 className="text-[22px] font-semibold mb-2 tracking-tight" style={{ color: "var(--orka-text)" }}>
          {t("tutor_welcome_title")}
        </h1>
        <p className="text-[13px] mb-8 max-w-[420px] mx-auto leading-relaxed" style={{ color: "var(--orka-text-4)" }}>
          {t("tutor_welcome_body")}
        </p>

        {/* Starter prompt grid */}
        <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 mb-6">
          {starterPrompts.map((item) => (
            <button
              key={item.label}
              onClick={() => onPromptClick(item.prompt)}
              className="rounded-xl px-4 py-3 text-left text-[12.5px] font-medium transition-all"
              style={{
                background: "var(--orka-surface-2)",
                border: "1px solid var(--orka-border)",
                color: "var(--orka-text-2)",
              }}
              onMouseEnter={(e) => {
                (e.currentTarget as HTMLButtonElement).style.borderColor = "rgba(110,215,206,0.22)";
                (e.currentTarget as HTMLButtonElement).style.color = "var(--orka-text)";
              }}
              onMouseLeave={(e) => {
                (e.currentTarget as HTMLButtonElement).style.borderColor = "var(--orka-border)";
                (e.currentTarget as HTMLButtonElement).style.color = "var(--orka-text-2)";
              }}
            >
              {item.label}
              <span className="mt-1 block text-[11px] font-normal leading-4" style={{ color: "var(--orka-text-5)" }}>
                {item.hint}
              </span>
            </button>
          ))}
        </div>

        <div
          className="flex items-center justify-center gap-2 py-1.5 px-3 rounded-full w-fit mx-auto"
          style={{ border: "1px solid var(--orka-border-3)" }}
        >
          <Bell className="w-3 h-3" style={{ color: "var(--orka-text-5)" }} />
          <span className="text-[10px] font-medium tracking-wide uppercase" style={{ color: "var(--orka-text-5)" }}>
            {t("small_step_first")}
          </span>
        </div>
      </motion.div>
    </div>
  );
}
