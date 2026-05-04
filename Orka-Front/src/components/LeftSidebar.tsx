/*
 * LeftSidebar — ChatGPT tarzı, topic-centric.
 * Her topic = bir müfredat oturumu. Seçince ChatPanel o topic'i yükler.
 *
 * Düzeltmeler:
 *  - topicsLoading artık initialLoading değişince sync ediliyor (sonsuz spinner bug'ı giderildi)
 *  - "Yeni Konu" (+) butonu + inline form eklendi
 *  - onTopicCreated prop'u destructuring'e eklendi
 */

import { useState, useEffect, useMemo, useCallback } from "react";
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
  Trash2,
  LayoutDashboard,
  Bookmark,
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
  activeFocusTopicId?: string;
  onTopicClick: (topic: ApiTopic | null, defaultMode?: "plan" | "chat") => void;
  onEnterChat: (topic: ApiTopic) => void;
  onTopicCreated: (topic: ApiTopic) => void;
  activeView: string;
  onViewChange: (view: string) => void;
  /** ChatPanel'dan AI yanıtı geldiğinde artar; topic listesini yeniden çeker. */
  refreshTrigger: number;
  isOpen?: boolean;
  onClose?: () => void;
}

// NAV_ITEMS: label artık t() ile çevriliyor, statik label kaldırıldı
const NAV_ITEMS = [
  { id: "chat",      icon: Home,            labelKey: "home",        route: null },
  { id: "dashboard", icon: LayoutDashboard, labelKey: "Dashboard",   route: null },
  { id: "wiki",      icon: BookMarked,      labelKey: "wiki",        route: null },
  { id: "ide",       icon: Code2,           labelKey: "ide",         route: null },
  { id: "bookmarks", icon: Bookmark,        labelKey: "Kayıtlarım", route: null },
];

const EMOJI_SUGGESTIONS = ["📚", "🧠", "💻", "🔬", "🎨", "🗣️", "🏛️", "⚡", "🌍", "🎯"];

