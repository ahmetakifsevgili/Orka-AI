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
  Library,
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
  GitBranch,
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
  { id: "chat",      icon: Home,            labelKey: "home",      route: null },
  { id: "dashboard", icon: LayoutDashboard, labelKey: "Dashboard", route: null },
  { id: "skilltree", icon: GitBranch, labelKey: "skilltree", route: null },
  { id: "wiki",      icon: BookMarked,      labelKey: "wiki",      route: null },
  { id: "research",  icon: Library,         labelKey: "research",  route: null },
  { id: "ide",       icon: Code2,           labelKey: "ide",       route: null },
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
  const [expandedPlanId, setExpandedPlanId] = useState<string | null>(null);
  
  // Accordion state for modules inside a generic plan
  const [expandedModuleIds, setExpandedModuleIds] = useState<Set<string>>(new Set());

  const toggleModule = (modId: string) => {
    setExpandedModuleIds(prev => {
      const next = new Set(prev);
      if (next.has(modId)) next.delete(modId);
      else next.add(modId);
      return next;
    });
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
      <div className="w-12 soft-surface border-r flex flex-col items-center py-4 flex-shrink-0">
        <button
          onClick={() => setCollapsed(false)}
          className="w-8 h-8 rounded-lg flex items-center justify-center soft-text-muted hover:text-foreground hover:bg-surface-muted transition-colors duration-150 mb-4"
          title="Genişlet"
        >
          <PanelLeft className="w-4 h-4" />
        </button>
        <OrcaLogo className="w-5 h-5 soft-text-muted" />
      </div>
    );
  }

  // Flyout panel: hangi müfredat genişletilmiş
  const expandedPlan = topics.find(t => t.id === expandedPlanId);
  const expandedModules = expandedPlan
    ? topics.filter(t => t.parentTopicId === expandedPlan.id).sort((a,b) => ((a as any).order || 0) - ((b as any).order || 0))
    : [];

  const renderLesson = (topicToRender: ApiTopic) => {
    const isActive = activeTopic?.id === topicToRender.id;
    const isCompleted = topicToRender.isMastered || topicToRender.progressPercentage === 100;
    return (
      <div
        key={topicToRender.id}
        onClick={() => onTopicClick(topicToRender)}
        role="button"
        tabIndex={0}
        className={`flex flex-col items-stretch w-full px-3 py-1.5 rounded-md text-left transition-all duration-150 relative group cursor-pointer ${
          isActive
            ? "text-foreground font-medium soft-muted border soft-border"
            : "soft-text-muted hover:text-foreground hover:bg-surface-muted"
        } ${isCompleted ? 'border-l-2 border-emerald-600/40 bg-emerald-500/10' : ''}`}
      >
        <div className="flex items-center gap-2.5 w-full relative">
          {isCompleted ? (
            <div className="w-4 h-4 rounded-full bg-emerald-500/20 border border-emerald-500/40 flex items-center justify-center">
              <Check className="w-2.5 h-2.5 text-emerald-400 stroke-[2.5]" />
            </div>
          ) : isActive ? (
            <div className="w-4 h-4 rounded-full bg-foreground/10 border border-soft-border flex items-center justify-center flex-shrink-0">
              <ChevronRight className="w-2.5 h-2.5 text-foreground" />
            </div>
          ) : (
            <div className="w-1.5 h-1.5 rounded-full bg-zinc-700 flex-shrink-0 ml-1" />
          )}
          <span className={`text-[12px] truncate flex-1 ${isCompleted ? 'soft-text-muted font-medium' : isActive ? 'text-foreground font-medium' : 'soft-text-muted'}`}>
            {topicToRender.title}
          </span>
          <div className="flex items-center gap-1">
            <button
              onClick={(e) => { e.stopPropagation(); onViewChange(`wiki:${topicToRender.id}`); }}
              title="Ders Wiki'si"
              className="opacity-70 group-hover:opacity-100 p-1 rounded soft-text-muted hover:text-amber-700 hover:bg-amber-500/10 transition-all duration-200"
            >
              <BookMarked className="w-3 h-3" />
            </button>
            <button
              onClick={(e) => { e.stopPropagation(); onEnterChat(topicToRender); }}
              title="Derse Başla"
              className={`opacity-0 group-hover:opacity-100 px-2 py-0.5 rounded text-[9px] font-medium transition-all duration-200 ${
                isActive ? "bg-foreground text-background" : "bg-emerald-500/10 text-emerald-700 dark:text-emerald-300 hover:bg-emerald-500/20 border border-emerald-500/20"
              }`}
            >
              {isActive ? "Derse Git" : "Devam Et"}
            </button>
          </div>
        </div>
        {!isCompleted && (topicToRender.progressPercentage ?? 0) > 0 && (
          <div className="mt-1 ml-6 w-full h-0.5 soft-muted rounded-full overflow-hidden">
            <div className="h-full bg-zinc-400 transition-all duration-500" style={{ width: `${topicToRender.progressPercentage}%` }} />
          </div>
        )}
      </div>
    );
  };

  return (
    <div className="flex flex-row h-full flex-shrink-0">
      {/* ══════════ ANA SIDEBAR (260px sabit) ══════════ */}
      <motion.div
        initial={{ width: 260 }}
        animate={{ width: 260 }}
        className="w-[260px] soft-surface border-r flex flex-col h-full flex-shrink-0"
      >
        {/* Header */}
        <div className="px-3 py-3 flex items-center justify-between flex-shrink-0">
          <div className="flex items-center gap-2.5">
            <OrcaLogo className="w-5 h-5 text-foreground" />
            <span className="text-sm font-semibold text-foreground tracking-tight">
              Orka AI
            </span>
          </div>
          <button
            onClick={() => setCollapsed(true)}
            className="w-7 h-7 rounded-md flex items-center justify-center soft-text-muted hover:text-foreground hover:bg-surface-muted transition-colors duration-150"
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
              if (item.labelKey === "Dashboard") return "Dashboard";
              if (item.labelKey === "skilltree") return "Yetenek Ağı";
              if (item.labelKey === "courses") return t("courses") || "Kurslar";
              if (item.labelKey === "wiki") return t("wiki") || "Wiki";
              if (item.labelKey === "research") return "Korteks Kütüphanesi";
              if (item.labelKey === "ide") return "Kod Editörü";
              return item.labelKey;
            })();
            return (
              <button
                key={item.id}
                onClick={() => item.route ? navigate(item.route) : onViewChange(item.id)}
                className={`flex items-center gap-2.5 w-full px-3 py-2 rounded-lg text-[13px] transition-colors duration-150 mb-0.5 ${
                  isActive
                    ? "soft-muted text-foreground font-medium"
                    : "soft-text-muted hover:text-foreground hover:bg-surface-muted"
                }`}
              >
                <item.icon className="w-4 h-4 flex-shrink-0" />
                <span className="truncate">{label}</span>
              </button>
            );
          })}
        </div>

        <div className="mx-3 border-t soft-border" />

        {/* Ana Scroll View */}
        <div className="flex-1 flex flex-col overflow-y-auto overflow-x-hidden min-h-0 custom-scrollbar-hide">
          
          {/* 💬 SOHBET GEÇMİŞİ */}
          <div className="px-3 pt-3 flex-shrink-0 mb-3">
            <div className="flex items-center justify-between pl-2 mb-2">
              <span className="text-[10px] font-bold soft-text-muted uppercase tracking-widest block">
                Sohbet Geçmişi
              </span>
              <button
                onClick={() => onTopicClick(null, "chat")}
                title="Yeni Sohbet Başlat"
                className="w-6 h-6 flex items-center justify-center rounded-md soft-text-muted hover:text-foreground hover:bg-surface-muted transition-all duration-200"
              >
                <Plus className="w-3.5 h-3.5" />
              </button>
            </div>
            <div className="space-y-0.5">
              {topics.filter(t => t.parentTopicId === null && (t.category || '').toLowerCase() !== 'plan').length === 0 && (
                <p className="text-[11px] soft-text-muted px-2 py-3 italic">Henüz sohbet yok</p>
              )}
              {topics.filter(t => t.parentTopicId === null && (t.category || '').toLowerCase() !== 'plan').map((chatTopic) => (
                <div
                  key={chatTopic.id}
                  className={`flex items-center justify-between w-full px-2 py-1.5 rounded-lg text-left transition-colors duration-150 group cursor-pointer ${
                    activeTopic?.id === chatTopic.id
                      ? "soft-muted text-foreground font-medium"
                      : "soft-text-muted hover:text-foreground hover:bg-surface-muted"
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
                    className="opacity-60 group-hover:opacity-100 w-5 h-5 flex items-center justify-center soft-text-muted hover:text-amber-700 hover:bg-amber-500/10 rounded transition-all"
                    title="Sohbeti Sil"
                  >
                    <Trash2 className="w-3 h-3" />
                  </button>
                </div>
              ))}
            </div>
          </div>

          <div className="mx-3 mt-1 mb-2 border-t soft-border opacity-70" />

          {/* 📚 ÖĞRENME MÜFREDATLARI — sadece başlıklar, tıklayınca sağa panel açılır */}
          <div className="px-3 pt-1 pb-2 flex-shrink-0">
            <div className="flex items-center justify-between pl-2 mb-2">
              <span className="text-[10px] font-bold soft-text-muted uppercase tracking-widest block">
                Öğrenme Müfredatları
              </span>
            </div>
            <div className="space-y-0.5">
              {topics.filter(t => t.parentTopicId === null && (t.category || '').toLowerCase() === 'plan').length === 0 && (
                <p className="text-[11px] soft-text-muted px-2 py-3 italic">Henüz müfredat yok</p>
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
                        ? "bg-emerald-500/10 text-emerald-800 dark:text-emerald-200 border border-emerald-500/20 font-medium"
                        : activeTopic?.id === planTopic.id
                          ? "soft-muted text-foreground font-medium"
                          : "soft-text-muted hover:text-foreground hover:bg-surface-muted"
                    }`}
                    onClick={() => {
                      onTopicClick(planTopic, "chat");
                      if (hasModules) {
                        setExpandedPlanId(isOpen ? null : planTopic.id);
                      }
                    }}
                  >
                    <div className="flex items-center gap-2 overflow-hidden flex-1">
                      <ChevronRight className={`w-3 h-3 flex-shrink-0 transition-transform duration-200 ${isOpen ? "rotate-90 text-emerald-600" : ""}`} />
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
                      className="opacity-60 group-hover:opacity-100 w-5 h-5 flex items-center justify-center soft-text-muted hover:text-amber-700 hover:bg-amber-500/10 rounded transition-all"
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
        <div className="mt-auto border-t soft-border px-3 py-3 flex-shrink-0 soft-surface">
          <button
            onClick={() => onViewChange("settings")}
            className={`flex items-center gap-2.5 w-full px-2 py-2 rounded-lg text-[13px] transition-colors duration-150 ${
              activeView === "settings"
                ? "soft-muted text-foreground font-medium"
                : "soft-text-muted hover:text-foreground hover:bg-surface-muted"
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
            animate={{ width: 340, opacity: 1 }}
            exit={{ width: 0, opacity: 0 }}
            transition={{ duration: 0.3, ease: "easeInOut" }}
            className="soft-surface border-r flex flex-col h-full overflow-hidden flex-shrink-0 soft-shadow"
          >
            {/* Panel Header */}
            <div className="px-4 py-3 border-b soft-border flex items-center justify-between flex-shrink-0">
              <div className="flex items-center gap-2 overflow-hidden flex-1">
                <GraduationCap className="w-4 h-4 text-emerald-600 flex-shrink-0" />
                <span className="text-sm font-semibold text-foreground truncate">{expandedPlan.title}</span>
              </div>
              <button
                onClick={() => setExpandedPlanId(null)}
                className="w-6 h-6 rounded-md flex items-center justify-center soft-text-muted hover:text-foreground hover:bg-surface-muted transition-colors"
              >
                <X className="w-3.5 h-3.5" />
              </button>
            </div>

            {/* Progress Bar */}
            <div className="px-4 py-2 border-b soft-border flex-shrink-0">
              <div className="flex items-center justify-between mb-1">
                <span className="text-[10px] soft-text-muted">İlerleme</span>
                <span className="text-[10px] soft-text-muted font-medium">{Math.round(expandedPlan.progressPercentage || 0)}%</span>
              </div>
              <div className="w-full h-1.5 soft-muted rounded-full overflow-hidden">
                <div
                  className="h-full bg-emerald-500 transition-all duration-700"
                  style={{ width: `${expandedPlan.progressPercentage || 0}%` }}
                />
              </div>
            </div>

            {/* Module/Lesson Tree */}
            <div className="flex-1 overflow-y-auto px-3 py-3 space-y-3 custom-scrollbar-hide">
              {expandedModules.length === 0 && (
                <p className="text-[11px] soft-text-muted italic px-2">Modül bulunamadı</p>
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
                      className={`flex items-center justify-between mb-1.5 px-2 py-1.5 rounded-md cursor-pointer transition-colors ${expandedModuleIds.has(mod.id) ? 'soft-muted' : 'hover:bg-surface-muted'}`}
                      onClick={() => toggleModule(mod.id)}
                    >
                      <div className="flex items-center gap-2">
                        <div className="flex items-center justify-center w-5 h-5 rounded-md soft-muted border soft-border text-[10px]">
                          {mod.emoji || "📦"}
                        </div>
                        <span className="text-[11px] font-semibold soft-text-muted tracking-wide truncate uppercase">
                          {mod.title}
                        </span>
                      </div>
                      <ChevronDown 
                        className={`w-3.5 h-3.5 soft-text-muted transition-transform duration-200 ${expandedModuleIds.has(mod.id) ? "rotate-180" : ""}`} 
                      />
                    </div>
                    {/* Dersler - AnimatePresence can be added later, basic conditional rendering for now */}
                    {expandedModuleIds.has(mod.id) && (
                      <div className="space-y-0.5 ml-2 pl-2 border-l soft-border">
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
  );
}
