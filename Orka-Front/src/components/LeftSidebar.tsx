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

function formatDate(value?: string | Date | null) {
  if (!value) return "";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "";
  return new Intl.DateTimeFormat("tr-TR", { day: "2-digit", month: "short" }).format(date);
}

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
      .then((r) => { if (!cancelled) setLocalTopics(r.data as ApiTopic[]); })
      .catch(() => { if (!cancelled) setLocalTopics(topics); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [refreshTrigger, topics]);

  const isExpanded = isPinned || isHovered;
  const activeKey = normalizeAppView(activeView);
  const W = isExpanded ? 240 : 56;

  const recentTopics = useMemo(
    () =>
      localTopics
        .filter((t) => !t.parentTopicId)
        .sort((a, b) => new Date(b.updatedAt ?? b.createdAt ?? 0).getTime() - new Date(a.updatedAt ?? a.createdAt ?? 0).getTime())
        .slice(0, 12),
    [localTopics],
  );

  return (
    <motion.aside
      animate={{ width: W }}
      transition={{ duration: 0.2, ease: [0.22, 1, 0.36, 1] }}
      onMouseEnter={() => setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
      className="relative z-20 flex h-screen shrink-0 flex-col"
      style={{
        background: "var(--orka-bg)",
        borderRight: "1px solid var(--orka-border)",
      }}
    >
      <div className="flex h-full min-h-0 flex-col py-3">

        {/* Logo + pin */}
        <div className="mb-4 flex items-center justify-between px-3">
          <button
            type="button"
            onClick={() => onViewChange("home")}
            className="flex min-w-0 items-center gap-2 rounded-lg p-1 outline-none focus-visible:ring-1 focus-visible:ring-[#6ed7ce]/40"
            aria-label="Orka home"
          >
            <span
              className="grid h-6 w-6 flex-none place-items-center rounded-md"
              style={{ background: "#6ed7ce" }}
            >
              <OrcaLogo className="h-3.5 w-3.5" style={{ color: "#041210" }} />
            </span>
            <AnimatePresence>
              {isExpanded && (
                <motion.span
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                  exit={{ opacity: 0 }}
                  transition={{ duration: 0.12 }}
                  className="text-[14px] font-semibold tracking-tight"
                  style={{ color: "var(--orka-text)" }}
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
                className="grid h-6 w-6 flex-none place-items-center rounded-md transition"
                style={{ color: "var(--orka-text-4)" }}
                aria-label={isPinned ? "Daralt" : "Sabitle"}
              >
                <PanelLeft className="h-3.5 w-3.5" />
              </motion.button>
            )}
          </AnimatePresence>
        </div>

        {/* New chat button */}
        <div className="mb-3 px-2">
          <button
            type="button"
            onClick={() => onTopicClick(null, "chat")}
            className="flex h-8 w-full items-center gap-2 rounded-lg px-2.5 text-[12.5px] font-medium transition"
            style={{
              border: "1px solid var(--orka-border)",
              background: "var(--orka-surface-2)",
              color: "var(--orka-text-2)",
            }}
            onMouseEnter={(e) => {
              (e.currentTarget as HTMLButtonElement).style.color = "var(--orka-text)";
              (e.currentTarget as HTMLButtonElement).style.borderColor = "var(--orka-border-2)";
            }}
            onMouseLeave={(e) => {
              (e.currentTarget as HTMLButtonElement).style.color = "var(--orka-text-2)";
              (e.currentTarget as HTMLButtonElement).style.borderColor = "var(--orka-border)";
            }}
          >
            <Plus className="h-3.5 w-3.5 flex-none" style={{ color: "var(--orka-text-3)" }} />
            <AnimatePresence>
              {isExpanded && (
                <motion.span
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                  exit={{ opacity: 0 }}
                  transition={{ duration: 0.1 }}
                  className="truncate"
                >
                  Yeni Sohbet
                </motion.span>
              )}
            </AnimatePresence>
          </button>
        </div>

        {/* Nav items */}
        <nav className="space-y-0.5 px-2" aria-label="Ana navigasyon">
          {PRIMARY_NAV.map((item) => {
            const Icon = item.icon;
            const active = activeKey === item.key;
            return (
              <button
                key={item.key}
                type="button"
                onClick={() => onViewChange(item.view)}
                title={!isExpanded ? item.label : undefined}
                className="group relative flex h-8 w-full items-center gap-2.5 rounded-lg px-2.5 text-[12.5px] font-medium transition-all"
                style={{
                  background: active ? "var(--orka-surface-3)" : "transparent",
                  color: active ? "var(--orka-text)" : "var(--orka-text-3)",
                }}
                onMouseEnter={(e) => {
                  if (!active) {
                    (e.currentTarget as HTMLButtonElement).style.background = "var(--orka-surface-2)";
                    (e.currentTarget as HTMLButtonElement).style.color = "var(--orka-text-2)";
                  }
                }}
                onMouseLeave={(e) => {
                  if (!active) {
                    (e.currentTarget as HTMLButtonElement).style.background = "transparent";
                    (e.currentTarget as HTMLButtonElement).style.color = "var(--orka-text-3)";
                  }
                }}
                aria-current={active ? "page" : undefined}
              >
                <Icon
                  className="h-3.5 w-3.5 flex-none shrink-0"
                  style={{ color: active ? "var(--orka-teal)" : undefined }}
                />
                <AnimatePresence>
                  {isExpanded && (
                    <motion.span
                      initial={{ opacity: 0 }}
                      animate={{ opacity: 1 }}
                      exit={{ opacity: 0 }}
                      transition={{ duration: 0.1 }}
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

        {/* Divider */}
        <div className="mx-3 my-3" style={{ height: "1px", background: "var(--orka-border-3)" }} />

        {/* Recent topics */}
        <div className="min-h-0 flex-1 overflow-hidden px-2">
          <AnimatePresence>
            {isExpanded && (
              <motion.div
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                className="mb-1.5 flex items-center justify-between px-1"
              >
                <span
                  className="text-[10px] font-semibold uppercase tracking-widest"
                  style={{ color: "var(--orka-text-4)" }}
                >
                  Geçmiş
                </span>
                {loading && <Loader2 className="h-3 w-3 animate-spin" style={{ color: "var(--orka-text-4)" }} />}
              </motion.div>
            )}
          </AnimatePresence>

          <div className="sidebar-scrollbar h-full space-y-0.5 overflow-y-auto pb-2">
            {recentTopics.length === 0 && !loading && isExpanded && (
              <div
                className="rounded-lg px-2.5 py-2.5 text-[12px] leading-5"
                style={{ color: "var(--orka-text-4)" }}
              >
                Henuz sohbet yok.
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
                  className="group flex w-full items-center gap-2 rounded-lg px-2 py-1.5 text-left transition"
                  style={{
                    background: selected ? "var(--orka-surface-3)" : "transparent",
                    color: selected ? "var(--orka-text)" : "var(--orka-text-3)",
                  }}
                  onMouseEnter={(e) => {
                    if (!selected) {
                      (e.currentTarget as HTMLButtonElement).style.background = "var(--orka-surface-2)";
                      (e.currentTarget as HTMLButtonElement).style.color = "var(--orka-text-2)";
                    }
                  }}
                  onMouseLeave={(e) => {
                    if (!selected) {
                      (e.currentTarget as HTMLButtonElement).style.background = "transparent";
                      (e.currentTarget as HTMLButtonElement).style.color = "var(--orka-text-3)";
                    }
                  }}
                >
                  {/* Avatar */}
                  <span
                    className="grid h-5 w-5 flex-none place-items-center rounded text-[10px] font-bold shrink-0"
                    style={{
                      background: selected ? "var(--orka-teal-bg)" : "var(--orka-surface-3)",
                      color: selected ? "var(--orka-teal)" : "var(--orka-text-4)",
                    }}
                  >
                    {emoji.slice(0, 1)}
                  </span>

                  <AnimatePresence>
                    {isExpanded && (
                      <motion.span
                        initial={{ opacity: 0 }}
                        animate={{ opacity: 1 }}
                        exit={{ opacity: 0 }}
                        transition={{ duration: 0.1 }}
                        className="min-w-0 flex-1"
                      >
                        <span className="block truncate text-[12.5px] font-medium">
                          {topic.title || "Basliksiz"}
                        </span>
                        <span
                          className="flex items-center gap-1 text-[11px]"
                          style={{ color: "var(--orka-text-5)" }}
                        >
                          {formatDate(topic.updatedAt ?? topic.createdAt)}
                          {progress > 0 && progress < 100 && (
                            <>
                              <span>·</span>
                              <span>{Math.round(progress)}%</span>
                            </>
                          )}
                        </span>
                        {progress > 0 && progress < 100 && (
                          <span
                            className="mt-1 block h-px w-full overflow-hidden rounded-full"
                            style={{ background: "var(--orka-surface-3)" }}
                          >
                            <span
                              className="block h-full rounded-full"
                              style={{
                                width: `${progress}%`,
                                background: "var(--orka-teal)",
                                opacity: 0.6,
                              }}
                            />
                          </span>
                        )}
                      </motion.span>
                    )}
                  </AnimatePresence>

                  {isExpanded && selected && (
                    <ChevronRight
                      className="h-3 w-3 flex-none shrink-0"
                      style={{ color: "var(--orka-teal)" }}
                    />
                  )}
                </button>
              );
            })}
          </div>
        </div>

        {/* Bottom — settings + logout */}
        <div
          className="mt-2 px-2 pt-2"
          style={{ borderTop: "1px solid var(--orka-border-3)" }}
        >
          <button
            type="button"
            onClick={() => onViewChange("settings")}
            title={!isExpanded ? "Ayarlar" : undefined}
            className="mb-1 flex h-8 w-full items-center gap-2.5 rounded-lg px-2.5 text-[12.5px] font-medium transition"
            style={{
              background: activeView === "settings" ? "var(--orka-surface-3)" : "transparent",
              color: activeView === "settings" ? "var(--orka-text)" : "var(--orka-text-3)",
            }}
            onMouseEnter={(e) => {
              if (activeView !== "settings") {
                (e.currentTarget as HTMLButtonElement).style.background = "var(--orka-surface-2)";
                (e.currentTarget as HTMLButtonElement).style.color = "var(--orka-text-2)";
              }
            }}
            onMouseLeave={(e) => {
              if (activeView !== "settings") {
                (e.currentTarget as HTMLButtonElement).style.background = "transparent";
                (e.currentTarget as HTMLButtonElement).style.color = "var(--orka-text-3)";
              }
            }}
          >
            <SettingsIcon className="h-3.5 w-3.5 flex-none" />
            <AnimatePresence>
              {isExpanded && (
                <motion.span initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}>
                  Ayarlar
                </motion.span>
              )}
            </AnimatePresence>
          </button>

          {/* User row */}
          <div
            className="flex items-center gap-2 rounded-lg px-2 py-1.5"
            style={{ background: "var(--orka-surface-2)" }}
          >
            <div
              className="grid h-6 w-6 flex-none place-items-center rounded text-[11px] font-bold shrink-0"
              style={{
                background: "var(--orka-teal-bg)",
                border: "1px solid var(--orka-teal-border)",
                color: "var(--orka-teal)",
              }}
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
                  <p
                    className="truncate text-[12.5px] font-medium"
                    style={{ color: "var(--orka-text-2)" }}
                  >
                    Ogrenci
                  </p>
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
                  className="grid h-6 w-6 flex-none place-items-center rounded transition disabled:opacity-40"
                  style={{ color: "var(--orka-text-4)" }}
                  aria-label="Cikis yap"
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