export default function LeftSidebar({
  topics: initialTopics,
  topicsLoading: initialLoading,
  activeTopic,
  activeFocusTopicId,
  onTopicClick,
  onEnterChat,
  onTopicCreated,
  activeView,
  onViewChange,
  refreshTrigger,
  isOpen,
  onClose,
}: LeftSidebarProps) {
  const [, navigate] = useLocation();
  const { t } = useLanguage();
  const [isPinned, setIsPinned] = useState(true);
  const [isHovered, setIsHovered] = useState(false);
  const isExpanded = isPinned || isHovered;
  const [topics, setTopics] = useState<ApiTopic[]>(initialTopics);
  const [topicsLoading, setTopicsLoading] = useState(initialLoading);

  // Yeni Konu formu state'leri
  const [showNewTopicForm, setShowNewTopicForm] = useState(false);
  const [newTopicTitle, setNewTopicTitle] = useState("");
  const [newTopicEmoji, setNewTopicEmoji] = useState("📚");
  const [creating, setCreating] = useState(false);
  const [expandedPlanId, setExpandedPlanId] = useState<string | null>(null);

  // Accordion state for modules inside a generic plan
  const [expandedModuleIds, setExpandedModuleIds] = useState<Set<string>>(new Set());

  const toggleModule = useCallback((modId: string) => {
    setExpandedModuleIds(prev => {
      const next = new Set(prev);
      if (next.has(modId)) next.delete(modId);
      else next.add(modId);
      return next;
    });
  }, []);

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


  // Flyout panel: hangi müfredat genişletilmiş — topics değişmedikçe cache'le.
  const expandedPlan = useMemo(
    () => topics.find(t => t.id === expandedPlanId),
    [topics, expandedPlanId]
  );
  const expandedModules = useMemo(
    () => expandedPlan
      ? topics.filter(t => t.parentTopicId === expandedPlan.id).sort((a, b) => ((a as any).order || 0) - ((b as any).order || 0))
      : [],
    [topics, expandedPlan]
  );

  const renderLesson = (topicToRender: ApiTopic) => {
    const isActive = activeFocusTopicId === topicToRender.id || activeTopic?.id === topicToRender.id;
    const isCompleted = topicToRender.isMastered || topicToRender.progressPercentage === 100;
    return (
      <div
        key={topicToRender.id}
        onClick={() => onTopicClick(topicToRender)}
        role="button"
        tabIndex={0}
        className={`flex flex-col items-stretch w-full px-3 py-1.5 rounded-md text-left transition-all duration-150 relative group cursor-pointer ${
          isActive
            ? "text-[#172033] font-medium bg-[#f7f9fa] shadow-sm border border-[#526d82]/5 border border-[#526d82]/5 shadow-sm"
            : "text-[#667085] hover:text-[#344054] hover:bg-[#f7f9fa]/30"
        } ${isCompleted ? 'border-l-2 border-[#547c61]/30 bg-[#eef1f3]/50' : ''}`}
      >
        <div className="flex items-center gap-2.5 w-full relative">
          {isCompleted ? (
            <div className="w-4 h-4 rounded-full bg-[#d9e7de] border border-[#547c61]/20 flex items-center justify-center">
              <Check className="w-2.5 h-2.5 text-[#547c61] stroke-[2.5]" />
            </div>
          ) : isActive ? (
            <div className="w-4 h-4 rounded-full bg-[#f7f9fa] shadow-sm border border-[#526d82]/10 flex items-center justify-center shadow-sm shadow-white/10 flex-shrink-0">
              <ChevronRight className="w-2.5 h-2.5 text-[#172033]" />
            </div>
          ) : (
            <div className="w-1.5 h-1.5 rounded-full bg-[#b8d4df] flex-shrink-0 ml-1" />
          )}
          <span className={`text-[12px] truncate flex-1 ${isCompleted ? 'text-[#5f6f7b] font-medium' : isActive ? 'text-[#172033] font-medium' : 'text-[#667085]'}`}>
            {topicToRender.title}
          </span>
          <div className="flex items-center gap-1">
            <button
              onClick={(e) => { e.stopPropagation(); onViewChange(`wiki:${topicToRender.id}`); }}
              title="Ders Wiki'si"
              className="opacity-0 group-hover:opacity-100 p-1 rounded text-[#667085] hover:text-[#906c36] hover:bg-[#d9bd79]/20 transition-all duration-200"
            >
              <BookMarked className="w-3 h-3" />
            </button>
            <button
              onClick={(e) => { e.stopPropagation(); onEnterChat(topicToRender); }}
              title="Derse Başla"
              className={`opacity-0 group-hover:opacity-100 px-2 py-0.5 rounded text-[9px] font-medium transition-all duration-200 ${
                isActive ? "bg-[#f7f9fa] text-zinc-900" : "bg-[#d9e7de]/60 text-[#547c61] hover:bg-[#d9e7de] border border-[#547c61]/10"
              }`}
            >
              {isActive ? "Derse Git" : "Devam Et"}
            </button>
          </div>
        </div>
        {!isCompleted && (topicToRender.progressPercentage ?? 0) > 0 && (
          <div className="mt-1 ml-6 w-full h-0.5 bg-[#eef1f3] rounded-full overflow-hidden">
            <div className="h-full bg-zinc-400 transition-all duration-500" style={{ width: `${topicToRender.progressPercentage}%` }} />
          </div>
        )}
      </div>
    );
  };

  return (
    <>
      {/* Mobile Overlay Backdrop */}
      <AnimatePresence>
        {isOpen && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            onClick={onClose}
            className="fixed inset-0 z-40 bg-black/20 backdrop-blur-sm md:hidden"
          />
        )}
      </AnimatePresence>

      <div
        className={`
          flex flex-row h-full flex-shrink-0 relative z-50
          ${isOpen ? "fixed inset-y-0 left-0" : "hidden md:flex"}
        `}
        onMouseEnter={() => setIsHovered(true)}
        onMouseLeave={() => setIsHovered(false)}
      >
      {/* ══════════ ANA SIDEBAR (Expandable) ══════════ */}
      <motion.div
        initial={{ width: 64 }}
        animate={{ width: (isExpanded || isOpen) ? 260 : 64 }}
        transition={{ duration: 0.2, ease: "easeInOut" }}
        className="bg-[#f7f9fa]/90 backdrop-blur-2xl border-r border-[#526d82]/10 flex flex-col h-full flex-shrink-0 overflow-hidden shadow-sm"
      >
        {/* Header */}
        <div className="px-3 py-3 flex items-center justify-between flex-shrink-0">
          <div className="flex items-center gap-2.5">
            <OrcaLogo className="w-5 h-5 text-[#172033]" />
            {(isExpanded || isOpen) && (
              <motion.span
                initial={{ opacity: 0 }} animate={{ opacity: 1 }}
                className="text-sm font-semibold text-[#172033] tracking-tight whitespace-nowrap"
              >
                Orka AI
              </motion.span>
            )}
          </div>
          <div className="flex items-center gap-1">
            {isOpen && (
              <button
                onClick={onClose}
                className="md:hidden w-7 h-7 rounded-md flex items-center justify-center text-[#667085] hover:bg-[#eef1f3]"
              >
                <X className="w-4 h-4" />
              </button>
            )}
            <button
              onClick={() => setIsPinned(!isPinned)}
              className="hidden md:flex w-7 h-7 rounded-md items-center justify-center text-[#667085] hover:text-[#344054] hover:bg-[#eef1f3] transition-colors duration-150"
              title={isPinned ? "Daralt" : "Sabitle"}
            >
              <PanelLeftClose className={`w-4 h-4 transition-transform duration-300 ${!isPinned ? "rotate-180" : ""}`} />
            </button>
          </div>
        </div>

        {/* Nav */}
        <div className="px-2 pb-2 flex-shrink-0">
          {NAV_ITEMS.map((item) => {
            const isActive = activeView === item.id;
            const label = (() => {
              if (item.labelKey === "home") return t("home_nav") || "Anasayfa";
              if (item.labelKey === "Dashboard") return "Dashboard";
              if (item.labelKey === "courses") return t("courses") || "Kurslar";
              if (item.labelKey === "wiki") return t("wiki") || "Wiki";
              if (item.labelKey === "ide") return "Kod Editörü";
              if (item.labelKey === "Kayıtlarım") return "Kayıtlarım";
              return item.labelKey;
            })();
            return (
              <button
                key={item.id}
                id={`tour-nav-${item.id}`}
                onClick={() => item.route ? navigate(item.route) : onViewChange(item.id)}
                className={`flex items-center gap-2.5 w-full px-3 py-2 rounded-lg text-[13px] transition-colors duration-150 mb-0.5 ${
                  isActive
                    ? "bg-[#f7f9fa] shadow-sm border border-[#526d82]/5 text-[#172033] font-medium"
                    : "text-[#5f6f7b] hover:text-[#172033] hover:bg-[#f7f9fa]/40"
                }`}
              >
                <item.icon className="w-4 h-4 flex-shrink-0" />
                {(isExpanded || isOpen) && <motion.span initial={{ opacity: 0 }} animate={{ opacity: 1 }} className="truncate">{label}</motion.span>}
              </button>
            );
          })}
        </div>

        <div className="mx-3 border-t border-[#526d82]/10" />

        {/* Ana Scroll View */}
        <div className="flex-1 flex flex-col overflow-y-auto overflow-x-hidden min-h-0 custom-scrollbar-hide">

          {/* 💬 SOHBET GEÇMİŞİ */}
          <div className="px-3 pt-3 flex-shrink-0 mb-3">
            <div className="flex items-center justify-between pl-2 mb-2">
              <span className="text-[10px] font-bold text-[#667085] uppercase tracking-widest block">
                💬 Sohbet Geçmişi
              </span>
              <button
                id="tour-new-topic"
                onClick={() => onTopicClick(null, "chat")}
                title="Yeni Sohbet Başlat"
                className="w-6 h-6 flex items-center justify-center rounded-md text-zinc-600 hover:text-[#172033] hover:bg-[#eef1f3] transition-all duration-200"
              >
                <Plus className="w-3.5 h-3.5" />
              </button>
            </div>
            <div className="space-y-0.5">
              {topics.filter(t => t.parentTopicId === null && (t.category || '').toLowerCase() !== 'plan').length === 0 && (
                <p className="text-[11px] text-zinc-600 px-2 py-3 italic">Henüz sohbet yok</p>
              )}
              {topics.filter(t => t.parentTopicId === null && (t.category || '').toLowerCase() !== 'plan').map((chatTopic) => (
                <div
                  key={chatTopic.id}
                  className={`flex items-center justify-between w-full px-2 py-1.5 rounded-lg text-left transition-colors duration-150 group cursor-pointer ${
                    activeTopic?.id === chatTopic.id
                      ? "bg-[#f7f9fa] shadow-sm border border-[#526d82]/5 text-[#172033] font-medium"
                      : "text-[#667085] hover:text-[#344054] hover:bg-[#f7f9fa]/40"
                  }`}
                  onClick={() => onTopicClick(chatTopic, "chat")}
                >
                  <div className="flex items-center gap-2 overflow-hidden flex-1">
                    <MessageSquare className="w-3 h-3 flex-shrink-0" />
                    <span className="text-xs truncate">{chatTopic.title}</span>
                  </div>
                  <button
                    onClick={async (e) => {
                      e.stopPropagation();
                      if (!window.confirm("Bu sohbeti silmek istediğinizden emin misiniz?")) return;
                      try {
                        await TopicsAPI.delete(chatTopic.id);
                        setTopics(prev => prev.filter(t => t.id !== chatTopic.id && t.parentTopicId !== chatTopic.id));
                        if (activeTopic?.id === chatTopic.id) onTopicClick(null, "chat");
                        toast.success("Sohbet silindi");
                      } catch { toast.error("Silinemedi"); }
                    }}
                    className="opacity-0 group-hover:opacity-100 w-5 h-5 flex items-center justify-center text-[#667085] hover:text-red-400 hover:bg-red-500/10 rounded transition-all"
                    title="Sohbeti Sil"
                  >
                    <Trash2 className="w-3 h-3" />
                  </button>
                </div>
              ))}
            </div>
          </div>

          <div className="mx-3 mt-1 mb-2 border-t border-[#526d82]/10 opacity-50" />

          {/* 📚 ÖĞRENME MÜFREDATLARI — sadece başlıklar, tıklayınca sağa panel açılır */}
          <div className="px-3 pt-1 pb-2 flex-shrink-0">
            <div className="flex items-center justify-between pl-2 mb-2">
              <span className="text-[10px] font-bold text-[#667085] uppercase tracking-widest block">
                📚 Öğrenme Müfredatları
              </span>
            </div>
            <div className="space-y-0.5">
              {topics.filter(t => t.parentTopicId === null && (t.category || '').toLowerCase() === 'plan').length === 0 && (
                <p className="text-[11px] text-zinc-600 px-2 py-3 italic">Henüz müfredat yok</p>
              )}
              {topics.filter(t =>
                t.parentTopicId === null &&
                (t.category || '').toLowerCase() === 'plan'
              ).map((planTopic) => {
                const hasModules = topics.some(t => t.parentTopicId === planTopic.id);
                const isOpen = expandedPlanId === planTopic.id;
                return (
                  <div
                    key={planTopic.id}
                    className={`flex items-center justify-between w-full px-2 py-1.5 rounded-lg text-left transition-all duration-200 group cursor-pointer ${
                      isOpen
                        ? "bg-violet-900/30 text-violet-200 border border-violet-700/30 font-medium"
                        : activeTopic?.id === planTopic.id
                          ? "bg-[#f7f9fa] shadow-sm border border-[#526d82]/5 text-[#172033] font-medium"
                          : "text-[#5f6f7b] hover:text-[#172033] hover:bg-[#f7f9fa]/40"
                    }`}
                    onClick={() => {
                      onTopicClick(planTopic, "chat");
                      if (hasModules) {
                        setExpandedPlanId(isOpen ? null : planTopic.id);
                      }
                    }}
                  >
                    <div className="flex items-center gap-2 overflow-hidden flex-1">
                      <ChevronRight className={`w-3 h-3 flex-shrink-0 transition-transform duration-200 ${isOpen ? "rotate-90 text-violet-400" : ""}`} />
                      <span className="text-xs truncate">{planTopic.title}</span>
                    </div>
                    <button
                      onClick={async (e) => {
                        e.stopPropagation();
                        if (!window.confirm("Bu müfredatı (ve tüm alt derslerini) silmek istediğinizden emin misiniz?")) return;
                        try {
                          await TopicsAPI.delete(planTopic.id);
                          setTopics(prev => prev.filter(t => t.id !== planTopic.id && t.parentTopicId !== planTopic.id));
                          if (expandedPlanId === planTopic.id) setExpandedPlanId(null);
                          if (activeTopic?.id === planTopic.id || activeTopic?.parentTopicId === planTopic.id) {
                            onTopicClick(null, "plan");
                          }
                          toast.success("Müfredat silindi");
                        } catch { toast.error("Silinemedi"); }
                      }}
                      className="opacity-0 group-hover:opacity-100 w-5 h-5 flex items-center justify-center text-[#667085] hover:text-red-400 hover:bg-red-500/10 rounded transition-all"
                      title="Müfredatı Sil"
                    >
                      <Trash2 className="w-3 h-3" />
                    </button>
                  </div>
                );
              })}
            </div>
          </div>
        </div>

        {/* Footer */}
        <div className="mt-auto border-t border-[#526d82]/10 px-3 py-3 flex-shrink-0 bg-[#f7f9fa]/40 backdrop-blur-md">
          <button
            onClick={() => onViewChange("settings")}
            className={`flex items-center gap-2.5 w-full px-2 py-2 rounded-lg text-[13px] transition-colors duration-150 ${
              activeView === "settings"
                ? "bg-[#f7f9fa] shadow-sm border border-[#526d82]/5 text-[#172033] font-medium"
                : "text-[#667085] hover:text-[#344054] hover:bg-[#f7f9fa]/40"
            }`}
          >
            <Settings className="w-4 h-4 flex-shrink-0" />
            <span>{t("settings") || "Ayarlar"}</span>
          </button>
        </div>
      </motion.div>

      {/* ══════════ FLYOUT PANEL — Müfredat Detayı (yana açılır) ══════════ */}
      <AnimatePresence>
        {expandedPlan && (
          <motion.div
            initial={{ width: 0, opacity: 0 }}
            animate={{ width: 280, opacity: 1 }}
            exit={{ width: 0, opacity: 0 }}
            transition={{ duration: 0.25, ease: "easeInOut" }}
            className="bg-[#f7f9fa]/40 backdrop-blur-md/95 border-r border-[#526d82]/10 flex flex-col h-full overflow-hidden flex-shrink-0 backdrop-blur-sm"
          >
            {/* Panel Header */}
            <div className="px-4 py-3 border-b border-[#526d82]/10 flex items-center justify-between flex-shrink-0">
              <div className="flex items-center gap-2 overflow-hidden flex-1">
                <GraduationCap className="w-4 h-4 text-violet-400 flex-shrink-0" />
                <span className="text-sm font-semibold text-[#172033] truncate">{expandedPlan.title}</span>
              </div>
              <button
                onClick={() => setExpandedPlanId(null)}
                className="w-6 h-6 rounded-md flex items-center justify-center text-[#667085] hover:text-[#344054] hover:bg-[#eef1f3] transition-colors"
              >
                <X className="w-3.5 h-3.5" />
              </button>
            </div>

            {/* Progress Bar */}
            <div className="px-4 py-2 border-b border-zinc-800/30 flex-shrink-0">
              <div className="flex items-center justify-between mb-1">
                <span className="text-[10px] text-[#667085]">İlerleme</span>
                <span className="text-[10px] text-[#5f6f7b] font-medium">{Math.round(expandedPlan.progressPercentage || 0)}%</span>
              </div>
              <div className="w-full h-1.5 bg-[#eef1f3] rounded-full overflow-hidden">
                <div
                  className="h-full bg-gradient-to-r from-violet-500 to-emerald-500 transition-all duration-700"
                  style={{ width: `${expandedPlan.progressPercentage || 0}%` }}
                />
              </div>
            </div>

            {/* Module/Lesson Tree */}
            <div className="flex-1 overflow-y-auto px-3 py-3 space-y-3 custom-scrollbar-hide">
              {expandedModules.length === 0 && (
                <p className="text-[11px] text-zinc-600 italic px-2">Modül bulunamadı</p>
              )}
              {expandedModules.map((mod) => {
                const lessons = topics.filter(t => t.parentTopicId === mod.id).sort((a,b) => ((a as any).order || 0) - ((b as any).order || 0));
                const isLegacyLesson = lessons.length === 0;

                if (isLegacyLesson) {
                  return <div key={mod.id}>{renderLesson(mod)}</div>;
                }

                return (
                  <div key={mod.id}>
                    {/* Modül Başlığı */}
                    <div
                      className="flex items-center justify-between mb-1.5 px-1 py-1 rounded cursor-pointer hover:bg-[#eef1f3]/40 transition-colors"
                      onClick={() => toggleModule(mod.id)}
                    >
                      <div className="flex items-center gap-2">
                        <div className="flex items-center justify-center w-5 h-5 rounded-md bg-[#f7f9fa]/80 border border-zinc-800/80 text-[10px]">
                          {mod.emoji || "📦"}
                        </div>
                        <span className="text-[11px] font-semibold text-[#5f6f7b] tracking-wide truncate uppercase">
                          {mod.title}
                        </span>
                      </div>
                      <ChevronDown
                        className={`w-3.5 h-3.5 text-[#667085] transition-transform duration-200 ${expandedModuleIds.has(mod.id) ? "rotate-180" : ""}`}
                      />
                    </div>
                    {/* Dersler - AnimatePresence can be added later, basic conditional rendering for now */}
                    {expandedModuleIds.has(mod.id) && (
                      <div className="space-y-0.5 ml-2 pl-2 border-l border-[#526d82]/10">
                        {lessons.map((lesson) => renderLesson(lesson))}
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          </motion.div>
        )}
      </AnimatePresence>
      </div>
    </>
  );
}
