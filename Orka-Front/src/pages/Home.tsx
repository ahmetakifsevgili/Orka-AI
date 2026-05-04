// ── Home — uygulamanın ana kabuğu. ───────────────────────────────────────────
// - Topics yüklenince varsa ilk topic otomatik seçilir → chat direkt açılır.
// - Topic olmasa bile ChatPanel açık kalır (null-topic modunda backend yeni topic oluşturur).
// - onTopicAutoCreated: mesaj sonrası backend yeni topic oluşturduysa sidebar güncellenir.

import { useState, useEffect, useCallback, useRef, useMemo } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { BookOpen, Code2, LayoutDashboard, MessageSquare, Settings, Sparkles, Bookmark } from "lucide-react";
import toast from "react-hot-toast";
import type { ActiveLearningContext, ChatMessage, ApiTopic, ApiSession, ContextRailTab, RightRailState } from "@/lib/types";
import { LearningAPI, TopicsAPI } from "@/services/api";
import { useQuizHistory } from "@/contexts/QuizHistoryContext";
import LeftSidebar from "@/components/LeftSidebar";
import ChatPanel from "@/components/ChatPanel";
import WikiMainPanel from "@/components/WikiMainPanel";
import SettingsPanel from "@/components/SettingsPanel";
import BookmarksPanel from "@/components/BookmarksPanel";
import DashboardPanel from "@/components/DashboardPanel";
import InteractiveIDE from "@/components/InteractiveIDE";
import SplitPane from "@/components/SplitPane";
import LearningWorkspace from "@/components/LearningWorkspace";
import ContextRightRail from "@/components/ContextRightRail";
import LearningContextBar from "@/components/LearningContextBar";
import { usePremiumOnboarding } from "@/components/PremiumOnboardingTour";
import OnboardingWelcomePanel from "@/components/OnboardingWelcomePanel";

// ── F5 Sonrası Context Kalıcılığı (localStorage keys) ───────────────────────
// Kullanıcı sayfayı yenilediğinde son aktif topic / view / wiki ekranı
// otomatik geri yüklenir. AUTH.clear() ile logout'ta temizlenir.
const LS_ACTIVE_TOPIC_ID = "orka_active_topic_id";
const LS_ACTIVE_VIEW = "orka_active_view";
const LS_WIKI_TOPIC_ID = "orka_wiki_topic_id";
const LS_RIGHT_RAIL = "orka_context_right_rail";

const VALID_VIEWS = new Set(["chat", "dashboard", "settings", "wiki", "ide", "bookmarks"]);

const VIEW_META = {
  chat: {
    title: "Öğrenme Sohbeti",
    description: "Tutor, plan, quiz ve kaynak hafızası aynı bağlamda çalışır.",
    icon: MessageSquare,
  },
  dashboard: {
    title: "Bugünkü Öğrenme Kokpiti",
    description: "Sinyaller, zayıf beceriler ve sıradaki en iyi adım.",
    icon: LayoutDashboard,
  },
  wiki: {
    title: "Wiki ve Kaynak Hafızası",
    description: "NotebookLM kaynakları, notlar ve kişisel pekiştirme alanı.",
    icon: BookOpen,
  },
  ide: {
    title: "Kod Çalışma Alanı",
    description: "Kodunu çalıştır, çıktıyı hocaya gönder ve öğrenme sinyali üret.",
    icon: Code2,
  },
  settings: {
    title: "Ayarlar",
    description: "Hesap, tercih ve çalışma ortamı ayarları.",
    icon: Settings,
  },
  bookmarks: {
    title: "Kayıtlı Mesajlarım",
    description: "Sohbet sırasında işaretlediğin yanıtlar burada toplanır.",
    icon: Bookmark,
  },
} as const;

function mapRole(r: string): "user" | "ai" {
  return r.toLowerCase() === "user" ? "user" : "ai";
}

function mapApiMessages(session: ApiSession | null): ChatMessage[] {
  if (!session || !session.messages) return [];
  return session.messages.map((m) => ({
    id: m.id,
    role: mapRole(m.role || "ai"),
    type: (m.messageType as ChatMessage["type"]) ?? "text",
    content: m.content || "",
    timestamp: m.createdAt ? new Date(m.createdAt) : new Date(),
  }));
}

