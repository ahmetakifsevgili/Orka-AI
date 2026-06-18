import { useEffect, useMemo, useState } from "react";
import {
  ChevronRight,
  Loader2,
  LogOut,
  PanelLeft,
  Plus,
  Settings as SettingsIcon,
} from "lucide-react";
import { motion, AnimatePresence } from "framer-motion";
import OrcaLogo from "./OrcaLogo";
import type { ApiTopic } from "@/lib/types";
import { APP_NAV_ITEMS, normalizeAppView } from "@/lib/appNavigation";
import { TopicsAPI } from "@/services/api";

interface LeftSidebarProps {
  topics: ApiTopic[];
  topicsLoading: boolean;
  activeTopic: ApiTopic | null;
  onTopicClick: (topic: ApiTopic | null, defaultMode?: "plan" | "chat") => void;
  onEnterChat: (topic: ApiTopic) => void;
  onTopicCreated: (topic: ApiTopic) => void;
  activeView: string;
  onViewChange: (view: string) => void;
  refreshTrigger: number;
  onLogout: () => void | Promise<void>;
  logoutLoading?: boolean;
}

function topicProgress(topic: ApiTopic) {
  const p = Math.max(0, Math.min(100, topic.progressPercentage ?? 0));
  if (topic.isMastered || p >= 100) return 100;
  return p;
}

function topicMeta(topic: ApiTopic) {
  const p = topicProgress(topic);
  if (p >= 100) return "tamamlandı";
  if (p > 0) return `%${Math.round(p)}`;
  return topic.category || "";
}

function formatDate(value?: string | Date | null) {
  if (!value) return "";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "";
  return new Intl.DateTimeFormat("tr-TR", { day: "2-digit", month: "short" }).format(date);
}

/* Primary nav — excludes settings (goes to bottom) */
const PRIMARY_NAV = APP_NAV_ITEMS.filter((i) => i.key !== "settings");

