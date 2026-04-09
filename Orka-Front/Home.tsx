/*
 * Design: "Sessiz Lüks" — h-screen flex overflow-hidden.
 * Multi-view layout: LeftSidebar (ChatGPT-style) | Main Content | WikiDrawer (conditional).
 * Views: chat, dashboard, courses, wiki, settings.
 * No top bar. No Community.
 */

import { useState, useCallback, useRef } from "react";
import { AnimatePresence } from "framer-motion";
import { toast } from "sonner";
import type { ChatMessage, Topic, WikiContent, Conversation } from "@/lib/types";
import { initialTopics, wikiContents } from "@/lib/mockData";
import LeftSidebar from "@/components/LeftSidebar";
import ChatPanel from "@/components/ChatPanel";
import WikiDrawer from "@/components/WikiDrawer";
import SettingsPanel from "@/components/SettingsPanel";
import QuizHistoryPanel from "@/components/QuizHistoryPanel";
import DashboardPanel from "@/components/DashboardPanel";

export default function Home() {
  const [topics, setTopics] = useState<Topic[]>(initialTopics);
  const [activeWiki, setActiveWiki] = useState<WikiContent | null>(null);
  const [activeSubLessonId, setActiveSubLessonId] = useState<string | null>(null);
  const [activeView, setActiveView] = useState<string>("chat");

  // Conversation management (ChatGPT-style)
  const [conversations, setConversations] = useState<Conversation[]>([]);
  const [activeConversationId, setActiveConversationId] = useState<string | null>(null);

  // Use ref to track the latest active conversation id for async operations
  const activeConvIdRef = useRef<string | null>(null);
  activeConvIdRef.current = activeConversationId;

  const activeConversation = conversations.find((c) => c.id === activeConversationId);
  const activeMessages = activeConversation?.messages || [];

  const setActiveMessages = useCallback(
    (updater: React.SetStateAction<ChatMessage[]>) => {
      const currentId = activeConvIdRef.current;
      if (!currentId) return;
      setConversations((prev) =>
        prev.map((conv) => {
          if (conv.id !== currentId) return conv;
          const newMessages =
            typeof updater === "function" ? updater(conv.messages) : updater;
          const lastMsg = newMessages[newMessages.length - 1];
          return {
            ...conv,
            messages: newMessages,
            lastMessage: lastMsg?.content?.slice(0, 60) || "",
            timestamp: lastMsg?.timestamp || conv.timestamp,
            title:
              conv.title === "Yeni Sohbet" && newMessages.length > 0
                ? newMessages[0].content.slice(0, 40) + (newMessages[0].content.length > 40 ? "..." : "")
                : conv.title,
          };
        })
      );
    },
    []
  );

  const handleNewConversation = useCallback(() => {
    const newConv: Conversation = {
      id: `conv-${Date.now()}`,
      title: "Yeni Sohbet",
      lastMessage: "",
      timestamp: new Date(),
      messages: [],
    };
    setConversations((prev) => [newConv, ...prev]);
    setActiveConversationId(newConv.id);
    activeConvIdRef.current = newConv.id;
    setActiveView("chat");
  }, []);

  const handleConversationClick = useCallback(
    (id: string) => {
      setActiveConversationId(id);
      activeConvIdRef.current = id;
      setActiveView("chat");
    },
    []
  );

  // Auto-create conversation if none exists when sending first message
  const ensureConversation = useCallback(() => {
    const currentId = activeConvIdRef.current;
    if (!currentId) {
      const newConv: Conversation = {
        id: `conv-${Date.now()}`,
        title: "Yeni Sohbet",
        lastMessage: "",
        timestamp: new Date(),
        messages: [],
      };
      setConversations((prev) => [newConv, ...prev]);
      setActiveConversationId(newConv.id);
      activeConvIdRef.current = newConv.id;
      return newConv.id;
    }
    return currentId;
  }, []);

  const handleSubLessonClick = (_topicId: string, subLessonId: string) => {
    const wiki = wikiContents[subLessonId];
    if (wiki) {
      setActiveWiki(wiki);
      setActiveSubLessonId(subLessonId);
      if (activeView !== "chat") setActiveView("chat");
    } else {
      toast("Wiki içeriği oluşturuluyor...", {
        description: "Detaylı notlar için kısa süre sonra tekrar bakın.",
      });
    }
  };

  const handleCloseWiki = () => {
    setActiveWiki(null);
    setActiveSubLessonId(null);
  };

  const handleNewTopic = () => {
    toast("/plan komutuyla yeni müfredat oluşturun", {
      description: 'Örnek: "Docker öğrenmek istiyorum /plan"',
    });
  };

  const handleAddTopic = (topic: Topic) => {
    setTopics((prev) => [...prev, topic]);
    toast(`"${topic.title}" müfredatınıza eklendi`, {
      description: `${topic.subLessons.length} alt ders oluşturuldu.`,
    });
  };

  const handleViewChange = (view: string) => {
    setActiveView(view);
    if (view !== "chat") {
      setActiveWiki(null);
      setActiveSubLessonId(null);
    }
  };

  const renderMainContent = () => {
    switch (activeView) {
      case "dashboard":
        return <DashboardPanel topics={topics} onViewChange={handleViewChange} />;
      case "history":
        return <QuizHistoryPanel />;
      case "settings":
        return <SettingsPanel />;
      case "courses":
        return (
          <PlaceholderView
            title="Kurslar"
            description="Kayıtlı kurslarınız ve öğrenme yollarınız burada görünecek."
            icon="📚"
          />
        );
      case "wiki":
        return (
          <PlaceholderView
            title="Wiki"
            description="Bilgi wikinizi keşfedin. Sidebar'daki müfredatlardan konulara tıklayarak wiki içeriğini görüntüleyin."
            icon="📖"
          />
        );
      case "chat":
      default:
        return (
          <ChatPanel
            messages={activeMessages}
            setMessages={setActiveMessages}
            topics={topics}
            onNewTopic={handleAddTopic}
            ensureConversation={ensureConversation}
          />
        );
    }
  };

  return (
    <div className="h-screen flex overflow-hidden bg-zinc-950">
      <LeftSidebar
        topics={topics}
        activeSubLessonId={activeSubLessonId}
        onSubLessonClick={handleSubLessonClick}
        onNewTopic={handleNewTopic}
        activeView={activeView}
        onViewChange={handleViewChange}
        conversations={conversations}
        activeConversationId={activeConversationId}
        onConversationClick={handleConversationClick}
        onNewConversation={handleNewConversation}
      />

      {renderMainContent()}

      <AnimatePresence>
        {activeWiki && activeView === "chat" && (
          <WikiDrawer wiki={activeWiki} onClose={handleCloseWiki} />
        )}
      </AnimatePresence>
    </div>
  );
}

/* Placeholder for views not yet fully implemented */
function PlaceholderView({
  title,
  description,
  icon,
}: {
  title: string;
  description: string;
  icon: string;
}) {
  return (
    <div className="flex-1 flex flex-col bg-zinc-900 h-full overflow-hidden">
      <div className="flex-1 flex items-center justify-center">
        <div className="text-center max-w-sm">
          <div className="text-4xl mb-4">{icon}</div>
          <h2 className="text-lg font-semibold text-zinc-200 mb-2">{title}</h2>
          <p className="text-sm text-zinc-500 leading-relaxed">{description}</p>
          <p className="text-xs text-zinc-600 mt-4">Yakında geliyor</p>
        </div>
      </div>
    </div>
  );
}