export default function Home() {
  const { shouldShowWelcome, startTour, dismissOnboarding } = usePremiumOnboarding();
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
    const saved = localStorage.getItem(LS_ACTIVE_VIEW);
    return saved && VALID_VIEWS.has(saved) ? saved : "chat";
  });
  const [wikiTopicId, setWikiTopicId] = useState<string | null>(
    () => localStorage.getItem(LS_WIKI_TOPIC_ID)
  );
  const [activeLearningContext, setActiveLearningContext] = useState<ActiveLearningContext | null>(null);
  const [rightRail, setRightRail] = useState<RightRailState>(() => {
    try {
      const saved = localStorage.getItem(LS_RIGHT_RAIL);
      if (!saved) return { isOpen: false, tab: "wiki" };
      const parsed = JSON.parse(saved) as RightRailState;
      return {
        isOpen: Boolean(parsed.isOpen),
        tab: parsed.tab ?? "wiki",
        topicId: parsed.topicId,
        title: parsed.title,
        sourceRef: parsed.sourceRef,
      };
    } catch {
      return { isOpen: false, tab: "wiki" };
    }
  });
  const [defaultChatMode, setDefaultChatMode] = useState<"plan" | "chat">("chat");
  const [pendingIDEMessage, setPendingIDEMessage] = useState<string | null>(null);
  const [activeQuizQuestion, setActiveQuizQuestion] = useState<string | null>(null);
  const [isMobileSidebarOpen, setIsMobileSidebarOpen] = useState(false);

  // ── Load topics on mount; localStorage'daki son aktif topic'i tercih et ───
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

        // Fallback: ilk topic
        if (!activeTopic) setActiveTopic(loaded[0]);
      })
      .catch((err) => {
        console.error("Topics load failed:", err);
        toast.error("Konular yüklenemedi.");
      })
      .finally(() => setTopicsLoading(false));
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ── activeTopic / activeView / wikiTopicId ── localStorage sync ────────────
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
    if (wikiTopicId) {
      localStorage.setItem(LS_WIKI_TOPIC_ID, wikiTopicId);
    } else {
      localStorage.removeItem(LS_WIKI_TOPIC_ID);
    }
  }, [wikiTopicId]);

  useEffect(() => {
    localStorage.setItem(LS_RIGHT_RAIL, JSON.stringify(rightRail));
  }, [rightRail]);

  const [creating, setCreating] = useState(false);
  const ignoreTopicChangeRef = useRef(false);

  const recordContextSignal = useCallback((signalType: string, context: ActiveLearningContext, payload?: Record<string, unknown>) => {
    LearningAPI.recordSignal({
      topicId: context.focusTopicId ?? context.topicId,
      sessionId: sessionId ?? undefined,
      signalType,
      topicPath: context.focusPath,
      payloadJson: JSON.stringify({
        focusTopicId: context.focusTopicId,
        focusTitle: context.focusTitle,
        sourceRef: context.focusSourceRef,
        intent: context.intent,
        ...payload,
      }),
    }).catch((err: unknown) => {
      console.warn("[Home] Learning context signal could not be recorded:", err);
    });
  }, [sessionId]);

  const buildLearningContext = useCallback((topic: ApiTopic, intent: ActiveLearningContext["intent"] = "lesson"): ActiveLearningContext => {
    const parent = topic.parentTopicId ? topics.find((t) => t.id === topic.parentTopicId) : null;
    const sessionTopic = parent ?? topic;
    const focusPath = parent ? `${parent.title} > ${topic.title}` : topic.title;

    return {
      topicId: sessionTopic.id,
      topicTitle: sessionTopic.title,
      parentTopicId: parent?.id,
      parentTitle: parent?.title,
      focusTopicId: topic.id,
      focusTitle: topic.title,
      focusPath,
      intent,
    };
  }, [topics]);

  const openContextRail = useCallback((context: ActiveLearningContext, tab: ContextRailTab = "wiki") => {
    const railTopicId = context.focusTopicId ?? context.topicId;
    if (!railTopicId) return;

    setWikiTopicId(railTopicId);
    setRightRail({
      isOpen: true,
      tab,
      topicId: railTopicId,
      title: context.focusTitle ?? context.topicTitle,
      sourceRef: context.focusSourceRef,
    });
  }, []);

  const activateLearningContext = useCallback((
    topic: ApiTopic,
    options?: { tab?: ContextRailTab; intent?: ActiveLearningContext["intent"]; keepIde?: boolean }
  ) => {
    const context = buildLearningContext(topic, options?.intent ?? "lesson");
    const parent = context.parentTopicId ? topics.find((t) => t.id === context.parentTopicId) : null;
    const sessionTopic = parent ?? topic;

    setActiveLearningContext(context);
    if (sessionTopic.id !== activeTopic?.id) {
      setActiveTopic(sessionTopic);
    }
    if (!options?.keepIde && activeView !== "ide") {
      setActiveView("chat");
    }
    openContextRail(context, options?.tab ?? "wiki");
    recordContextSignal("LessonFocused", context, { railTab: options?.tab ?? "wiki" });
  }, [activeTopic?.id, activeView, buildLearningContext, openContextRail, recordContextSignal, topics]);

  // ── Load session when active topic changes ────────────────────────────────
  useEffect(() => {
    if (!activeTopic) return;
    
    if (ignoreTopicChangeRef.current) {
        ignoreTopicChangeRef.current = false;
        return;
    }

    setMessages([]);
    setSessionId(null);
    setSessionLoading(true);

    TopicsAPI.getLatestSession(activeTopic.id)
      .then((r) => {
        const session = r.data as ApiSession;
        setSessionId(session.sessionId);
        setMessages(mapApiMessages(session));
      })
      .catch(() => {
        // 404 = no session yet; ChatPanel will create one on first message
      })
      .finally(() => setSessionLoading(false));

    loadHistoryForTopic(activeTopic.id);
  }, [activeTopic, loadHistoryForTopic]);

  // ── Topic selection from sidebar ──────────────────────────────────────────
  const handleTopicClick = useCallback((topic: ApiTopic | null, defaultMode: "plan" | "chat" = "chat") => {
    if (!topic) {
      setActiveTopic(null);
      setActiveLearningContext(null);
      setSessionId(null);
      setMessages([]);
      setActiveView("chat");
      setWikiTopicId(null);
      setRightRail({ isOpen: false, tab: "wiki" });
      setDefaultChatMode(defaultMode);
      return;
    }

    activateLearningContext(topic, { tab: "wiki", intent: "lesson", keepIde: activeView === "ide" });
  }, [activateLearningContext, activeView]);

  const handleEnterChat = useCallback((topic: ApiTopic) => {
    activateLearningContext(topic, { tab: "wiki", intent: "lesson", keepIde: activeView === "ide" });
  }, [activateLearningContext, activeView]);

  // ── New topic created from sidebar ────────────────────────────────────────
  const handleTopicCreated = useCallback((topic: ApiTopic) => {
    setTopics((prev) => [topic, ...prev]);
    setActiveTopic(topic);
    setActiveLearningContext({
      topicId: topic.id,
      topicTitle: topic.title,
      focusTopicId: topic.id,
      focusTitle: topic.title,
      focusPath: topic.title,
      intent: "lesson",
    });
    setActiveView("chat");
    toast.success(`"${topic.title}" oluşturuldu`);
  }, []);

  // ── Sidebar refresh (Deep Plan → yeni topic geldiğinde) ──────────────────
  const handleTopicsRefresh = useCallback(() => {
    setRefreshTrigger((n) => n + 1);
  }, []);

  // ── Backend yeni topic oluşturduysa (null-topic modu) sidebar'ı kur ──
  const handleTopicAutoCreated = useCallback((newTopicId: string) => {
    ignoreTopicChangeRef.current = true;
    setRefreshTrigger((n) => n + 1);
    TopicsAPI.getAll()
      .then((r) => {
        const loaded: ApiTopic[] = r.data ?? [];
        setTopics(loaded);
        const found = loaded.find((t) => t.id === newTopicId);
        if (found) setActiveTopic(found);
      })
      .catch((err: unknown) => {
        console.error("[Home] TopicsAPI.getAll (after topic change) failed:", err);
        ignoreTopicChangeRef.current = false;
      });
  }, []);

  // ── Wiki panel ────────────────────────────────────────────────────────────
  const handleOpenWiki = useCallback((topicId: string, tab: ContextRailTab = "wiki") => {
    const topic = topics.find((t) => t.id === topicId);
    const context = topic
      ? buildLearningContext(topic, tab === "practice" ? "practice" : "lesson")
      : {
          ...(activeLearningContext ?? {}),
          topicId: activeTopic?.id,
          topicTitle: activeTopic?.title,
          focusTopicId: topicId,
          focusTitle: activeLearningContext?.focusTitle ?? activeTopic?.title ?? "Wiki",
          focusPath: activeLearningContext?.focusPath ?? activeTopic?.title ?? "Wiki",
          intent: "lesson" as const,
        };

    setActiveLearningContext(context);
    openContextRail(context, tab);
    if (activeView !== "ide") setActiveView("chat");
    recordContextSignal("WikiRailOpened", context, { railTab: tab });
  }, [activeLearningContext, activeTopic, activeView, buildLearningContext, openContextRail, recordContextSignal, topics]);

  const handleCloseWiki = useCallback(() => {
    setRightRail((prev) => ({ ...prev, isOpen: false }));
  }, []);

  const handleFullscreenWiki = useCallback((topicId: string) => {
    setWikiTopicId(topicId);
    setActiveView("wiki");
  }, []);

  const handleRailTabChange = useCallback((tab: ContextRailTab) => {
    setRightRail((prev) => ({ ...prev, tab }));
    if (activeLearningContext) {
      recordContextSignal("ContextActionClicked", activeLearningContext, { railTab: tab, action: "right-rail-tab" });
    }
  }, [activeLearningContext, recordContextSignal]);

  const handleSendContextToChat = useCallback((message: string, sourceRef?: string) => {
    const nextContext = activeLearningContext
      ? { ...activeLearningContext, focusSourceRef: sourceRef ?? activeLearningContext.focusSourceRef, intent: "source" as const }
      : null;
    if (nextContext) setActiveLearningContext(nextContext);
    setPendingIDEMessage(message);
    setActiveView("chat");
    if (nextContext) recordContextSignal("ContextActionClicked", nextContext, { action: "wiki-to-chat", sourceRef });
  }, [activeLearningContext, recordContextSignal]);

  const clearLearningContext = useCallback(() => {
    setActiveLearningContext(null);
    setRightRail({ isOpen: false, tab: "wiki" });
    setWikiTopicId(null);
  }, []);

  const handleViewChange = useCallback((view: string) => {
    if (view.startsWith("wiki:")) {
      const subtopicId = view.split(":")[1];
      if (subtopicId) handleOpenWiki(subtopicId, "wiki");
      return;
    }

    if (view === "wiki") {
      const topicId = activeLearningContext?.focusTopicId ?? activeTopic?.id;
      if (topicId) {
        handleOpenWiki(topicId, "wiki");
      } else {
        toast.error("Wiki'yi görüntülemek için önce bir konu seçmelisiniz.");
      }
      return;
    }

    setActiveView(view);
  }, [activeLearningContext?.focusTopicId, activeTopic?.id, handleOpenWiki]);

  const handleOnboardingFirstGoal = useCallback(() => {
    dismissOnboarding();
    handleViewChange("chat");
    window.setTimeout(() => {
      document.getElementById("tour-chat-input")?.focus();
    }, 180);
  }, [dismissOnboarding, handleViewChange]);

  const handleOnboardingStartTour = useCallback(() => {
    handleViewChange("dashboard");
    window.setTimeout(() => {
      startTour();
    }, 220);
  }, [handleViewChange, startTour]);

  // ── Main content renderer ──────────────────────────────────────────────────
  const currentSubtopic = useMemo(() => {
    if (!activeTopic) return null;
    if (activeLearningContext?.focusTopicId && activeLearningContext.focusTopicId !== activeTopic.id) {
      const focused = topics.find((t) => t.id === activeLearningContext.focusTopicId);
      const siblings = focused?.parentTopicId
        ? topics.filter((t) => t.parentTopicId === focused.parentTopicId).sort((a, b) => (a.order || 0) - (b.order || 0))
        : [];
      const index = Math.max(0, siblings.findIndex((t) => t.id === focused?.id));
      return {
        title: activeLearningContext.focusTitle ?? focused?.title ?? "Odak ders",
        index: index >= 0 ? index + 1 : 1,
        total: siblings.length || 1,
        progress: focused?.progressPercentage ?? activeTopic.progressPercentage ?? 0,
      };
    }
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
  }, [activeLearningContext, activeTopic, topics]);

  const activeViewMeta = VIEW_META[(activeView as keyof typeof VIEW_META)] ?? VIEW_META.chat;
  const ActiveViewIcon = activeViewMeta.icon;
  const viewKey = `${activeView}-${wikiTopicId ?? "main"}`;

  // IDE'den gelen kodu TutorAgent'a gönderir: chat view'a geçip mesajı tetikler
  const handleIDESendToChat = useCallback((message: string) => {
    setPendingIDEMessage(message);
    setActiveView("chat");
    if (activeLearningContext) {
      openContextRail(activeLearningContext, rightRail.tab ?? "wiki");
      recordContextSignal("ContextActionClicked", activeLearningContext, { action: "ide-to-chat" });
    }
  }, [activeLearningContext, openContextRail, recordContextSignal, rightRail.tab]);

  const handleIDEClose = useCallback(() => {
    // Quiz bittikten sonra IDE kapanır, chat'e geri döner
    setActiveQuizQuestion(null);
    setActiveView("chat");
  }, []);

  const renderChatPanel = () => (
    <ChatPanel
      activeTopic={activeTopic}
      sessionId={sessionId}
      onSessionStart={setSessionId}
      messages={messages}
      setMessages={setMessages}
      sessionLoading={sessionLoading}
      onOpenWiki={handleOpenWiki}
      onTopicsRefresh={handleTopicsRefresh}
      onTopicAutoCreated={handleTopicAutoCreated}
      currentSubtopic={currentSubtopic}
      defaultMode={defaultChatMode}
      pendingMessage={pendingIDEMessage}
      onPendingMessageConsumed={() => setPendingIDEMessage(null)}
      onOpenIDE={(question) => { setActiveQuizQuestion(question ?? null); setActiveView("ide"); }}
      focusTopicId={activeLearningContext?.focusTopicId}
      focusTitle={activeLearningContext?.focusTitle}
      focusPath={activeLearningContext?.focusPath}
      focusSourceRef={activeLearningContext?.focusSourceRef}
    />
  );

  const renderRightRail = () => (
    <ContextRightRail
      state={rightRail}
      onClose={handleCloseWiki}
      onFullscreenWiki={handleFullscreenWiki}
      onTabChange={handleRailTabChange}
      onSendToChat={handleSendContextToChat}
    />
  );

  const renderMain = () => {
    switch (activeView) {
      case "dashboard":
        return <DashboardPanel topics={topics} onViewChange={handleViewChange} onFocusTopic={activateLearningContext} />;
      case "settings":
        return <SettingsPanel />;
      case "bookmarks":
        return (
          <BookmarksPanel
            topics={topics}
            onFocusTopic={(topic) => activateLearningContext(topic, { tab: "wiki", intent: "lesson" })}
            onViewChange={handleViewChange}
          />
        );
      case "wiki":
        return wikiTopicId ? (
          <WikiMainPanel
            topicId={wikiTopicId}
            onClose={() => handleViewChange("chat")}
            variant="full"
            onSendToChat={(message) => handleSendContextToChat(message)}
          />
        ) : (
          <div className="flex-1 flex items-center justify-center text-zinc-500">
            Bir ders seçilmedi.
          </div>
        );
      case "ide": {
        return (
          <SplitPane
            left={renderChatPanel()}
            right={
              <InteractiveIDE
                topicTitle={activeLearningContext?.focusTitle ?? activeTopic?.title}
                topicId={activeLearningContext?.focusTopicId ?? activeTopic?.id}
                sessionId={sessionId ?? undefined}
                quizQuestion={activeQuizQuestion ?? undefined}
                onSendToChat={handleIDESendToChat}
                onClose={handleIDEClose}
              />
            }
          />
        );
      }
      default:
        return (
          <LearningWorkspace rightRail={renderRightRail()} railOpen={rightRail.isOpen}>
            {renderChatPanel()}
          </LearningWorkspace>
        );
    }
  };

  return (
    <div className="orka-app-shell orka-bg h-screen flex overflow-hidden text-[#172033] relative">
      <div className="pointer-events-none absolute inset-0 mist-grid opacity-35" />
      <div className="pointer-events-none absolute left-[24rem] top-10 h-64 w-64 rounded-full bg-[#dcecf3]/70 blur-3xl" />
      <div className="pointer-events-none absolute bottom-10 right-14 h-72 w-72 rounded-full bg-[#ddebe3]/70 blur-3xl" />
      <LeftSidebar
        topics={topics}
        topicsLoading={topicsLoading}
        activeTopic={activeTopic}
        activeFocusTopicId={activeLearningContext?.focusTopicId}
        onTopicClick={(t, m) => { handleTopicClick(t, m); setIsMobileSidebarOpen(false); }}
        onEnterChat={(t) => { handleEnterChat(t); setIsMobileSidebarOpen(false); }}
        onTopicCreated={handleTopicCreated}
        activeView={activeView}
        onViewChange={(v) => { handleViewChange(v); setIsMobileSidebarOpen(false); }}
        refreshTrigger={refreshTrigger}
        isOpen={isMobileSidebarOpen}
        onClose={() => setIsMobileSidebarOpen(false)}
      />

      <div className="relative z-10 flex flex-1 overflow-hidden p-0 md:p-3 md:pl-0">
        <div className="flex-1 overflow-hidden md:rounded-[2rem] bg-[#f5f7f9]/90 shadow-sm border-t md:border border-white/40 backdrop-blur-2xl ring-1 ring-[#526d82]/5 flex flex-col relative">
          <div className="flex-shrink-0 border-b border-[#526d82]/10 bg-[#f4f7f7]/76 px-4 py-3 backdrop-blur-xl sm:px-6">
            <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
              <div className="flex min-w-0 items-center gap-3">
                <button
                  onClick={() => setIsMobileSidebarOpen(true)}
                  className="md:hidden p-2 -ml-2 rounded-lg text-[#667085] hover:bg-[#eef1f3]"
                >
                  <MessageSquare className="w-5 h-5" />
                </button>
                <div className="grid h-10 w-10 place-items-center rounded-2xl bg-[#172033] text-white shadow-sm shadow-slate-900/10">
                  <ActiveViewIcon className="h-4 w-4" />
                </div>
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <h1 className="truncate text-sm font-black tracking-tight text-[#172033]">
                      {activeViewMeta.title}
                    </h1>
                    {activeTopic?.title && (
                      <span className="rounded-full bg-[#dcecf3]/72 px-2.5 py-1 text-[10px] font-bold text-[#2d5870]">
                        {activeTopic.title}
                      </span>
                    )}
                  </div>
                  <p className="mt-1 truncate text-xs font-semibold text-[#667085]">
                    {currentSubtopic
                      ? `${currentSubtopic.index}/${currentSubtopic.total} · ${currentSubtopic.title}`
                      : activeViewMeta.description}
                  </p>
                  <div className="mt-2">
                    <LearningContextBar
                      context={activeLearningContext}
                      railOpen={rightRail.isOpen}
                      onOpenRail={(tab = "wiki") => {
                        if (activeLearningContext) openContextRail(activeLearningContext, tab);
                      }}
                      onClear={clearLearningContext}
                    />
                  </div>
                </div>
              </div>

              <div className="flex flex-wrap items-center gap-2">
                {[
                  { id: "chat", label: "Sohbet", icon: MessageSquare },
                  { id: "dashboard", label: "Kokpit", icon: LayoutDashboard },
                  { id: "ide", label: "IDE", icon: Code2 },
                ].map((item) => {
                  const isActive = activeView === item.id;
                  const ItemIcon = item.icon;
                  return (
                    <button
                      key={item.id}
                      onClick={() => handleViewChange(item.id)}
                      className={`inline-flex items-center gap-1.5 rounded-xl border px-3 py-2 text-[11px] font-extrabold transition ${
                        isActive
                          ? "border-[#172033]/10 bg-[#172033] text-white shadow-sm"
                          : "border-[#526d82]/12 bg-[#eef1f3]/72 text-[#667085] hover:-translate-y-0.5 hover:bg-[#f7f4ec] hover:text-[#172033]"
                      }`}
                    >
                      <ItemIcon className="h-3.5 w-3.5" />
                      {item.label}
                    </button>
                  );
                })}
              </div>
            </div>
          </div>

          <div className="min-h-0 flex-1 overflow-hidden">
            <AnimatePresence mode="wait" initial={false}>
              <motion.div
                key={viewKey}
                initial={{ opacity: 0, filter: "blur(4px)" }}
                animate={{ opacity: 1, filter: "blur(0px)" }}
                exit={{ opacity: 0, filter: "blur(3px)" }}
                transition={{ duration: 0.15, ease: "easeOut" }}
                className="h-full min-h-0"
              >
                {renderMain()}
              </motion.div>
            </AnimatePresence>
          </div>
        </div>
      </div>

      <OnboardingWelcomePanel
        open={shouldShowWelcome}
        onFirstGoal={handleOnboardingFirstGoal}
        onStartTour={handleOnboardingStartTour}
        onSkip={dismissOnboarding}
      />
    </div>
  );
}
