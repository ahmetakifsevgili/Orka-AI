import { useEffect, useState } from "react";
import {
  Activity,
  ArrowRight,
  Award,
  BookOpen,
  CheckCircle2,
  Cpu,
  Flame,
  Target,
  TrendingUp,
} from "lucide-react";
import { useQuizHistory } from "@/contexts/QuizHistoryContext";
import { DashboardAPI, QuizAPI, storage, UserAPI } from "@/services/api";
import type { ApiDashboardStats, ApiGamification, ApiGlobalStats, ApiTopic } from "@/lib/types";
import SystemHealthHUD from "@/components/SystemHealthHUD";

interface DashboardPanelProps {
  topics: ApiTopic[];
  onViewChange: (view: string) => void;
}

function StatTile({
  label,
  value,
  icon: Icon,
  tone = "neutral",
}: {
  label: string;
  value: string | number;
  icon: typeof Activity;
  tone?: "neutral" | "success" | "warning";
}) {
  const toneClass =
    tone === "success"
      ? "text-emerald-700 dark:text-emerald-300 bg-emerald-500/10"
      : tone === "warning"
        ? "text-amber-700 dark:text-amber-300 bg-amber-500/10"
        : "text-foreground soft-muted";

  return (
    <div className="soft-surface border rounded-xl p-5">
      <div className={`mb-4 flex h-9 w-9 items-center justify-center rounded-lg ${toneClass}`}>
        <Icon className="h-4 w-4" />
      </div>
      <p className="text-2xl font-semibold tracking-tight text-foreground">{value}</p>
      <p className="mt-1 text-xs font-medium uppercase tracking-wide soft-text-muted">{label}</p>
    </div>
  );
}

