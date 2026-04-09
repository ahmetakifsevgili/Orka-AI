/*
 * Design: ChatGPT-style sidebar.
 * Top: Logo + New Chat button
 * Middle: Conversation list (recent chats) + Müfredatlar (collapsible topics)
 * Bottom: Settings
 * Navigation: Anasayfa, Kurslar, Wiki (3 items only)
 * No Community. No OK button. No Share button.
 */

import { useState } from "react";
import { useLocation } from "wouter";
import {
  ChevronRight,
  ChevronDown,
  Plus,
  Check,
  BookOpen,
  Home,
  GraduationCap,
  BookMarked,
  Settings,
  MessageSquare,
  PanelLeftClose,
  PanelLeft,
} from "lucide-react";
import { motion, AnimatePresence } from "framer-motion";
import OrcaLogo from "./OrcaLogo";
import type { Topic, Conversation } from "@/lib/types";

interface LeftSidebarProps {
  topics: Topic[];
  activeSubLessonId: string | null;
  onSubLessonClick: (topicId: string, subLessonId: string) => void;
  onNewTopic: () => void;
  activeView: string;
  onViewChange: (view: string) => void;
  conversations: Conversation[];
  activeConversationId: string | null;
  onConversationClick: (id: string) => void;
  onNewConversation: () => void;
}

