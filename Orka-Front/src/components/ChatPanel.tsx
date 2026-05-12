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
import { Send, Sparkles, BookOpen, Bell, Globe, CheckCircle2, Edit3, RotateCcw } from "lucide-react";
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
          sessionId: sessionId ?? undefined,
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
                // First append the decoded chunk to our content buffer
                currentContent += data.replaceAll("[NEWLINE]", "\n");
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

  // ── Session loading spinner ────────────────────────────────────────────
  if (sessionLoading) {
    return (
      <div className="flex-1 flex items-center justify-center bg-[#f7f9fa] h-full">
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

  const assistantMessages = messages.filter((message) => message.role === "ai");
  const latestAssistantMessage = assistantMessages.length > 0 ? assistantMessages[assistantMessages.length - 1] : null;
  const latestMetadata = latestAssistantMessage?.metadata ?? null;
  const canvasArtifacts = assistantMessages.flatMap((message) => message.artifacts ?? []).slice(-4);
  const setCommandPrompt = (prompt: string) => {
    setInput(prompt);
    requestAnimationFrame(() => textareaRef.current?.focus());
  };

  return (
    <div className="flex-1 flex flex-col bg-[#f7f9fa] h-full overflow-hidden">
      {/* Topic Header — topic varsa adını, yoksa AI assistant markasını göster */}
      <div className="flex-shrink-0 flex items-center justify-between px-6 py-3 border-b border-[#526d82]/10/50">
        <div className="flex items-center gap-2.5">
          {activeTopic ? (
            <>
              <span className="text-base">{activeTopic.emoji}</span>
              <span className="text-sm font-medium text-[#172033]">
                {activeTopic.title}
              </span>
              {currentSubtopic && (
                <div className="flex items-center gap-2 ml-2 pl-2 border-l border-[#526d82]/10">
                  <span className="text-[11px] text-[#667085]">{'>'}</span>
                  <span className="text-xs text-[#344054]">{currentSubtopic.title}</span>
                  <div className="flex items-center gap-1.5 ml-2">
                    <div className="w-16 h-1 bg-[#eef1f3] rounded-full overflow-hidden">
                      <div className="h-full bg-emerald-500/60 transition-all duration-500" style={{ width: `${currentSubtopic.progress}%` }} />
                    </div>
                    <span className="text-[9px] text-[#8ba8b5]">{currentSubtopic.index}/{currentSubtopic.total}</span>
                  </div>
                </div>
              )}
            </>
          ) : (
            <>
              <OrcaLogo className="w-4 h-4 text-[#667085]" />
              <span className="text-sm font-medium text-[#344054]">
                Orka AI
              </span>
            </>
          )}
        </div>
        <div className="flex items-center gap-4">
          <div className="hidden lg:block">
            <ToolCapabilityStrip compact />
          </div>
          {activeTopic && (
            <button
              onClick={() => onOpenWiki(activeTopic.id)}
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs font-medium text-[#344054] hover:text-[#172033] hover:bg-[#eef1f3] transition-colors duration-150"
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
            <div className="bg-[#f7f9fa]/95 backdrop-blur border border-[#526d82]/10 rounded-full px-5 py-2 shadow-lg shadow-sm flex items-center gap-3">
              <BookOpen className="w-4 h-4 text-emerald-500" />
              <div className="flex items-center gap-2 text-xs font-medium">
                <span className="text-[#344054]">{activeTopic?.title}</span>
                <span className="text-[#8ba8b5]">❯</span>
                <span className="text-[#172033]">{currentSubtopic.title}</span>
              </div>

              <div className="flex items-center gap-2 ml-4 pl-4 border-l border-[#526d82]/10">
                <div className="text-[10px] text-[#667085] font-mono">
                  {currentSubtopic.index} / {currentSubtopic.total}
                </div>
                <div className="w-16 h-1.5 bg-[#eef1f3] rounded-full overflow-hidden">
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
                    initial={{ opacity: 0, y: 8 }}
                    animate={{ opacity: 1, y: 0 }}
                    exit={{ opacity: 0 }}
                    className="flex items-center gap-3 mt-4"
                  >
                    <div className="flex-shrink-0 w-8 h-8 rounded-full bg-[#eef1f3] border border-[#526d82]/10 flex items-center justify-center">
                      <OrcaLogo className="w-4 h-4 text-[#344054]" animated={true} />
                    </div>
                    <div className="pt-0">
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

          {/* Floating Input Frame — agent command bar */}
          <div className="flex-shrink-0 relative pointer-events-none">
            <div className="max-w-3xl mx-auto w-full px-6 pb-8 pt-2 pointer-events-auto">
          <motion.div
            layout
            initial={false}
            className={`
              glass-panel rounded-2xl border transition-all duration-500 shadow-2xl
              ${isThinking ? "opacity-90 grayscale-[0.2]" : "opacity-100"}
              ${isPlanMode ? "glow-silver-active" : (input.length > 0 ? "glow-silver border-zinc-500/20" : "border-[#526d82]/10 shadow-sm")}
              bg-[#f7f9fa]/90 backdrop-blur-xl overflow-hidden
            `}
          >
            <div className="px-4 pt-3 flex flex-col">
              <textarea
                id="tour-chat-input"
                ref={textareaRef}
                value={input}
                onChange={handleTextareaChange}
                onKeyDown={handleKeyDown}
                placeholder={
                  isKorteksMode
                    ? "Araştırmamı istediğin konuyu yaz; web'de derinlemesine araştırayım..."
                    : isPlanMode
                    ? "Bana bir konu ver; önce niyeti netleştireyim, sonra Korteks araştırsın..."
                    : "Bir şey sor veya müfredat oluşturmak için Plan Modu'nu aç..."
                }
                rows={1}
                disabled={isThinking}
                className="w-full bg-transparent resize-none outline-none text-[14px] text-[#172033] placeholder-[#8ba8b5] leading-relaxed min-h-[44px] max-h-[200px] py-1"
              />

              <div className="flex items-center justify-between pb-3 pt-2 border-t border-[#526d82]/10 mt-1">
                <div className="flex items-center gap-2">
                  <button
                    onClick={() => { setIsPlanMode((prev) => !prev); setIsKorteksMode(false); }}
                    className={`
                      flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[11px] font-semibold transition-all duration-200 border
                      ${isPlanMode
                        ? "bg-[#dcecf3] border-[#9ec7d9]/60 text-[#172033] shadow-sm"
                        : "bg-[#eef1f3] border-[#526d82]/10 text-[#667085] hover:text-[#344054] hover:border-[#526d82]/20"}
                    `}
                    title="Plan Modu - önce niyet analizi, sonra Korteks araştırması"
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
                        : "bg-[#eef1f3] border-[#526d82]/10 text-[#667085] hover:text-[#344054] hover:border-[#526d82]/20"}
                    `}
                    title="Korteks - web'de derin araştırma yapar ve wiki'ye kaydeder"
                  >
                    <Globe className={`w-3.5 h-3.5 ${isKorteksMode ? "text-emerald-400" : ""}`} />
                    <span>Korteks</span>
                  </button>

                  {(isPlanMode || isKorteksMode) && (
                    <motion.span
                      initial={{ opacity: 0, x: -10 }}
                      animate={{ opacity: 1, x: 0 }}
                      className={`text-[10px] font-medium tracking-tight uppercase ${isKorteksMode ? "text-emerald-600" : "text-[#344054]"}`}
                    >
	                      {isKorteksMode ? "Korteks aktif" : "Plan modu aktif - quiz chat'e karışmaz"}
                    </motion.span>
                  )}
                </div>

                <div className="flex items-center gap-3">
                   <span className="text-[10px] text-[#8ba8b5] hidden sm:inline-block">
                     {input.length > 0 ? "Shift+Enter yeni satır" : ""}
                   </span>
                   <button
                    onClick={() => handleSend()}
                    disabled={!input.trim() || isThinking}
                    className={`
                      flex items-center justify-center w-8 h-8 rounded-lg transition-all duration-200
                      ${!input.trim() || isThinking
                        ? "bg-[#eef1f3] text-[#8ba8b5] opacity-50 cursor-not-allowed"
                        : "bg-[#172033] hover:bg-[#2d5870] text-white shadow-lg shadow-sm"}
                    `}
                  >
                    <Send className="w-4 h-4" />
                  </button>
                </div>
              </div>
              <div className="hidden flex-wrap gap-2 border-t border-[#526d82]/10 pb-3 pt-2 md:flex">
                {[
                  { label: "Kaynağa göre sor", prompt: "Bu konuyu kaynaklarıma dayanarak açıkla ve kaynakta yoksa net söyle." },
                  { label: "Örnekle anlat", prompt: "Bunu gerçek hayattan bir örnekle, sonra kısa bir kontrol sorusuyla anlat." },
                  { label: "Görselleştir", prompt: "Bunu bir diagram, tablo veya zaman çizelgesiyle görselleştir." },
                  { label: "Pratik üret", prompt: "Bu kavram için seviyeme uygun kısa bir pratik sorusu üret." },
                ].map((action) => (
                  <button
                    key={action.label}
                    type="button"
                    onClick={() => setCommandPrompt(action.prompt)}
                    className="rounded-full border border-[#526d82]/12 bg-white/72 px-3 py-1 text-[10px] font-black text-[#52768a] transition hover:border-[#9ec7d9] hover:text-[#172033]"
                  >
                    {action.label}
                  </button>
                ))}
              </div>
            </div>
          </motion.div>
          <p className="text-[9px] text-[#8ba8b5] mt-3 text-center tracking-wide uppercase opacity-50 font-medium">
            Orka AI · canlı öğrenme ajanı · kaynak, araç ve kanıt izlenir
          </p>
            </div>
          </div>
        </div>

        <aside className="hidden min-h-0 w-[360px] shrink-0 border-l border-[#526d82]/10 bg-[#fbfcfd] 2xl:flex">
          <ArtifactCanvas artifacts={canvasArtifacts} />
        </aside>
        <AgentStatusRail
          metadata={latestMetadata}
          sessionId={sessionId ?? undefined}
          topicTitle={activeTopic?.title}
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
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      className="my-4 ml-12 rounded-3xl border border-[#9ec7d9]/40 bg-white/86 p-5 shadow-[0_18px_48px_rgba(66,91,112,0.14)]"
    >
      <div className="mb-4 flex items-start gap-3">
        <div className="mt-0.5 rounded-2xl bg-[#dcecf3] p-2 text-[#2d5870]">
          <Sparkles className="h-4 w-4" />
        </div>
        <div>
          <div className="text-xs font-black uppercase tracking-[0.18em] text-[#667085]">Niyet analizi</div>
          <h3 className="mt-1 text-lg font-black text-[#172033]">Korteks'e gitmeden once bunu onayla</h3>
          <p className="mt-1 text-sm leading-6 text-[#526d82]">
            {intent.confirmationText || "Calisma niyetini ayirdim. Onay verirsen Korteks arastirmasi baslayacak."}
          </p>
          <div className="mt-2 inline-flex rounded-full border border-amber-200 bg-amber-50 px-3 py-1 text-[11px] font-black text-amber-800">
            Onay yoksa arastirma, quiz ve plan baslamaz
          </div>
        </div>
      </div>

      <div className="grid gap-3 sm:grid-cols-2">
        <IntentField label="Ana alan" value={intent.mainTopic} />
        <IntentField label="Odak konu" value={intent.focusArea} />
        <IntentField label="Amac" value={intent.studyGoal} />
        <IntentField label="Korteks arastirma niyeti" value={intent.researchIntent} mono />
      </div>

      {intent.clarifyingNotes?.length > 0 && (
        <div className="mt-4 rounded-2xl border border-[#526d82]/10 bg-[#f7f9fa]/70 px-4 py-3 text-xs leading-5 text-[#667085]">
          {intent.clarifyingNotes.slice(0, 3).map((note) => (
            <div key={note}>• {note}</div>
          ))}
        </div>
      )}

      <div className="mt-4 grid gap-2 rounded-2xl border border-[#9ec7d9]/25 bg-[#f7fbfd] p-3 text-xs text-[#526d82] sm:grid-cols-4">
        {[
          ["1", "Niyet onayi"],
          ["2", "Korteks arastirmasi"],
          ["3", "15-25 soru seviye testi"],
          ["4", "Kişisel plan"],
        ].map(([step, label]) => (
          <div key={step} className="flex items-center gap-2 rounded-xl bg-white/70 px-3 py-2">
            <span className="flex h-6 w-6 items-center justify-center rounded-full bg-[#dcecf3] text-[11px] font-black text-[#2d5870]">
              {step}
            </span>
            <span className="font-bold">{label}</span>
          </div>
        ))}
      </div>

      {isEditing && (
        <div className="mt-4">
          <label className="mb-2 block text-xs font-black uppercase tracking-[0.14em] text-[#667085]">
            Duzeltme
          </label>
          <textarea
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            className="min-h-[86px] w-full rounded-2xl border border-[#526d82]/15 bg-[#f7f9fa] px-4 py-3 text-sm text-[#172033] outline-none focus:border-[#9ec7d9] focus:ring-2 focus:ring-[#9ec7d9]/25"
            placeholder="Ornek: Java programlamada algoritmalar ve veri yapilari calismak istiyorum"
          />
        </div>
      )}

      <div className="mt-5 flex flex-wrap items-center gap-2">
        <button
          onClick={() => onConfirm(intent)}
          disabled={isBusy}
          className="inline-flex items-center gap-2 rounded-full bg-[#172033] px-4 py-2 text-xs font-black text-white shadow-sm transition hover:bg-[#2d5870] disabled:cursor-not-allowed disabled:opacity-50"
        >
          <CheckCircle2 className="h-4 w-4" />
          Onayla ve araştır
        </button>
        <button
          onClick={() => (isEditing ? onRevise(draft) : setIsEditing(true))}
          disabled={isBusy}
          className="inline-flex items-center gap-2 rounded-full border border-[#526d82]/15 bg-[#f7f9fa] px-4 py-2 text-xs font-black text-[#344054] transition hover:border-[#9ec7d9] disabled:cursor-not-allowed disabled:opacity-50"
        >
          <Edit3 className="h-4 w-4" />
          {isEditing ? "Duzeltmeyi analiz et" : "Duzelt"}
        </button>
        <button
          onClick={onReset}
          disabled={isBusy}
          className="inline-flex items-center gap-2 rounded-full border border-[#526d82]/10 bg-white px-4 py-2 text-xs font-black text-[#667085] transition hover:text-[#172033] disabled:cursor-not-allowed disabled:opacity-50"
        >
          <RotateCcw className="h-4 w-4" />
          Yeniden yaz
        </button>
      </div>
    </motion.div>
  );
}

function IntentField({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="rounded-2xl border border-[#526d82]/10 bg-[#f7f9fa]/75 px-4 py-3">
      <div className="text-[10px] font-black uppercase tracking-[0.16em] text-[#667085]">{label}</div>
      <div className={`mt-1 text-sm font-bold text-[#172033] ${mono ? "font-mono text-[12px]" : ""}`}>
        {value || "-"}
      </div>
    </div>
  );
}
function PlanFlowIndicator({ stage, detail }: { stage: PlanFlowStage; detail?: string | null }) {
  const steps: Array<{ id: PlanFlowStage; label: string; body: string }> = [
    { id: "intent", label: "Niyet ayrılıyor", body: "Ham istek konu, odak ve araştırma niyetine çevrilir; onay olmadan Korteks çalışmaz." },
    { id: "topic", label: "Hedef okunuyor", body: "Konu, hedef ve baslangic niyeti ayriliyor." },
    { id: "research", label: "Bağlam taranıyor", body: "Kaynak, wiki, YouTube pedagojisi ve güvenli araç sinyalleri kontrol ediliyor." },
    { id: "quiz", label: "Seviye testi kuruluyor", body: "Sorular tek quiz yüzeyinde açılır; chat'e sistem komutu düşmez." },
    { id: "plan", label: "Öğrenme yolu üretiliyor", body: "Cevaplar, zayıf kavramlar, IDE pratikleri ve tekrar baskısı plana çevrilir." },
  ];
  const currentIndex = Math.max(0, steps.findIndex((step) => step.id === stage));

  return (
    <div className="rounded-2xl border border-[#9ec7d9]/35 bg-white/76 px-4 py-3 shadow-[0_14px_36px_rgba(66,91,112,0.12)] backdrop-blur-xl">
      <div className="mb-2 text-xs font-black text-[#172033]">Plan motoru calisiyor</div>
      <div className="grid gap-2 sm:grid-cols-2">
        {steps.map((step, index) => {
          const done = index < currentIndex || stage === "done";
          const active = index === currentIndex && stage !== "done" && stage !== "error";
          return (
            <div
              key={step.id}
              className={`rounded-xl border px-3 py-2 text-[11px] ${
                active
                  ? "border-[#9ec7d9] bg-[#dcecf3]/72 text-[#172033]"
                  : done
                    ? "border-emerald-200 bg-emerald-50 text-emerald-800"
                    : "border-[#526d82]/10 bg-[#f7f9fa]/72 text-[#667085]"
              }`}
            >
              <div className="font-black">{done ? "✓ " : active ? "• " : ""}{step.label}</div>
              <div className="mt-0.5 leading-4 opacity-80">{step.body}</div>
            </div>
          );
        })}
      </div>
      {detail && <p className="mt-2 text-[11px] font-medium leading-5 text-[#667085]">{detail}</p>}
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
    <div className="flex flex-col items-center justify-center min-h-[40vh]">
      <motion.div
        initial={{ opacity: 0, y: 12 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.4, ease: "easeOut" }}
        className="text-center"
      >
        <div className="flex items-center justify-center mb-6">
          <div className="w-12 h-12 rounded-2xl bg-[#eef1f3] border border-[#526d82]/10 flex items-center justify-center shadow-2xl shadow-sm">
            <OrcaLogo className="w-6 h-6 text-[#172033]" />
          </div>
        </div>
        <h1 className="text-2xl font-bold text-[#172033] mb-3 tracking-tight">
          {t("tutor_welcome_title")}
        </h1>
        <p className="text-[13px] text-[#344054] mb-6 max-w-[480px] mx-auto leading-relaxed">
          {t("tutor_welcome_body")}
          <br/><br/>
          Istersen <strong>Plan Modu</strong> ile mufredat ac, <strong>Korteks</strong> ile kaynakli arastirma yap veya IDE sonucunu Tutor'a gonder.
        </p>

        <div className="mx-auto mb-6 grid max-w-[560px] grid-cols-1 gap-2 sm:grid-cols-2">
          {starterPrompts.map((item) => (
            <button
              key={item.label}
              onClick={() => onPromptClick(item.prompt)}
              className="rounded-2xl border border-[#526d82]/12 bg-white/62 px-4 py-3 text-left text-xs font-bold text-[#344054] shadow-sm transition hover:bg-[#f7f4ec] hover:text-[#172033] focus:outline-none focus:ring-2 focus:ring-[#9ec7d9]"
            >
              {item.label}
              <span className="mt-1 block text-[10px] font-medium leading-5 text-[#667085]">
                {item.hint}
              </span>
            </button>
          ))}
        </div>

        <div className="flex items-center justify-center gap-2 mt-4 py-2 px-4 rounded-full border border-[#526d82]/10/50 bg-[#f7f9fa]/30 w-fit mx-auto">
          <Bell className="w-3 h-3 text-[#344054]" />
          <span className="text-[10px] font-medium text-[#667085] tracking-wide uppercase">
            {t("small_step_first")}
          </span>
        </div>
      </motion.div>
    </div>
  );
}