export default function DashboardPanel({ topics, onViewChange }: DashboardPanelProps) {
  const { attempts: sessionAttempts } = useQuizHistory();
  const isAdmin = storage.getUser()?.isAdmin === true;
  const [activeTab, setActiveTab] = useState<"summary" | "hud">("summary");
  const [stats, setStats] = useState<ApiGlobalStats | null>(null);
  const [dashStats, setDashStats] = useState<ApiDashboardStats | null>(null);
  const [gamification, setGamification] = useState<ApiGamification | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    QuizAPI.getGlobalStats()
      .then((res) => setStats(res.data))
      .catch((err) => console.error("Quiz stats fetch error:", err));

    UserAPI.getGamification()
      .then((res) => setGamification(res.data as ApiGamification))
      .catch((err) => console.error("Gamification fetch error:", err));

    DashboardAPI.getStats()
      .then((res) => setDashStats(res.data as ApiDashboardStats))
      .catch((err) => console.error("Dashboard stats fetch error:", err))
      .finally(() => setLoading(false));
  }, [sessionAttempts.length]);

  const accuracy = stats?.accuracy ?? 0;
  const totalLessons = dashStats?.totalSections ?? topics.reduce((sum, t) => sum + (t.totalSections ?? 0), 0);
  const completedLessons =
    dashStats?.completedSections ?? topics.reduce((sum, t) => sum + (t.completedSections ?? 0), 0);
  const totalXP = dashStats?.totalXP ?? 0;
  const activeStreak = dashStats?.currentStreak ?? stats?.dailyProgress.filter((d) => d.total > 0).length ?? 0;
  const activeTopics = topics.filter((topic) => !topic.parentTopicId).slice(0, 5);
  const quests = dashStats?.dailyQuests ?? [];
  const level = gamification?.level ? `Seviye ${gamification.level}` : "Öğrenme özeti";

  return (
    <div className="flex-1 overflow-y-auto soft-page">
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-8 px-8 py-8">
        <div className="flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
          <div>
            <p className="text-xs font-medium uppercase tracking-wide soft-text-muted">{level}</p>
            <h1 className="mt-2 text-2xl font-semibold tracking-tight text-foreground">Bugünkü öğrenme durumu</h1>
            <p className="mt-2 max-w-2xl text-sm leading-relaxed soft-text-muted">
              {dashStats?.motivationalMessage || "Kaldığın yerden devam et. Sohbet ana akışın, burası sadece yolu netleştirir."}
            </p>
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={() => setActiveTab("summary")}
              className={`rounded-lg px-3 py-2 text-xs font-medium transition-colors ${
                activeTab === "summary" ? "soft-muted text-foreground" : "soft-text-muted hover:bg-surface-muted"
              }`}
            >
              Özet
            </button>
            {isAdmin && (
              <button
                onClick={() => setActiveTab("hud")}
                className={`flex items-center gap-2 rounded-lg px-3 py-2 text-xs font-medium transition-colors ${
                  activeTab === "hud" ? "bg-amber-500/10 text-amber-700 dark:text-amber-300" : "soft-text-muted hover:bg-surface-muted"
                }`}
              >
                <Cpu className="h-3.5 w-3.5" />
                Sistem
              </button>
            )}
          </div>
        </div>

        {activeTab === "hud" && isAdmin ? (
          <div className="overflow-hidden rounded-xl border soft-border soft-surface">
            <SystemHealthHUD />
          </div>
        ) : (
          <>
            <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
              <StatTile label="Toplam XP" value={loading ? "-" : totalXP} icon={TrendingUp} />
              <StatTile
                label="Tamamlanan ders"
                value={loading ? "-" : totalLessons > 0 ? `${completedLessons}/${totalLessons}` : topics.length}
                icon={BookOpen}
                tone="success"
              />
              <StatTile label="Doğruluk" value={loading ? "-" : `%${accuracy}`} icon={Target} tone="success" />
              <StatTile label="Günlük seri" value={loading ? "-" : activeStreak} icon={Flame} tone="warning" />
            </div>

            <div className="grid grid-cols-1 gap-6 xl:grid-cols-[1fr_360px]">
              <section className="soft-surface border rounded-xl p-5">
                <div className="mb-5 flex items-center justify-between">
                  <div>
                    <h2 className="text-sm font-semibold text-foreground">Öğrenme yolları</h2>
                    <p className="mt-1 text-xs soft-text-muted">Aktif konular ve ilerleme.</p>
                  </div>
                  <button
                    onClick={() => onViewChange("chat")}
                    className="flex items-center gap-1.5 rounded-lg px-3 py-2 text-xs font-medium soft-text-muted hover:bg-surface-muted hover:text-foreground"
                  >
                    Sohbete dön
                    <ArrowRight className="h-3.5 w-3.5" />
                  </button>
                </div>

                {activeTopics.length === 0 ? (
                  <div className="rounded-lg border border-dashed soft-border px-4 py-10 text-center text-sm soft-text-muted">
                    Henüz aktif bir öğrenme yolu yok.
                  </div>
                ) : (
                  <div className="space-y-3">
                    {activeTopics.map((topic) => {
                      const pct = topic.totalSections
                        ? Math.round(((topic.completedSections || 0) / topic.totalSections) * 100)
                        : Math.round(topic.progressPercentage || 0);

                      return (
                        <button
                          key={topic.id}
                          onClick={() => onViewChange("chat")}
                          className="w-full rounded-lg border soft-border p-4 text-left transition-colors hover:bg-surface-muted"
                        >
                          <div className="flex items-start justify-between gap-4">
                            <div className="min-w-0">
                              <p className="truncate text-sm font-medium text-foreground">{topic.title}</p>
                              <p className="mt-1 text-xs soft-text-muted">{topic.category || "Genel"}</p>
                            </div>
                            <span className="text-xs font-semibold text-emerald-700 dark:text-emerald-300">%{pct}</span>
                          </div>
                          <div className="mt-3 h-1.5 overflow-hidden rounded-full soft-muted">
                            <div className="h-full rounded-full bg-emerald-500" style={{ width: `${pct}%` }} />
                          </div>
                        </button>
                      );
                    })}
                  </div>
                )}
              </section>

              <aside className="space-y-6">
                <section className="soft-surface border rounded-xl p-5">
                  <div className="mb-4 flex items-center gap-2">
                    <Award className="h-4 w-4 text-amber-700 dark:text-amber-300" />
                    <h2 className="text-sm font-semibold text-foreground">Günlük görevler</h2>
                  </div>

                  {quests.length === 0 ? (
                    <p className="text-sm soft-text-muted">Görevler yükleniyor.</p>
                  ) : (
                    <div className="space-y-3">
                      {quests.map((quest) => (
                        <div key={quest.id} className="flex items-center gap-3 rounded-lg soft-muted p-3">
                          <div
                            className={`flex h-8 w-8 items-center justify-center rounded-lg ${
                              quest.isCompleted
                                ? "bg-emerald-500/10 text-emerald-700 dark:text-emerald-300"
                                : "bg-amber-500/10 text-amber-700 dark:text-amber-300"
                            }`}
                          >
                            {quest.isCompleted ? <CheckCircle2 className="h-4 w-4" /> : <Activity className="h-4 w-4" />}
                          </div>
                          <div className="min-w-0 flex-1">
                            <p className="truncate text-sm font-medium text-foreground">{quest.title}</p>
                            <p className="text-xs soft-text-muted">+{quest.xpReward} XP</p>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </section>

                <section className="soft-surface border rounded-xl p-5">
                  <h2 className="text-sm font-semibold text-foreground">Hızlı erişim</h2>
                  <div className="mt-4 grid gap-2">
                    <button
                      onClick={() => onViewChange("chat")}
                      className="flex items-center justify-between rounded-lg px-3 py-3 text-left hover:bg-surface-muted"
                    >
                      <span className="text-sm font-medium text-foreground">Öğrenmeye devam</span>
                      <ArrowRight className="h-4 w-4 soft-text-muted" />
                    </button>
                    <button
                      onClick={() => onViewChange("wiki")}
                      className="flex items-center justify-between rounded-lg px-3 py-3 text-left hover:bg-surface-muted"
                    >
                      <span className="text-sm font-medium text-foreground">Wiki</span>
                      <ArrowRight className="h-4 w-4 soft-text-muted" />
                    </button>
                  </div>
                </section>
              </aside>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