export default function LeftSidebar({
  topics,
  activeSubLessonId,
  onSubLessonClick,
  onNewTopic,
  activeView,
  onViewChange,
  conversations,
  activeConversationId,
  onConversationClick,
  onNewConversation,
}: LeftSidebarProps) {
  const [, navigate] = useLocation();
  const [expandedTopics, setExpandedTopics] = useState<Set<string>>(
    new Set(topics.map((t) => t.id))
  );
  const [collapsed, setCollapsed] = useState(false);
  const [mufredatOpen, setMufredatOpen] = useState(true);

  const toggleTopic = (topicId: string) => {
    setExpandedTopics((prev) => {
      const next = new Set(prev);
      if (next.has(topicId)) next.delete(topicId);
      else next.add(topicId);
      return next;
    });
  };

  if (collapsed) {
    return (
      <div className="w-12 bg-zinc-950 border-r border-zinc-800/50 flex flex-col items-center py-4 flex-shrink-0">
        <button
          onClick={() => setCollapsed(false)}
          className="w-8 h-8 rounded-lg flex items-center justify-center text-zinc-500 hover:text-zinc-300 hover:bg-zinc-900 transition-colors duration-150 mb-4"
          title="Expand sidebar"
        >
          <PanelLeft className="w-4 h-4" />
        </button>
        <div className="w-8 h-8 rounded-lg flex items-center justify-center">
          <OrcaLogo className="w-5 h-5 text-zinc-400" />
        </div>
      </div>
    );
  }

  return (
    <motion.div
      initial={{ width: 260, opacity: 1 }}
      animate={{ width: 260, opacity: 1 }}
      className="w-[260px] bg-zinc-950 border-r border-zinc-800/50 flex flex-col h-full flex-shrink-0"
    >
      {/* Header: Logo + Collapse + New Chat */}
      <div className="px-3 py-3 flex items-center justify-between flex-shrink-0">
        <div className="flex items-center gap-2.5">
          <OrcaLogo className="w-5 h-5 text-zinc-100" />
          <span className="text-sm font-semibold text-zinc-100 tracking-tight">
            Orka AI
          </span>
        </div>
        <div className="flex items-center gap-1">
          <button
            onClick={onNewConversation}
            className="w-7 h-7 rounded-md flex items-center justify-center text-zinc-500 hover:text-zinc-300 hover:bg-zinc-800 transition-colors duration-150"
            title="Yeni sohbet"
          >
            <Plus className="w-4 h-4" />
          </button>
          <button
            onClick={() => setCollapsed(true)}
            className="w-7 h-7 rounded-md flex items-center justify-center text-zinc-500 hover:text-zinc-300 hover:bg-zinc-800 transition-colors duration-150"
            title="Daralt"
          >
            <PanelLeftClose className="w-4 h-4" />
          </button>
        </div>
      </div>

      {/* Navigation: Anasayfa, Kurslar, Wiki */}
      <div className="px-2 pb-2 flex-shrink-0">
        {[
          { id: "chat", icon: Home, label: "Anasayfa", route: null },
          { id: "courses", icon: GraduationCap, label: "Kurslar", route: "/courses" },
          { id: "wiki", icon: BookMarked, label: "Wiki", route: null },
        ].map((item) => {
          const isActive = activeView === item.id;
          return (
            <button
              key={item.id}
              onClick={() => {
                if (item.route) {
                  navigate(item.route);
                } else {
                  onViewChange(item.id);
                }
              }}
              className={`flex items-center gap-2.5 w-full px-3 py-2 rounded-lg text-[13px] transition-colors duration-150 mb-0.5 ${
                isActive
                  ? "bg-zinc-800/80 text-zinc-100 font-medium"
                  : "text-zinc-400 hover:text-zinc-200 hover:bg-zinc-900/50"
              }`}
            >
              <item.icon className="w-4 h-4 flex-shrink-0" />
              <span className="truncate">{item.label}</span>
            </button>
          );
        })}
      </div>

      {/* Divider */}
      <div className="mx-3 border-t border-zinc-800/50" />

      {/* Conversations List (ChatGPT style) */}
      <div className="flex-1 overflow-y-auto px-2 py-2">
        {/* Recent Conversations */}
        {conversations.length > 0 && (
          <div className="mb-3">
            <span className="px-2 text-[10px] font-medium text-zinc-600 uppercase tracking-wider">
              Son Sohbetler
            </span>
            <div className="mt-1.5 space-y-0.5">
              {conversations.map((conv) => (
                <button
                  key={conv.id}
                  onClick={() => onConversationClick(conv.id)}
                  className={`flex items-center gap-2.5 w-full px-2.5 py-2 rounded-lg text-left transition-colors duration-150 group ${
                    activeConversationId === conv.id && activeView === "chat"
                      ? "bg-zinc-800/70 text-zinc-100"
                      : "text-zinc-400 hover:text-zinc-200 hover:bg-zinc-900/50"
                  }`}
                >
                  <MessageSquare className="w-3.5 h-3.5 flex-shrink-0 text-zinc-600" />
                  <span className="text-[13px] truncate">{conv.title}</span>
                </button>
              ))}
            </div>
          </div>
        )}

        {/* Müfredatlar Section */}
        <div>
          <button
            onClick={() => setMufredatOpen(!mufredatOpen)}
            className="flex items-center justify-between w-full px-2 py-1.5"
          >
            <span className="text-[10px] font-medium text-zinc-600 uppercase tracking-wider">
              Müfredatlar
            </span>
            <div className="flex items-center gap-1">
              <button
                onClick={(e) => {
                  e.stopPropagation();
                  onNewTopic();
                }}
                className="text-zinc-600 hover:text-zinc-300 transition-colors duration-150"
                title="Yeni müfredat"
              >
                <Plus className="w-3 h-3" />
              </button>
              {mufredatOpen ? (
                <ChevronDown className="w-3 h-3 text-zinc-600" />
              ) : (
                <ChevronRight className="w-3 h-3 text-zinc-600" />
              )}
            </div>
          </button>

          <AnimatePresence initial={false}>
            {mufredatOpen && (
              <motion.div
                initial={{ height: 0, opacity: 0 }}
                animate={{ height: "auto", opacity: 1 }}
                exit={{ height: 0, opacity: 0 }}
                transition={{ duration: 0.15, ease: "easeOut" }}
                className="overflow-hidden"
              >
                <div className="mt-1 space-y-0.5">
                  {topics.map((topic) => (
                    <div key={topic.id}>
                      <button
                        onClick={() => toggleTopic(topic.id)}
                        className="flex items-center gap-2 w-full px-2 py-1.5 rounded-md text-[13px] text-zinc-300 hover:bg-zinc-900/50 transition-colors duration-150"
                      >
                        <ChevronRight
                          className={`w-3 h-3 text-zinc-600 transition-transform duration-150 flex-shrink-0 ${
                            expandedTopics.has(topic.id) ? "rotate-90" : ""
                          }`}
                        />
                        <span className="mr-1 text-sm">{topic.icon}</span>
                        <span className="truncate text-left">
                          {topic.title}
                        </span>
                      </button>

                      <AnimatePresence initial={false}>
                        {expandedTopics.has(topic.id) && (
                          <motion.div
                            initial={{ height: 0, opacity: 0 }}
                            animate={{ height: "auto", opacity: 1 }}
                            exit={{ height: 0, opacity: 0 }}
                            transition={{ duration: 0.15, ease: "easeOut" }}
                            className="overflow-hidden"
                          >
                            <div className="pl-5 py-0.5">
                              {topic.subLessons.map((sub) => (
                                <button
                                  key={sub.id}
                                  onClick={() =>
                                    onSubLessonClick(topic.id, sub.id)
                                  }
                                  className={`flex items-center gap-2 w-full text-left px-2 py-1.5 rounded text-xs transition-colors duration-150 ${
                                    activeSubLessonId === sub.id
                                      ? "text-zinc-100 bg-zinc-800/50"
                                      : "text-zinc-500 hover:text-zinc-300 hover:bg-zinc-900/50"
                                  }`}
                                >
                                  {sub.completed ? (
                                    <Check className="w-3 h-3 text-zinc-500 flex-shrink-0" />
                                  ) : (
                                    <BookOpen className="w-3 h-3 text-zinc-600 flex-shrink-0" />
                                  )}
                                  <span className="truncate">{sub.title}</span>
                                </button>
                              ))}
                            </div>
                          </motion.div>
                        )}
                      </AnimatePresence>
                    </div>
                  ))}
                </div>
              </motion.div>
            )}
          </AnimatePresence>
        </div>
      </div>

      {/* Footer: Settings only */}
      <div className="border-t border-zinc-800/50 px-3 py-3 flex-shrink-0">
        <button
          onClick={() => onViewChange("settings")}
          className={`flex items-center gap-2.5 w-full px-2 py-2 rounded-lg text-[13px] transition-colors duration-150 ${
            activeView === "settings"
              ? "bg-zinc-800/80 text-zinc-100 font-medium"
              : "text-zinc-500 hover:text-zinc-300 hover:bg-zinc-900/50"
          }`}
        >
          <Settings className="w-4 h-4 flex-shrink-0" />
          <span>Ayarlar</span>
        </button>
      </div>
    </motion.div>
  );
}
