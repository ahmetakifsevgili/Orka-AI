/*
 * LeftSidebar — ChatGPT tarzı, topic-centric.
 * Her topic = bir müfredat oturumu. Seçince ChatPanel o topic'i yükler.
 *
 * Düzeltmeler:
 *  - topicsLoading artık initialLoading değişince sync ediliyor (sonsuz spinner bug'ı giderildi)
 *  - "Yeni Konu" (+) butonu + inline form eklendi
 *  - onTopicCreated prop'u destructuring'e eklendi
 */

import { useState, useEffect } from "react";
import { useLocation } from "wouter";
import {
  Plus,
  Home,
  GraduationCap,
  BookMarked,
  Settings,
  PanelLeftClose,
  PanelLeft,
  MessageSquare,
  Loader2,
  X,
  Check,
  ChevronRight,
  ChevronDown,
  FlaskConical,
  Code2,
} from "lucide-react";
import { motion, AnimatePresence } from "framer-motion";
import toast from "react-hot-toast";
import OrcaLogo from "./OrcaLogo";
import type { ApiTopic } from "@/lib/types";
import { TopicsAPI } from "@/services/api";
import { useLanguage } from "@/contexts/LanguageContext";

interface LeftSidebarProps {
  topics: ApiTopic[];
  topicsLoading: boolean;
  activeTopic: ApiTopic | null;
  onTopicClick: (topic: ApiTopic | null, defaultMode?: "plan" | "chat") => void;
  onEnterChat: (topic: ApiTopic) => void;
  onTopicCreated: (topic: ApiTopic) => void;
  activeView: string;
  onViewChange: (view: string) => void;
  /** ChatPanel'dan AI yanıtı geldiğinde artar; topic listesini yeniden çeker. */
  refreshTrigger: number;
}

// NAV_ITEMS: label artık t() ile çevriliyor, statik label kaldırıldı
const NAV_ITEMS = [
  { id: "chat",    icon: Home,          labelKey: "home",    route: null },
  { id: "courses", icon: GraduationCap, labelKey: "courses", route: "/courses" },
  { id: "wiki",    icon: BookMarked,    labelKey: "wiki",    route: null },
  { id: "ide",     icon: Code2,         labelKey: "ide",     route: null },
];

const EMOJI_SUGGESTIONS = ["📚", "🧠", "💻", "🔬", "🎨", "🗣️", "🏛️", "⚡", "🌍", "🎯"];