export default function LeftSidebar({
  topics,
  topicsLoading,
  activeTopic,
  onTopicClick,
  activeView,
  onViewChange,
  refreshTrigger,
  onLogout,
  logoutLoading = false,
}: LeftSidebarProps) {
  const [isPinned, setIsPinned] = useState(true);
  const [isHovered, setIsHovered] = useState(false);
  const [localTopics, setLocalTopics] = useState<ApiTopic[]>(topics);
  const [loading, setLoading] = useState(topicsLoading);

  useEffect(() => {
    setLocalTopics(topics);
    setLoading(topicsLoading);
  }, [topics, topicsLoading]);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    TopicsAPI.getAll()
      .then((response) => {
        if (!cancelled) setLocalTopics(response.data as ApiTopic[]);
      })
      .catch(() => {
        if (!cancelled) setLocalTopics(topics);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => { cancelled = true; };
  }, [refreshTrigger, topics]);

  const isExpanded = isPinned || isHovered;
  const activeKey = normalizeAppView(activeView);

  const recentTopics = useMemo(
    () =>
      localTopics
        .filter((t) => !t.parentTopicId)
        .sort((a, b) => new Date(b.updatedAt ?? b.createdAt ?? 0).getTime() - new Date(a.updatedAt ?? a.createdAt ?? 0).getTime())
        .slice(0, 10),
    [localTopics],
  );

  const W = isExpanded ? 252 : 60;

  return (
    <motion.aside
      animate={{ width: W }}
      transition={{ duration: 0.22, ease: [0.22, 1, 0.36, 1] }}
      onMouseEnter={() => setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
      className="relative z-20 flex h-screen shrink-0 flex-col bg-[#f7f9fa] border-r border-[#eef1f3]"
      style={{
        boxShadow: "2px 0 12px rgba(0,0,0,0.02)",
      }}
    >
      <div className="flex h-full min-h-0 flex-col py-4">
        {/* Logo + pin toggle */}
        <div className="mb-5 flex items-center justify-between px-3">
          <button
            type="button"
            onClick={() => onViewChange("home")}
            className="flex min-w-0 items-center gap-2.5 rounded-lg p-1 outline-none focus-visible:ring-2 focus-visible:ring-[#6ed7ce]/40"
            aria-label="Orka home"
          >
            <span
              className="grid h-7 w-7 flex-none place-items-center rounded-lg"
              style={{ background: "#6ed7ce" }}
            >
              <OrcaLogo className="h-4 w-4" style={{ color: "#041210" }} />
            </span>
            <AnimatePresence>
              {isExpanded && (
                <motion.span
                  initial={{ opacity: 0, x: -4 }}
                  animate={{ opacity: 1, x: 0 }}
                  exit={{ opacity: 0, x: -4 }}
                  transition={{ duration: 0.15 }}
                  className="text-[15px] font-bold tracking-tight text-[#172033]"
                >
                  Orka
                </motion.span>
              )}
            </AnimatePresence>
          </button>

          <AnimatePresence>
            {isExpanded && (
              <motion.button
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                type="button"
                onClick={() => setIsPinned((v) => !v)}
                className="grid h-7 w-7 flex-none place-items-center rounded-lg text-[#667085] transition hover:bg-[#eef1f3] hover:text-[#344054]"
                aria-label={isPinned ? "Sidebar'ı küçült" : "Sidebar'ı sabitle"}
              >
                <PanelLeft className="h-3.5 w-3.5" />
              </motion.button>
            )}
          </AnimatePresence>
        </div>

        {/* New topic button */}
        <div className="mb-4 px-2">
          <button
            id="tour-new-topic"
            type="button"
            onClick={() => onTopicClick(null, "plan")}
            className="flex h-9 w-full items-center gap-2.5 rounded-xl border px-3 text-[13px] font-medium transition"
            style={{
              border: "1px solid rgba(110,215,206,0.2)",
              background: "rgba(110,215,206,0.06)",
              color: "#6ed7ce",
            }}
          >
            <Plus className="h-4 w-4 flex-none" />
            <AnimatePresence>
              {isExpanded && (
                <motion.span
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                  exit={{ opacity: 0 }}
                  transition={{ duration: 0.1 }}
                  className="truncate"
                >
                  Yeni calisma
                </motion.span>
              )}
            </AnimatePresence>
          </button>
        </div>

        {/* Primary nav */}
        <nav className="space-y-0.5 px-2" aria-label="Ana navigasyon">
          {PRIMARY_NAV.map((item) => {
            const Icon = item.icon;
            const active = activeKey === item.key;
            const tourId = (() => {
              if (item.key === "home") return "tour-nav-dashboard";
              if (item.key === "tutor") return "tour-nav-learning";
              if (item.key === "sources-wiki") return "tour-nav-wiki";
              if (item.key === "notebook") return "tour-nav-ide";
              return undefined;
            })();

            return (
              <button
                key={item.key}
                id={tourId}
                type="button"
                onClick={() => onViewChange(item.view)}
                title={!isExpanded ? item.label : undefined}
                className={[
                  "group relative flex h-9 w-full items-center gap-2.5 rounded-xl px-3 text-[13px] font-medium transition-all",
                  active
                    ? "text-[#172033]"
                    : "text-[#667085] hover:bg-white/4 hover:text-[#344054]",
                ].join(" ")}
                style={active ? {
                  background: `rgba(${item.accent === "#6ed7ce" ? "110,215,206" : item.accent === "#a7e879" ? "167,232,121" : item.accent === "#b4a0f0" ? "180,160,240" : item.accent === "#dac17a" ? "218,193,122" : "255,255,255"}, 0.09)`,
                } : {}}
                aria-current={active ? "page" : undefined}
              >
                {/* Active left bar */}
                {active && (
                  <span
                    className="absolute left-0 top-1/2 h-4 w-0.5 -translate-y-1/2 rounded-full"
                    style={{ background: item.accent }}
                  />
                )}
                <Icon
                  className="h-4 w-4 flex-none"
                  style={{ color: active ? item.accent : undefined }}
                />
                <AnimatePresence>
                  {isExpanded && (
                    <motion.span
                      initial={{ opacity: 0 }}
                      animate={{ opacity: 1 }}
                      exit={{ opacity: 0 }}
                      transition={{ duration: 0.12 }}
                      className="min-w-0 truncate"
                    >
                      {item.label}
                    </motion.span>
                  )}
                </AnimatePresence>
              </button>
            );
          })}
        </nav>

        {/* Recent topics */}
        <div className="mt-4 min-h-0 flex-1 overflow-hidden px-2">
          <AnimatePresence>
            {isExpanded && (
              <motion.div
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                className="mb-2 flex items-center justify-between px-1"
              >
                <span className="text-[10px] font-bold uppercase tracking-widest text-[#667085]">
                  Gecmis calismalar
                </span>
                {loading && <Loader2 className="h-3 w-3 animate-spin text-[#3a403d]" />}
              </motion.div>
            )}
          </AnimatePresence>

          <div className="sidebar-scrollbar h-full space-y-0.5 overflow-y-auto">
            {recentTopics.length === 0 && !loading && isExpanded && (
              <div className="rounded-xl px-3 py-3 text-[12px] leading-5 text-[#3a403d]">
                İlk dersi başlat; Orka çalışma alanını hazırlar.
              </div>
            )}

            {recentTopics.map((topic) => {
              const selected = activeTopic?.id === topic.id;
              const progress = topicProgress(topic);
              const emoji = topic.emoji || topic.title?.charAt(0)?.toUpperCase() || "O";

              return (
                <button
                  key={topic.id}
                  type="button"
                  onClick={() => onTopicClick(topic, "chat")}
                  title={!isExpanded ? topic.title : undefined}
                  className={[
                    "group flex w-full items-center gap-2.5 rounded-xl px-2.5 py-2 text-left transition",
                    selected
                      ? "bg-[#6ed7ce]/8 text-[#172033] ring-1 ring-[#6ed7ce]/18"
                      : "text-[#667085] hover:bg-white/4 hover:text-[#344054]",
                  ].join(" ")}
                >
                  {/* Avatar */}
                  <span
                    className="grid h-7 w-7 flex-none place-items-center rounded-lg text-[11px] font-bold"
                    style={{
                      background: selected ? "rgba(110,215,206,0.12)" : "rgba(255,255,255,0.04)",
                      color: selected ? "#6ed7ce" : "#5a6360",
                    }}
                  >
                    {emoji.slice(0, 2)}
                  </span>

                  <AnimatePresence>
                    {isExpanded && (
                      <motion.span
                        initial={{ opacity: 0 }}
                        animate={{ opacity: 1 }}
                        exit={{ opacity: 0 }}
                        transition={{ duration: 0.12 }}
                        className="min-w-0 flex-1"
                      >
                        <span className="block truncate text-[13px] font-medium">
                          {topic.title || "Başlıksız çalışma"}
                        </span>
                        <span className="flex items-center gap-1.5 text-[11px] text-[#3a403d]">
                          {topicMeta(topic) && <span>{topicMeta(topic)}</span>}
                          {formatDate(topic.updatedAt ?? topic.createdAt) && (
                            <>
                              <span className="h-0.5 w-0.5 rounded-full bg-[#3a403d]" />
                              <span>{formatDate(topic.updatedAt ?? topic.createdAt)}</span>
                            </>
                          )}
                        </span>
                        {/* Progress bar */}
                        {progress > 0 && progress < 100 && (
                          <span className="mt-1.5 block h-0.5 w-full overflow-hidden rounded-full bg-white/6">
                            <span
                              className="block h-full rounded-full"
                              style={{
                                width: `${progress}%`,
                                background: "linear-gradient(90deg, #6ed7ce, #a7e879)",
                              }}
                            />
                          </span>
                        )}
                      </motion.span>
                    )}
                  </AnimatePresence>

                  {isExpanded && selected && (
                    <ChevronRight className="h-3.5 w-3.5 flex-none text-[#6ed7ce]" />
                  )}
                </button>
              );
            })}
          </div>
        </div>

        {/* Bottom — settings + user */}
        <div className="mt-3 border-t px-2 pt-3" style={{ borderColor: "rgba(255,255,255,0.06)" }}>
          <button
            type="button"
            onClick={() => onViewChange("settings")}
            title={!isExpanded ? "Settings / Safety" : undefined}
            className={[
              "mb-2 flex h-9 w-full items-center gap-2.5 rounded-xl px-3 text-[13px] font-medium transition",
              activeView === "settings"
                ? "bg-white/8 text-[#172033]"
                : "text-[#667085] hover:bg-white/4 hover:text-[#344054]",
            ].join(" ")}
          >
            <SettingsIcon className="h-4 w-4 flex-none" />
            <AnimatePresence>
              {isExpanded && (
                <motion.span initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}>
                  Settings / Safety
                </motion.span>
              )}
            </AnimatePresence>
          </button>

          {/* User row */}
          <div
            className="flex items-center gap-2.5 rounded-xl p-2"
            style={{ background: "rgba(255,255,255,0.03)" }}
          >
            <div
              className="grid h-8 w-8 flex-none place-items-center rounded-lg text-[12px] font-bold text-[#172033]"
              style={{ background: "rgba(110,215,206,0.12)", border: "1px solid rgba(110,215,206,0.2)" }}
            >
              A
            </div>
            <AnimatePresence>
              {isExpanded && (
                <motion.div
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                  exit={{ opacity: 0 }}
                  className="min-w-0 flex-1"
                >
                  <p className="truncate text-[13px] font-medium text-[#172033]">Öğrenci</p>
                  <p className="text-[11px] text-[#3a403d]">Ücretsiz plan</p>
                </motion.div>
              )}
            </AnimatePresence>
            <AnimatePresence>
              {isExpanded && (
                <motion.button
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                  exit={{ opacity: 0 }}
                  type="button"
                  onClick={onLogout}
                  disabled={logoutLoading}
                  className="grid h-7 w-7 flex-none place-items-center rounded-lg text-[#3a403d] transition hover:bg-white/6 hover:text-[#8f9894] disabled:opacity-40"
                  aria-label="Log out"
                >
                  {logoutLoading ? (
                    <Loader2 className="h-3.5 w-3.5 animate-spin" />
                  ) : (
                    <LogOut className="h-3.5 w-3.5" />
                  )}
                </motion.button>
              )}
            </AnimatePresence>
          </div>
        </div>
      </div>
    </motion.aside>
  );
}
