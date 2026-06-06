/*
 * Home — protected Learning OS shell.
 * - Default screen is Mission Control.
 * - Tutor stays usable without a selected topic; the backend may create the topic after the first message.
 * - Legacy view ids are accepted as compatibility aliases but the sidebar exposes only canonical modes.
 */

import { useState, useEffect, useCallback, useRef, useMemo, lazy, Suspense } from "react";
import { useLocation } from "wouter";
import { AnimatePresence } from "framer-motion";
import toast from "react-hot-toast";
import type { ChatMessage, ApiTopic, ApiSession } from "@/lib/types";
import { AuthAPI, TopicsAPI, storage } from "@/services/api";
import { appViewPath, isKnownAppView, normalizeAppView } from "@/lib/appNavigation";
import { useQuizHistory } from "@/contexts/QuizHistoryContext";
import LeftSidebar from "@/components/LeftSidebar";
import { usePremiumOnboarding } from "@/components/PremiumOnboardingTour";
import {
  MissionControlHome,
  StudyRoomPanel,
  ExamWarRoomPanel,
  SourceWikiProPanel,
  NotebookStudioProPanel,
  CodeLearningIdePanel
} from "@/components/ProductCoherencePanels";

const ChatPanel = lazy(() => import("@/components/ChatPanel"));
const WikiMainPanel = lazy(() => import("@/components/WikiMainPanel"));
const SettingsPanel = lazy(() => import("@/components/SettingsPanel"));
const DashboardPanel = lazy(() => import("@/components/DashboardPanel"));
const CentralExamsPanel = lazy(() => import("@/components/CentralExamsPanel"));
const InteractiveIDE = lazy(() => import("@/components/InteractiveIDE"));
const LearningPanel = lazy(() => import("@/components/LearningPanel"));
const SplitPane = lazy(() => import("@/components/SplitPane"));
import OrkaLMDashboard from "@/components/OrkaLMDashboard";

const LoadingFallback = () => (
  <div className="flex-1 flex items-center justify-center h-full">
    <svg className="h-8 w-8 animate-spin text-sky-500" viewBox="0 0 24 24" fill="none">
      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
    </svg>
  </div>
);

// ── F5 Sonrası Context Kalıcılığı (localStorage keys) ─────────────────────
// Kullanıcı sayfayı yenilediğinde son aktif topic / view / wiki ekranı
// otomatik geri yüklenir. AUTH.clear() ile logout'ta temizlenir.
const LS_ACTIVE_TOPIC_ID = "orka_active_topic_id";
const LS_ACTIVE_VIEW = "orka_active_view";
const LS_WIKI_TOPIC_ID = "orka_wiki_topic_id";

function mapRole(r: string): "user" | "ai" {
  return r.toLowerCase() === "user" ? "user" : "ai";
}

function mapApiMessages(session: ApiSession | null): ChatMessage[] {
  if (!session || !session.messages) return [];
  return session.messages.map((m) => ({
    id: m.id,
    role: mapRole(m.role || "ai"),
    type: m.messageType === "general" ? "text" : ((m.messageType as ChatMessage["type"]) ?? "text"),
    content: m.content || "",
    metadata: m.metadata ?? null,
    timestamp: m.createdAt ? new Date(m.createdAt) : new Date(),
  }));
}

