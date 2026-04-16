/*
 * Home — uygulamanın ana kabuğu.
 * - Topics yüklenince varsa ilk topic otomatik seçilir → chat direkt açılır.
 * - Topic olmasa bile ChatPanel açık kalır (null-topic modunda backend yeni topic oluşturur).
 * - onTopicAutoCreated: mesaj sonrası backend yeni topic oluşturduysa sidebar güncellenir.
 */

import { useState, useEffect, useCallback, useRef, useMemo } from "react";
import { AnimatePresence } from "framer-motion";
import toast from "react-hot-toast";
import type { ChatMessage, ApiTopic, ApiSession } from "@/lib/types";
import { TopicsAPI } from "@/services/api";
import { useQuizHistory } from "@/contexts/QuizHistoryContext";
import LeftSidebar from "@/components/LeftSidebar";
import ChatPanel from "@/components/ChatPanel";
import WikiMainPanel from "@/components/WikiMainPanel";
import SettingsPanel from "@/components/SettingsPanel";
import DashboardPanel from "@/components/DashboardPanel";
import InteractiveIDE from "@/components/InteractiveIDE";
import SplitPane from "@/components/SplitPane";

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
  const { loadHistoryForTopic } = useQuizHistory();
  const [topics, setTopics] = useState<ApiTopic[]>([]);
  const [topicsLoading, setTopicsLoading] = useState(true);
  /** ChatPanel'den gelen tetikleyici; her artışta LeftSidebar topic listesini yeniler. */
  const [refreshTrigger, setRefreshTrigger] = useState(0);

  const [activeTopic, setActiveTopic] = useState<ApiTopic | null>(null);
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [sessionLoading, setSessionLoading] = useState(false);

  const [activeView, setActiveView] = useState("chat");
  const [wikiTopicId, setWikiTopicId] = useState<string | null>(null);
  const [defaultChatMode, setDefaultChatMode] = useState<"plan" | "chat">("chat");
  const [pendingIDEMessage, setPendingIDEMessage] = useState<string | null>(null);

  // ── Load topics on mount; otomatik ilk seçimi yap ────────────────────
  useEffect(() => {
    TopicsAPI.getAll()
      .then((r) => {
        const loaded: ApiTopic[] = (r && r.data) ? r.data : [];
        setTopics(loaded);
        if (loaded.length > 0 && !activeTopic) {
          setActiveTopic(loaded[0]);
        }
      })
      .catch((err) => {
        console.error("Topics load failed:", err);
        toast.error("Konular yüklenemedi.");
      })
      .finally(() => setTopicsLoading(false));
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const [creating, setCreating] = useState(false);
  const ignoreTopicChangeRef = useRef(false);

  // ── Load session when active topic changes ─────────────────────────────
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

  // ── Topic selection from sidebar ──────────────────────────────────────
  const handleTopicClick = useCallback((topic: ApiTopic | null, defaultMode: "plan" | "chat" = "chat") => {
    // null = Yeni Sohbet Başlat (+ butonundan)
    if (!topic) {
      setActiveTopic(null);
      setSessionId(null);
      setMessages([]);
      setActiveView("chat");
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
      setActiveView("chat");
      setWikiTopicId(null);
      return;
    }

    // Parent plan topic (children var) → chat session'a yönlendir
    setActiveTopic(topic);
    setActiveView("chat");
    setWikiTopicId(null);
  }, [topics, activeTopic]);

  // ── Explicitly enter a chat session (Lesson Focus) ───────────────────
  const handleEnterChat = useCallback((topic: ApiTopic) => {
    setActiveTopic(topic);
    setActiveView("chat");
    setWikiTopicId(null);
  }, []);

  // ── New topic created from sidebar ─────────────────────────────────────
  const handleTopicCreated = useCallback((topic: ApiTopic) => {
    setTopics((prev) => [topic, ...prev]);
    setActiveTopic(topic);
    setActiveView("chat");
    toast.success(`"${topic.title}" oluşturuldu`);
  }, []);

  // ── Sidebar refresh (Deep Plan → yeni topic geldiğinde) ───────────────
  const handleTopicsRefresh = useCallback(() => {
    setRefreshTrigger((n) => n + 1);
  }, []);

  // ── Backend yeni topic oluşturduysa (null-topic modu) sidebar'ı kur ───
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
      .catch(() => {
        ignoreTopicChangeRef.current = false;
      });
  }, []);

  // ── Wiki panel ────────────────────────────────────────────────────────
  const handleOpenWiki = useCallback((topicId: string) => {
    setWikiTopicId(topicId);
    setActiveView("wiki");
  }, []);

  const handleCloseWiki = useCallback(() => setWikiTopicId(null), []);

  // ── View switching ─────────────────────────────────────────────────────
  const handleViewChange = useCallback((view: string) => {
    if (view === "wiki") {
      if (activeTopic) {
        setWikiTopicId(activeTopic.id);
        setActiveView("wiki");
      } else {
        import("react-hot-toast").then((toast) => toast.default.error("Wiki'yi görüntülemek için önce bir konu seçmelisiniz."));
      }
      return;
    }
    setActiveView(view);
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

  const renderMain = () => {
    switch (activeView) {
      case "dashboard":
        return <DashboardPanel topics={topics} onViewChange={handleViewChange} />;
      case "settings":
        return <SettingsPanel />;
      case "wiki":
        return wikiTopicId ? (
          <WikiMainPanel topicId={wikiTopicId} onClose={() => handleViewChange("chat")} />
        ) : (
          <div className="flex-1 flex items-center justify-center text-zinc-500">
            Bir ders seçilmedi.
          </div>
        );
      case "ide": {
        return (
          <SplitPane
            left={
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
                onOpenIDE={() => setActiveView("ide")}
              />
            }
            right={
              <InteractiveIDE
                topicTitle={activeTopic?.title}
                onSendToChat={handleIDESendToChat}
              />
            }
          />
        );
      }
      default:
        return (
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
            onOpenIDE={() => setActiveView("ide")}
          />
        );
    }
  };

  return (
    <div className="h-screen flex overflow-hidden bg-zinc-950">
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
      />

      {renderMain()}
    </div>
  );
}