export default function LeftSidebar({
  topics: initialTopics,
  topicsLoading: initialLoading,
  activeTopic,
  onTopicClick,
  onEnterChat,
  onTopicCreated,
  activeView,
  onViewChange,
  refreshTrigger,
}: LeftSidebarProps) {
  const [, navigate] = useLocation();
  const { t } = useLanguage();
  const [collapsed, setCollapsed] = useState(false);
  const [topics, setTopics] = useState<ApiTopic[]>(initialTopics);
  const [topicsLoading, setTopicsLoading] = useState(initialLoading);

  // Yeni Konu formu state'leri
  const [showNewTopicForm, setShowNewTopicForm] = useState(false);
  const [newTopicTitle, setNewTopicTitle] = useState("");
  const [newTopicEmoji, setNewTopicEmoji] = useState("📚");
  const [creating, setCreating] = useState(false);
  const [expandedPlans, setExpandedPlans] = useState<Record<string, boolean>>({});

  const togglePlanExpansion = (planId: string) => {
      setExpandedPlans(prev => ({ ...prev, [planId]: prev[planId] === undefined ? false : !prev[planId] }));
  };

  // initialTopics VE initialLoading değişince local state'i sync et
  useEffect(() => {
    setTopics(initialTopics);
  }, [initialTopics]);

  useEffect(() => {
    setTopicsLoading(initialLoading);
  }, [initialLoading]);

  // refreshTrigger her değiştiğinde topic listesini yenile
  useEffect(() => {
    if (refreshTrigger === 0) return;
    setTopicsLoading(true);
    TopicsAPI.getAll()
      .then((r) => {
        const loaded = r.data as ApiTopic[];
        setTopics(loaded);
        // Yeni plan topic'leri otomatik aç
        const planParents = loaded.filter(
          (t: ApiTopic) => !t.parentTopicId && (t.category || '').toLowerCase() === 'plan'
        );
        const hasChildren = (id: string) => loaded.some((t: ApiTopic) => t.parentTopicId === id);
        setExpandedPlans(prev => {
          const next = { ...prev };
          planParents.forEach((p: ApiTopic) => {
            if (hasChildren(p.id) && next[p.id] === undefined) {
              next[p.id] = true;
            }
          });
          return next;
        });
      })
      .finally(() => setTopicsLoading(false));
  }, [refreshTrigger]);

  // Yeni konu oluştur (Optimistic UI)
  const handleCreateTopic = async () => {
    const title = newTopicTitle.trim();
    if (!title) return;

    const tempId = `temp-${Date.now()}`;
    const newTopic: ApiTopic = {
      id: tempId,
      title,
      emoji: newTopicEmoji,
      category: "Genel",
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      userId: "", // Placeholder
    };

    // Optimistic update
    setTopics(prev => [newTopic, ...prev]);
    setShowNewTopicForm(false);
    setNewTopicTitle("");
    setCreating(true);

    try {
      const { data } = await TopicsAPI.create({
        title,
        emoji: newTopicEmoji,
        category: "Genel",
      });

      // Update topics list with real data from server
      setTopics(prev => prev.map(t => t.id === data.id ? { ...t, title: data.title, emoji: data.emoji } : t));
      onTopicCreated(data as ApiTopic);
    } catch {
      // Rollback on fail
      setTopics(prev => prev.filter(t => t.id !== tempId));
      toast.error("Konu oluşturulamadı. Lütfen tekrar deneyin.");
      setShowNewTopicForm(true); // Re-open form
      setNewTopicTitle(title);
    } finally {
      setCreating(false);
    }
  };

  const handleNewTopicKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleCreateTopic();
    }
    if (e.key === "Escape") {
      setShowNewTopicForm(false);
      setNewTopicTitle("");
    }
  };

  if (collapsed) {
    return (
      <div className="w-12 bg-zinc-950 border-r border-zinc-800/50 flex flex-col items-center py-4 flex-shrink-0">
        <button
          onClick={() => setCollapsed(false)}
          className="w-8 h-8 rounded-lg flex items-center justify-center text-zinc-500 hover:text-zinc-300 hover:bg-zinc-900 transition-colors duration-150 mb-4"
          title="Genişlet"
        >
          <PanelLeft className="w-4 h-4" />
        </button>
        <OrcaLogo className="w-5 h-5 text-zinc-600" />
      </div>
    );
  }

  return (
    <motion.div
      initial={{ width: 260 }}
      animate={{ width: 260 }}
      className="w-[260px] bg-zinc-950 border-r border-zinc-800/50 flex flex-col h-full flex-shrink-0"
    >
      {/* Header */}
      <div className="px-3 py-3 flex items-center justify-between flex-shrink-0">
        <div className="flex items-center gap-2.5">
          <OrcaLogo className="w-5 h-5 text-zinc-100" />
          <span className="text-sm font-semibold text-zinc-100 tracking-tight">
            Orka AI
          </span>
        </div>
        <button
          onClick={() => setCollapsed(true)}
          className="w-7 h-7 rounded-md flex items-center justify-center text-zinc-500 hover:text-zinc-300 hover:bg-zinc-800 transition-colors duration-150"
          title="Daralt"
        >
          <PanelLeftClose className="w-4 h-4" />
        </button>
      </div>

      {/* Nav */}
      <div className="px-2 pb-2 flex-shrink-0">
        {NAV_ITEMS.map((item) => {
          const isActive = activeView === item.id;
          const label = (() => {
            if (item.labelKey === "home") return t("home_nav") || "Anasayfa";
            if (item.labelKey === "courses") return t("courses") || "Kurslar";
            if (item.labelKey === "wiki") return t("wiki") || "Wiki";
            if (item.labelKey === "ide") return "Kod Editörü";
            return item.labelKey;
          })();
          return (
            <button
              key={item.id}
              onClick={() =>
                item.route ? navigate(item.route) : onViewChange(item.id)
              }
              className={`flex items-center gap-2.5 w-full px-3 py-2 rounded-lg text-[13px] transition-colors duration-150 mb-0.5 ${
                isActive
                  ? "bg-zinc-800/80 text-zinc-100 font-medium"
                  : "text-zinc-400 hover:text-zinc-200 hover:bg-zinc-900/50"
              }`}
            >
              <item.icon className="w-4 h-4 flex-shrink-0" />
              <span className="truncate">{label}</span>
            </button>
          );
        })}
      </div>

      <div className="mx-3 border-t border-zinc-800/50" />

      {/* Ana Scroll View */}
      <div className="flex-1 flex flex-col overflow-y-auto overflow-x-hidden min-h-0 custom-scrollbar-hide">
        
        {/* 1. ÖĞRENME MÜFREDATLARI (PLANLAR) */}
        <div className="px-3 pt-3 pb-2 flex-shrink-0">
          <div className="flex items-center justify-between pl-2 mb-3">
            <span className="text-[10px] font-bold text-zinc-500 uppercase tracking-widest block">
              📚 Öğrenme Müfredatları
            </span>
            <button
                onClick={() => onTopicClick(null, "plan")}
                title="Yeni Müfredat Oluştur"
                className="w-6 h-6 flex items-center justify-center rounded-md text-zinc-600 hover:text-zinc-200 hover:bg-zinc-800 transition-all duration-200"
              >
                <Plus className="w-3.5 h-3.5" />
            </button>
          </div>

          <div className="space-y-1">
            {topics.filter(t => 
              t.parentTopicId === null && 
              (t.category || '').toLowerCase() === 'plan'
            ).map((parentChat) => {
               const children = topics.filter(t => t.parentTopicId === parentChat.id).sort((a,b) => ((a as any).order || 0) - ((b as any).order || 0));
             const isExpanded = expandedPlans[parentChat.id] !== false; // default expanded
             const hasChildren = children.length > 0;

             return (
              <div key={parentChat.id} className="mb-1">
                  <div 
                    className={`flex items-center justify-between px-2 py-1.5 group/cat cursor-pointer rounded-lg transition-all duration-200 ${
                      activeTopic?.id === parentChat.id ? "bg-zinc-800/80 text-zinc-100 shadow-sm" : "hover:bg-zinc-900/60 text-zinc-400 hover:text-zinc-200"
                    }`}
                  >
                     <div 
                        className="flex items-center gap-2 overflow-hidden flex-1"
                        onClick={(e) => {
                             if (hasChildren) {
                                 e.stopPropagation();
                                 togglePlanExpansion(parentChat.id);
                             } else {
                               onTopicClick(parentChat);
                             }
                        }}
                     >
                         <div 
                           className="w-4 h-4 flex items-center justify-center cursor-pointer text-zinc-500 hover:text-zinc-300"
                           onClick={(e) => {
                              if (hasChildren) {
                                  e.stopPropagation();
                                  togglePlanExpansion(parentChat.id);
                              }
                           }}
                         >
                           {hasChildren ? (isExpanded ? <ChevronDown className="w-3.5 h-3.5" /> : <ChevronRight className="w-3.5 h-3.5" />) : <MessageSquare className={`w-3 h-3 flex-shrink-0 ${activeTopic?.id === parentChat.id ? "text-zinc-300" : ""}`} />}
                         </div>
                         <span className="text-[12px] truncate">{parentChat.title}</span>
                     </div>
                     
                     {hasChildren && (
                      <div className="flex items-center gap-1.5 opacity-0 group-hover/cat:opacity-100 transition-opacity">
                        <div className="flex flex-col items-end gap-1">
                          <span className="text-[9px] text-zinc-500 bg-zinc-800/50 px-1.5 py-0.5 rounded-full border border-zinc-700/30">
                            {Math.round(parentChat.progressPercentage || 0)}%
                          </span>
                          <div className="w-12 h-1 bg-zinc-800 rounded-full overflow-hidden">
                             <div 
                               className="h-full bg-emerald-500/50 transition-all duration-500" 
                               style={{ width: `${parentChat.progressPercentage || 0}%` }}
                             />
                          </div>
                        </div>
                      </div>
                     )}
                  </div>
                  
                  <AnimatePresence>
                  {hasChildren && isExpanded && (
                     <motion.div 
                        initial={{ height: 0, opacity: 0 }} 
                        animate={{ height: "auto", opacity: 1 }} 
                        exit={{ height: 0, opacity: 0 }} 
                        className="pl-[19px] mt-1 space-y-0.5 border-l border-zinc-800/50 ml-4 relative"
                     >
                       {children.map((topic) => {
                          const isActive = activeTopic?.id === topic.id;
                          const isCompleted = topic.isMastered || topic.progressPercentage === 100;
                          return (
                            <button
                               key={topic.id}
                               onClick={() => onTopicClick(topic)}
                             className={`flex flex-col items-stretch w-full px-3 py-1.5 rounded-md text-left transition-all duration-150 relative group ${
                                 isActive
                                   ? "text-zinc-100 font-medium bg-white/5 border border-white/10 shadow-sm"
                                   : "text-zinc-500 hover:text-zinc-300 hover:bg-zinc-900/30"
                               } ${isCompleted ? 'border-l-2 border-zinc-600/40 bg-zinc-800/30' : ''}`}
                             >
                               {/* Tree connector lines */}
                               <div className="absolute left-[-11px] top-0 bottom-1/2 w-px bg-zinc-800/60"></div>
                               <div className="absolute left-[-11px] top-1/2 w-4 h-px bg-zinc-800/60"></div>
                               
                               <div className="flex items-center gap-2.5 w-full relative">
                                 {isCompleted ? (
                                    <div className="w-4 h-4 rounded-full bg-emerald-500/20 border border-emerald-500/40 flex items-center justify-center">
                                      <Check className="w-2.5 h-2.5 text-emerald-400 stroke-[2.5]" />
                                    </div>
                                 ) : isActive ? (
                                    <div className="w-4 h-4 rounded-full bg-white/10 border border-white/20 flex items-center justify-center shadow-sm shadow-white/10 flex-shrink-0">
                                      <ChevronRight className="w-2.5 h-2.5 text-white" />
                                    </div>
                                 ) : (
                                    <div className="w-1.5 h-1.5 rounded-full bg-zinc-700 flex-shrink-0 ml-1"></div>
                                 )}
                                 <span className={`text-[12px] truncate flex-1 ${isCompleted ? 'text-zinc-400 font-medium' : isActive ? 'text-white font-medium' : 'text-zinc-500'}`}>
                                   {topic.title}
                                 </span>

                                 {/* Mastery Score Badge */}
                                 {topic.successScore && topic.successScore > 0 && (
                                   <span className="text-[8px] text-emerald-400 font-bold opacity-0 group-hover:opacity-100 transition-opacity">
                                      {topic.successScore}%
                                   </span>
                                 )}

                                 <button
                                   onClick={(e) => {
                                     e.stopPropagation();
                                     onEnterChat(topic);
                                   }}
                                   title="Derse Başla"
                                   className={`opacity-0 group-hover:opacity-100 px-2 py-0.5 rounded text-[9px] font-medium transition-all duration-200 ${
                                     isActive ? "bg-white text-zinc-900" : "bg-emerald-500/10 text-emerald-400 hover:bg-emerald-500/20 border border-emerald-500/20"
                                   }`}
                                 >
                                   {isActive ? "Derse Git" : "Devam Et"}
                                 </button>
                               </div>

                               {/* Progress line for ongoing topics */}
                               {!isCompleted && (topic.progressPercentage ?? 0) > 0 && (
                                 <div className="mt-1 ml-4 w-full h-0.5 bg-zinc-800 rounded-full overflow-hidden">
                                   <div 
                                     className="h-full bg-zinc-400 transition-all duration-500" 
                                     style={{ width: `${topic.progressPercentage}%` }}
                                   />
                                 </div>
                               )}
                             </button>
                          );
                       })}
                     </motion.div>
                  )}
                  </AnimatePresence>
              </div>
            );
          })}
        </div>
      </div>

        </div>

        {/* 2. DİĞER SOHBETLER */}
        <div className="mx-3 mt-4 mb-2 border-t border-zinc-800/50 opacity-50" />
        <div className="px-3 flex-shrink-0 mb-4">
          <div className="flex items-center justify-between pl-2 mb-2">
            <span className="text-[10px] font-bold text-zinc-500 uppercase tracking-widest block">
              💬 Sohbet Geçmişi
            </span>
            <button
                onClick={() => onTopicClick(null, "chat")}
                title="Yeni Sohbet Başlat"
                className="w-6 h-6 flex items-center justify-center rounded-md text-zinc-600 hover:text-zinc-200 hover:bg-zinc-800 transition-all duration-200"
              >
                <Plus className="w-3.5 h-3.5" />
            </button>
          </div>
          <div className="space-y-0.5">
            {topics.filter(t => t.parentTopicId === null && (t.category || '').toLowerCase() !== 'plan').map((chatTopic) => (
              <button
                key={chatTopic.id}
                onClick={() => onTopicClick(chatTopic, "chat")}
                className={`flex items-center justify-between w-full px-2 py-1.5 rounded-lg text-left transition-colors duration-150 group ${
                  activeTopic?.id === chatTopic.id
                    ? "bg-zinc-800/80 text-zinc-100 font-medium"
                    : "text-zinc-500 hover:text-zinc-300 hover:bg-zinc-900/50"
                }`}
              >
                <div className="flex items-center gap-2 overflow-hidden">
                  <MessageSquare className="w-3 h-3 flex-shrink-0" />
                  <span className="text-xs truncate">{chatTopic.title}</span>
                </div>
              </button>
            ))}
          </div>
      </div>

      {/* Footer */}
      <div className="mt-auto border-t border-zinc-800/50 px-3 py-3 flex-shrink-0 bg-zinc-950">
        <button
          onClick={() => onViewChange("settings")}
          className={`flex items-center gap-2.5 w-full px-2 py-2 rounded-lg text-[13px] transition-colors duration-150 ${
            activeView === "settings"
              ? "bg-zinc-800/80 text-zinc-100 font-medium"
              : "text-zinc-500 hover:text-zinc-300 hover:bg-zinc-900/50"
          }`}
        >
          <Settings className="w-4 h-4 flex-shrink-0" />
          <span>{t("settings") || "Ayarlar"}</span>
        </button>
      </div>
    </motion.div>
  );
}