export default function Home({ initialView }: { initialView?: string }) {
  usePremiumOnboarding();
  const [location, navigate] = useLocation();
  const { loadHistoryForTopic } = useQuizHistory();
  const [topics, setTopics] = useState<ApiTopic[]>([]);
  const [topicsLoading, setTopicsLoading] = useState(true);
  /** ChatPanel'den gelen tetikleyici; her artışta LeftSidebar topic listesini yeniler. */
  const [refreshTrigger, setRefreshTrigger] = useState(0);

  const [activeTopic, setActiveTopic] = useState<ApiTopic | null>(null);
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [sessionLoading, setSessionLoading] = useState(false);

  // Initial view — localStorage'da geçerli bir kayıt varsa onu yükle
  const [activeView, setActiveView] = useState<string>(() => {
    if (isKnownAppView(initialView)) return normalizeAppView(initialView);
    const saved = localStorage.getItem(LS_ACTIVE_VIEW);
    return isKnownAppView(saved) ? normalizeAppView(saved) : "home";
  });
  const [wikiTopicId, setWikiTopicId] = useState<string | null>(
    () => localStorage.getItem(LS_WIKI_TOPIC_ID)
  );
  const [defaultChatMode, setDefaultChatMode] = useState<"plan" | "chat">("chat");
  const [pendingIDEMessage, setPendingIDEMessage] = useState<string | null>(null);
  const [activeQuizQuestion, setActiveQuizQuestion] = useState<string | null>(null);
  const [loggingOut, setLoggingOut] = useState(false);

  // ── Load topics on mount; localStorage'daki son aktif topic'i tercih et ─
  useEffect(() => {
    TopicsAPI.getAll()
      .then((r) => {
        const loaded: ApiTopic[] = (r && r.data) ? r.data : [];
        setTopics(loaded);
        if (loaded.length === 0) return;

        // F5 sonrası restore: önce kaydedilmiş topicId'yi dene
        const savedId = localStorage.getItem(LS_ACTIVE_TOPIC_ID);
        if (savedId) {
          const found = loaded.find((t) => t.id === savedId);
          if (found) {
            setActiveTopic(found);
            return;
          }
          // Kayıtlı topic artık yoksa temizle
          localStorage.removeItem(LS_ACTIVE_TOPIC_ID);
        }

        // Do not auto-select the first topic; late topic loading can erase a null-topic first chat.
      })
      .catch((err) => {
        console.error("Topics load failed:", err);
        toast.error("Konular yüklenemedi.");
      })
      .finally(() => setTopicsLoading(false));
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ── activeTopic / activeView / wikiTopicId → localStorage sync ──────────
  useEffect(() => {
    if (activeTopic?.id) {
      localStorage.setItem(LS_ACTIVE_TOPIC_ID, activeTopic.id);
    } else {
      localStorage.removeItem(LS_ACTIVE_TOPIC_ID);
    }
  }, [activeTopic]);

  useEffect(() => {
    localStorage.setItem(LS_ACTIVE_VIEW, activeView);
  }, [activeView]);

  useEffect(() => {
    if (!initialView) return;
    const normalized = normalizeAppView(initialView);
    setActiveView((current) => (current === normalized ? current : normalized));
  }, [initialView]);

  useEffect(() => {
    const targetPath = appViewPath(activeView);
    if (location !== targetPath) {
      navigate(targetPath, { replace: true });
    }
  }, [activeView, location, navigate]);

  useEffect(() => {
    if (wikiTopicId) {
      localStorage.setItem(LS_WIKI_TOPIC_ID, wikiTopicId);
    } else {
      localStorage.removeItem(LS_WIKI_TOPIC_ID);
    }
  }, [wikiTopicId]);

  const [creating, setCreating] = useState(false);
  const ignoreTopicChangeRef = useRef(false);
  const sessionRequestRef = useRef(0);

  // ── Load session when active topic changes ─────────────────────────────
  useEffect(() => {
    const requestId = ++sessionRequestRef.current;
    if (!activeTopic) {
      setSessionLoading(false);
      return;
    }
    
    if (ignoreTopicChangeRef.current) {
        ignoreTopicChangeRef.current = false;
        return;
    }

    setMessages([]);
    setSessionId(null);
    setSessionLoading(true);

    TopicsAPI.getLatestSession(activeTopic.id)
      .then((r) => {
        if (requestId !== sessionRequestRef.current) return;
        const session = r.data as ApiSession;
        setSessionId(session.sessionId);
        setMessages(mapApiMessages(session));
      })
      .catch(() => {
        // 404 = no session yet; ChatPanel will create one on first message
      })
      .finally(() => {
        if (requestId === sessionRequestRef.current) setSessionLoading(false);
      });

    loadHistoryForTopic(activeTopic.id);
  }, [activeTopic, loadHistoryForTopic]);

  // ── Topic selection from sidebar ──────────────────────────────────────
  const handleTopicClick = useCallback((topic: ApiTopic | null, defaultMode: "plan" | "chat" = "chat") => {
    // null = New Tutor Thread (+ button)
    if (!topic) {
      setActiveTopic(null);
      setSessionId(null);
      setMessages([]);
      setActiveView("tutor");
      setWikiTopicId(null);
      setDefaultChatMode(defaultMode);
      return;
    }

    // Child subtopic (parentTopicId var) → parent'ı active topic yap + chat'e yönlendir
    // NOT: ignoreTopicChangeRef sayesinde chat sıfırlanmaz (session geçmişi korunur)
    if (topic.parentTopicId) {
      const parent = topics.find(t => t.id === topic.parentTopicId);
      if (parent && parent.id !== activeTopic?.id) {
        ignoreTopicChangeRef.current = true; // chat mesajlarını sıfırlama
        setActiveTopic(parent);
      }
      // Code IDE açıksa açık kalsın, değilse Tutor'a geç
      if (activeView === "wiki" || activeView === "orkalm") {
        setWikiTopicId(topic.id);
        return;
      }
      if (normalizeAppView(activeView) !== "code") setActiveView("tutor");
      setWikiTopicId(null);
      return;
    }

    // Parent plan topic (children var) → chat session'a yönlendir
    setActiveTopic(topic);
    if (activeView === "wiki" || activeView === "orkalm") {
      setWikiTopicId(topic.id);
    } else {
      if (normalizeAppView(activeView) !== "code") setActiveView("tutor");
      setWikiTopicId(null);
    }
  }, [topics, activeTopic]);

  // ── Explicitly enter a chat session (Lesson Focus) ───────────────────
  const handleEnterChat = useCallback((topic: ApiTopic) => {
    // Subtopic → parent'ın global session'ına yönlendir
    if (topic.parentTopicId) {
      const parent = topics.find(t => t.id === topic.parentTopicId);
      if (parent) {
        setActiveTopic(parent);
      } else {
        setActiveTopic(topic);
      }
    } else {
      setActiveTopic(topic);
    }
    setWikiTopicId(null);
  }, [topics, activeView]);

  // ── New topic created from sidebar ─────────────────────────────────────
  const handleTopicCreated = useCallback((topic: ApiTopic) => {
    setTopics((prev) => [topic, ...prev]);
    setActiveTopic(topic);
    setSessionId(null);
    setMessages([]);
      setActiveView("tutor");
    toast.success(`"${topic.title}" oluşturuldu`);
  }, []);

  // ── Sidebar refresh (Deep Plan → yeni topic geldiğinde) ───────────────
  const handleTopicsRefresh = useCallback(() => {
    setRefreshTrigger((n) => n + 1);
  }, []);

  // ── Backend yeni topic oluşturduysa (null-topic modu) sidebar'ı kur ───
  const handleTopicAutoCreated = useCallback((newTopicId: string) => {
    setRefreshTrigger((n) => n + 1);
    TopicsAPI.getAll()
      .then((r) => {
        const loaded: ApiTopic[] = r.data ?? [];
        setTopics(loaded);
        const found = loaded.find((t) => t.id === newTopicId);
        if (!found) {
          ignoreTopicChangeRef.current = false;
          return;
        }

        setActiveTopic((current) => {
          if (current?.id === found.id) {
            ignoreTopicChangeRef.current = false;
            return current;
          }

          ignoreTopicChangeRef.current = true;
          return found;
        });
      })
      .catch(() => {
        ignoreTopicChangeRef.current = false;
      });
  }, []);

  // ── Wiki panel ────────────────────────────────────────────────────────
  const handleOpenWiki = useCallback((topicId: string) => {
    setWikiTopicId(topicId);
    setActiveView("sources-wiki");
  }, []);

  const handleCloseWiki = useCallback(() => setWikiTopicId(null), []);

  const handleLogout = useCallback(async () => {
    if (loggingOut) return;
    setLoggingOut(true);
    let revokeFailed = false;

    try {
      await AuthAPI.logout();
    } catch {
      revokeFailed = true;
    } finally {
      storage.clear();
      setTopics([]);
      setActiveTopic(null);
      setSessionId(null);
      setMessages([]);
      setWikiTopicId(null);
      setPendingIDEMessage(null);
      setActiveQuizQuestion(null);
      setActiveView("home");

      if (revokeFailed) {
        toast.error("Çıkış yapılamadı. Lütfen tekrar deneyin.");
      }

      navigate("/login");
      setLoggingOut(false);
    }
  }, [loggingOut, navigate]);

  // ── View switching ─────────────────────────────────────────────────────
  const handleViewChange = useCallback((view: string) => {
    // wiki:subtopicId formatı — flyout panelden belirli bir subtopic wiki'si açılıyor
    if (view.startsWith("wiki:")) {
      const subtopicId = view.split(":")[1];
      if (subtopicId) {
        setWikiTopicId(subtopicId);
        setActiveView("sources-wiki");
        return;
      }
    }

    if (["sources-wiki", "sources", "wiki"].includes(view)) {
      if (activeTopic) {
        setWikiTopicId(activeTopic.id);
        setActiveView("sources-wiki");
      } else {
        setWikiTopicId(null);
        setActiveView("sources-wiki");
        toast.error("Sources / Wiki için önce bir konu seçmelisin.");
      }
      return;
    }

    if (["notebook", "orkalm"].includes(view)) {
      if (activeTopic) {
        setWikiTopicId(activeTopic.id);
      } else {
        setWikiTopicId(null);
          // toast.error removed because OrkaLM is now standalone
      }
      setActiveView("notebook");
      return;
    }

    if (["practice", "learning"].includes(view)) {
      setActiveView("review");
      return;
    }
    if (view === "central-exams") {
      setActiveView("exams");
      return;
    }
    if (view === "ide") {
      setActiveView("code");
      return;
    }
    setActiveView(normalizeAppView(view));
  }, [activeTopic]);

  // ── Main content renderer ──────────────────────────────────────────────
  const currentSubtopic = useMemo(() => {
    if (!activeTopic) return null;
    const children = topics.filter(t => t.parentTopicId === activeTopic.id).sort((a, b) => (a.order || 0) - (b.order || 0));
    if (children.length === 0) return null;
    const completedCount = activeTopic.completedSections || 0;
    const activeChild = children[completedCount] || children[children.length - 1];
    return {
      title: activeChild.title,
      index: completedCount + 1,
      total: children.length,
      progress: activeTopic.progressPercentage || 0,
    };
  }, [activeTopic, topics]);

  // IDE'den gelen kodu TutorAgent'a gönderir: chat view'a geçip mesajı tetikler
  const handleIDESendToChat = useCallback((message: string) => {
    setPendingIDEMessage(message);
    // Split-pane modundayken ide view'dan çıkmasına gerek yok, chat ve ide yan yana!
  }, []);

  const handleIDEClose = useCallback(() => {
    // Quiz bittikten sonra IDE kapanır, chat'e geri döner
    setActiveQuizQuestion(null);
    setActiveView("tutor");
  }, []);

  const renderMain = () => {
    switch (activeView) {
      case "dashboard":
        return <DashboardPanel topics={topics} onViewChange={handleViewChange} />;
      case "progress":
        return <DashboardPanel topics={topics} onViewChange={handleViewChange} mode="progress" />;
      case "central-exams":
        return <CentralExamsPanel />;
      case "settings":
        return <SettingsPanel />;
      case "learning":
      case "review":
        return <LearningPanel topic={activeTopic} sessionId={sessionId ?? undefined} mode="review" onOpenChat={() => setActiveView("tutor")} onOpenIDE={() => setActiveView("code")} />;
      case "practice":
        return <LearningPanel topic={activeTopic} sessionId={sessionId ?? undefined} mode="practice" onOpenChat={() => setActiveView("tutor")} onOpenIDE={() => setActiveView("code")} />;
      case "home":
        return <MissionControlHome activeTopic={activeTopic} sessionId={sessionId} topics={topics} onViewChange={handleViewChange} />;
      case "study-room":
        return <StudyRoomPanel activeTopic={activeTopic} sessionId={sessionId} onViewChange={handleViewChange} />;
      case "exams":
        return <ExamWarRoomPanel activeTopic={activeTopic} sessionId={sessionId} onViewChange={handleViewChange} />;
      case "sources-wiki":
      case "wiki":
        return (
          <SplitPane
            left={
              <ChatPanel activeTopic={activeTopic} sessionId={sessionId} onSessionStart={setSessionId} messages={messages} setMessages={setMessages} sessionLoading={sessionLoading} onOpenWiki={handleOpenWiki} onTopicsRefresh={handleTopicsRefresh} onTopicAutoCreated={handleTopicAutoCreated} currentSubtopic={currentSubtopic} defaultMode={defaultChatMode} pendingMessage={pendingIDEMessage} onPendingMessageConsumed={() => setPendingIDEMessage(null)} onOpenIDE={(question) => { setActiveQuizQuestion(question ?? null); setActiveView("code"); }} />
            }
            right={
              (wikiTopicId ?? activeTopic?.id) ? (
                <WikiMainPanel topicId={(wikiTopicId ?? activeTopic?.id) as string} onClose={() => handleViewChange("tutor")} />
              ) : (
                <div className="flex-1 flex flex-col items-center justify-center text-[#5a6360]">
                  <p className="text-sm font-medium">Wiki i�in �nce bir sohbet veya konu se�.</p>
                </div>
              )
            }
          />
        );
      case "notebook":
        case "orkalm":
        case "sources":
          return (wikiTopicId ?? activeTopic?.id) ? (
            <WikiMainPanel 
              topicId={(wikiTopicId ?? activeTopic?.id) as string} 
              mode="orkalm" 
              onClose={() => { setActiveTopic(null); setWikiTopicId(null); handleViewChange("orkalm"); }} 
            />
          ) : (
            <OrkaLMDashboard 
              topics={topics} 
              onSelectTopic={(topic) => { setActiveTopic(topic); handleViewChange("orkalm"); }} 
              onTopicCreated={(topic) => { setTopics((prev) => [topic, ...prev]); setActiveTopic(topic); handleViewChange("orkalm"); }} 
            />
          );
      case "code":
        return (
          <CodeLearningIdePanel activeTopic={activeTopic} sessionId={sessionId} onViewChange={handleViewChange} onSendToTutor={handleIDESendToChat} onCloseQuiz={handleIDEClose} />
        );
      case "ide":
        return (
          <SplitPane
            left={
              <ChatPanel activeTopic={activeTopic} sessionId={sessionId} onSessionStart={setSessionId} messages={messages} setMessages={setMessages} sessionLoading={sessionLoading} onOpenWiki={handleOpenWiki} onTopicsRefresh={handleTopicsRefresh} onTopicAutoCreated={handleTopicAutoCreated} currentSubtopic={currentSubtopic} defaultMode={defaultChatMode} pendingMessage={pendingIDEMessage} onPendingMessageConsumed={() => setPendingIDEMessage(null)} onOpenIDE={(question) => { setActiveQuizQuestion(question ?? null); setActiveView("code"); }} />
            }
            right={
              <InteractiveIDE topicTitle={activeTopic?.title} topicId={activeTopic?.id} sessionId={sessionId ?? undefined} quizQuestion={activeQuizQuestion ?? undefined} onSendToChat={handleIDESendToChat} onClose={handleIDEClose} />
            }
          />
        );
      case "tutor":
      case "chat":
      default:
        return (
          <ChatPanel activeTopic={activeTopic} sessionId={sessionId} onSessionStart={setSessionId} messages={messages} setMessages={setMessages} sessionLoading={sessionLoading} onOpenWiki={handleOpenWiki} onTopicsRefresh={handleTopicsRefresh} onTopicAutoCreated={handleTopicAutoCreated} currentSubtopic={currentSubtopic} defaultMode={defaultChatMode} pendingMessage={pendingIDEMessage} onPendingMessageConsumed={() => setPendingIDEMessage(null)} onOpenIDE={(question) => { setActiveQuizQuestion(question ?? null); setActiveView("code"); }} />
        );
    }
  };

  return (
    <div className="orka-app-shell orka-bg h-screen flex overflow-hidden text-[#f4f6f3] relative">
      <div className="pointer-events-none absolute inset-0 mist-grid opacity-40" />
      <LeftSidebar
        topics={topics}
        topicsLoading={topicsLoading}
        activeTopic={activeTopic}
        onTopicClick={handleTopicClick}
        onEnterChat={handleEnterChat}
        onTopicCreated={handleTopicCreated}
        activeView={activeView}
        onViewChange={handleViewChange}
        refreshTrigger={refreshTrigger}
        onLogout={handleLogout}
        logoutLoading={loggingOut}
      />

      <div className="relative z-10 flex flex-1 overflow-hidden p-3 pl-0">
        <div className="flex-1 overflow-hidden rounded-[1.35rem] bg-[#0a0c0e]/92 shadow-[0_28px_90px_rgba(0,0,0,0.34)] border border-white/[0.08] backdrop-blur-2xl ring-1 ring-white/[0.03] flex flex-col relative">
          <Suspense fallback={<LoadingFallback />}>
            {renderMain()}
          </Suspense>
        </div>
      </div>
    </div>
  );
}

