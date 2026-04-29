import { useState, useEffect } from "react";
import {
  BookOpen,
  Brain,
  Target,
  Flame,
  TrendingUp,
  ArrowRight,
  ChevronRight,
  Activity,
  Award,
  Cpu,
} from "lucide-react";
import { useQuizHistory } from "@/contexts/QuizHistoryContext";
import { QuizAPI, DashboardAPI, UserAPI, storage } from "@/services/api";
import type { ApiTopic, ApiGlobalStats, ApiDashboardStats, ApiGamification } from "@/lib/types";
import SystemHealthHUD from "@/components/SystemHealthHUD";

interface DashboardPanelProps {
  topics: ApiTopic[];
  onViewChange: (view: string) => void;
}

/** 
 * Custom Sparkline Component
 * UX Mandate: No heavy chart libs, premium SVG feel.
 */
function SuccessRateSparkline({ data }: { data: ApiGlobalStats['dailyProgress'] }) {
  if (!data || data.length < 2) return null;
  
  const width = 200;
  const height = 40;
  const padding = 5;
  
  const maxVal = 100;
  const minVal = 0;
  
  const points = data.map((d, i) => {
    const x = (i / (data.length - 1)) * (width - 2 * padding) + padding;
    const y = height - ((d.accuracy - minVal) / (maxVal - minVal)) * (height - 2 * padding) - padding;
    return `${x},${y}`;
  }).join(" ");

  return (
    <div className="relative group">
      <svg width={width} height={height} className="overflow-visible">
        {/* Shadow path for depth */}
        <polyline
          points={points}
          fill="none"
          stroke="rgba(16, 185, 129, 0.1)"
          strokeWidth="4"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
        {/* Main path */}
        <polyline
          points={points}
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
          className="text-emerald-500/80"
        />
        {/* End dot */}
        <circle 
          cx={(width - padding)} 
          cy={height - ((data[data.length-1].accuracy - minVal) / (maxVal - minVal)) * (height - 2 * padding) - padding}
          r="3"
          className="fill-emerald-400 stroke-emerald-950 stroke-2"
        />
      </svg>
    </div>
  );
}

export default function DashboardPanel({ topics, onViewChange }: DashboardPanelProps) {
  const { attempts: sessionAttempts } = useQuizHistory(); // For local feedback
  // HUD yalnızca admin hesaplarda görünür — LLMOps verisi operasyon sırrıdır.
  const isAdmin = storage.getUser()?.isAdmin === true;
  const [activeTab, setActiveTab] = useState<"karne" | "hud">("karne");
  const [stats, setStats] = useState<ApiGlobalStats | null>(null);
  const [dashStats, setDashStats] = useState<ApiDashboardStats | null>(null);
  const [gamification, setGamification] = useState<ApiGamification | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    // Quiz istatistikleri (doğruluk oranı, sparkline)
    QuizAPI.getGlobalStats()
      .then(res => setStats(res.data))
      .catch(err => console.error("Quiz stats fetch error:", err));

    // Gamification (seviye, xpToNextLevel, levelLabel)
    UserAPI.getGamification()
      .then(res => setGamification(res.data as ApiGamification))
      .catch(err => console.error("Gamification fetch error:", err));

    // Dashboard istatistikleri (XP, Streak, gerçek tamamlama verileri)
    DashboardAPI.getStats()
      .then(res => setDashStats(res.data as ApiDashboardStats))
      .catch(err => console.error("Dashboard stats fetch error:", err))
      .finally(() => setLoading(false));
  }, [sessionAttempts.length]); // Quiz tamamlandığında yenile

  const correctCount = stats?.correctAnswers ?? 0;
  const totalQuizzes = stats?.totalQuizzes ?? 0;
  const accuracy = stats?.accuracy ?? 0;

  const totalLessons = dashStats?.totalSections ?? topics.reduce(
    (sum, t) => sum + (t.totalSections ?? 0), 0
  );
  const completedLessons = dashStats?.completedSections ?? topics.reduce(
    (sum, t) => sum + (t.completedSections ?? 0), 0
  );

  // Gerçek değerler: DB'den gelen XP ve Streak
  const totalXP      = dashStats?.totalXP      ?? 0;
  const activeStreak = dashStats?.currentStreak ?? stats?.dailyProgress.filter(d => d.total > 0).length ?? 0;
  const learningSignalBook = dashStats?.learningSignalBook;
  const weakSkills = learningSignalBook?.weakSkills ?? [];
  const recentSignals = learningSignalBook?.recentSignals ?? [];

  return (
    <div className="flex-1 flex flex-col bg-transparent h-full overflow-hidden">

      {/* Tab Switcher */}
      <div className="flex-shrink-0 flex items-center gap-1 px-8 pt-6 pb-0">
        <button
          onClick={() => setActiveTab("karne")}
          className={`flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-bold transition-all ${
            activeTab === "karne"
              ? "bg-[#dcecf3]/85 text-[#172033] border border-[#9ec7d9]/45 shadow-sm"
              : "text-[#667085] hover:text-[#172033] border border-transparent"
          }`}
        >
          <Award className="w-3.5 h-3.5" />
          Öğrenme Karnesi
        </button>
        {isAdmin && (
          <button
            onClick={() => setActiveTab("hud")}
            className={`flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-bold transition-all ${
              activeTab === "hud"
                ? "bg-[#dcecf3]/85 text-[#172033] border border-[#9ec7d9]/45 shadow-sm"
                : "text-[#667085] hover:text-[#172033] border border-transparent"
            }`}
            title="Admin paneli — LLMOps İzleme"
          >
            <Cpu className="w-3.5 h-3.5" />
            Sistem Analitiği
            <span className="flex h-1.5 w-1.5 rounded-full bg-[#8fb7a2] animate-pulse" />
            <span className="ml-1 text-[9px] font-bold uppercase tracking-widest text-[#9a6b24]/80 border border-amber-500/30 bg-[#fff8ee] px-1.5 py-0.5 rounded">
              Admin
            </span>
          </button>
        )}
      </div>

      {/* Tab Content */}
      {activeTab === "hud" && isAdmin ? (
        <SystemHealthHUD />
      ) : (
      <div className="flex-1 overflow-y-auto">
        <div className="max-w-3xl mx-auto w-full px-8 py-10">
          
          {/* Header & Mastery Card */}
          <div className="flex items-center justify-between mb-10">
            <div>
              <h1 className="text-2xl font-bold text-[#172033] mb-1.5 tracking-tight">Öğrenme Karnesi</h1>
              <div className="flex items-center gap-2">
                <span className="flex h-2 w-2 rounded-full bg-[#8fb7a2] animate-pulse"></span>
                <p className="text-[11px] font-medium text-[#667085] uppercase tracking-widest">Sistem Analitiği Aktif</p>
              </div>
            </div>
            
            <div id="tour-global-stats" className="hidden sm:flex items-center gap-6 bg-[#f7f9fa]/68 border border-[#526d82]/14 backdrop-blur-xl px-6 py-4 rounded-2xl">
               <div className="text-right">
                  <p className="text-[10px] text-[#667085] uppercase font-bold tracking-tighter mb-0.5">Global Başarı</p>
                  <p className="text-xl font-mono font-bold text-[#47725d]">%{accuracy}</p>
               </div>
               {stats && <SuccessRateSparkline data={stats.dailyProgress} />}
            </div>
          </div>

          {/* Core Stats Grid */}
          <div className="grid grid-cols-2 lg:grid-cols-4 gap-4 mb-10">
            {/* Stat Item: XP */}
            <div className="p-5 rounded-2xl bg-[#f7f9fa]/70 border border-[#526d82]/14 backdrop-blur-xl hover:border-[#526d82]/18/50 transition-colors group">
              <div className="w-8 h-8 rounded-lg bg-[#dcecf3]/70 flex items-center justify-center mb-4 group-hover:bg-[#dcecf3]/70 transition-colors">
                <TrendingUp className="w-4 h-4 text-[#667085]" />
              </div>
              <p className="text-2xl font-bold text-[#172033]">{loading ? "—" : totalXP}</p>
              <p className="text-[11px] font-medium text-[#667085] uppercase mt-1">Toplam XP</p>
              {gamification && (
                <p className="text-[10px] text-[#98a2b3] mt-1">
                  {gamification.levelLabel} · Seviye {gamification.level}
                </p>
              )}
            </div>

            {/* Stat Item: Lessons */}
            <div className="p-5 rounded-2xl bg-[#f7f9fa]/70 border border-[#526d82]/14 backdrop-blur-xl hover:border-[#526d82]/18/50 transition-colors group">
              <div className="w-8 h-8 rounded-lg bg-[#dcecf3]/70 flex items-center justify-center mb-4 group-hover:bg-[#dcecf3]/70 transition-colors">
                <Brain className="w-4 h-4 text-[#667085]" />
              </div>
              <p className="text-2xl font-bold text-[#172033]">
                {loading ? "—" : (totalLessons > 0 ? `${completedLessons}/${totalLessons}` : topics.length)}
              </p>
              <p className="text-[11px] font-medium text-[#667085] uppercase mt-1">Tamamlanan Ders</p>
            </div>

            {/* Stat Item: Accuracy */}
            <div className="p-5 rounded-2xl bg-[#8fb7a2]/5 border border-emerald-500/10 hover:border-emerald-500/20 transition-colors group">
              <div className="w-8 h-8 rounded-lg bg-[#8fb7a2]/10 flex items-center justify-center mb-4">
                <Target className="w-4 h-4 text-[#47725d]/70" />
              </div>
              <p className="text-2xl font-bold text-[#47725d]">{loading ? "—" : `%${accuracy}`}</p>
              <p className="text-[11px] font-medium text-emerald-600/80 uppercase mt-1">Doğruluk Oranı</p>
            </div>

            {/* Stat Item: Streak (gerçek DB verisi) */}
            <div className="p-5 rounded-2xl bg-[#fff8ee]/85 border border-[#e8c46f]/28 hover:border-[#e8c46f]/45 transition-colors group">
              <div className="w-8 h-8 rounded-lg bg-[#fff8ee] flex items-center justify-center mb-4">
                <Flame className="w-4 h-4 text-[#9a6b24]" />
              </div>
              <p className="text-2xl font-bold text-[#9a6b24]">{loading ? "—" : activeStreak}</p>
              <p className="text-[11px] font-medium text-[#a8783d] uppercase mt-1">
                {activeStreak > 1 ? `${activeStreak} Günlük Seri` : "Öğrenme Serisi"}
              </p>
            </div>
          </div>

          <div className="mb-10 rounded-[1.75rem] border border-[#526d82]/12 bg-[#f7f9fa]/72 p-5 shadow-sm backdrop-blur-xl">
            <div className="mb-4 flex items-start justify-between gap-4">
              <div>
                <p className="text-[10px] font-black uppercase tracking-[0.18em] text-[#52768a]">
                  Öğrenci Sinyal Defteri
                </p>
                <h2 className="mt-1 text-base font-extrabold text-[#172033]">
                  {learningSignalBook?.summary || "Henüz belirgin zayıf beceri sinyali yok."}
                </h2>
              </div>
              <span className="rounded-full bg-[#dcecf3]/80 px-3 py-1 text-[10px] font-bold text-[#2d5870]">
                {learningSignalBook?.totalRecentAttempts ?? 0} son deneme
              </span>
            </div>

            <div className="grid gap-3 md:grid-cols-2">
              <div className="rounded-2xl bg-[#eef1f3]/70 p-4">
                <p className="mb-3 text-[11px] font-black uppercase tracking-[0.16em] text-[#667085]">
                  Zayıf beceriler
                </p>
                {weakSkills.length === 0 ? (
                  <p className="text-xs leading-6 text-[#667085]">
                    Quiz cevapları skill etiketiyle geldikçe burada kişisel telafi hedefleri oluşacak.
                  </p>
                ) : (
                  <div className="space-y-2">
                    {weakSkills.slice(0, 3).map((skill) => (
                      <div key={`${skill.skillTag}-${skill.topicPath}`} className="rounded-xl bg-[#f7f4ec]/78 px-3 py-2">
                        <div className="flex items-center justify-between gap-3">
                          <span className="text-xs font-bold text-[#172033]">{skill.skillTag || "unknown skill"}</span>
                          <span className="text-[10px] font-mono text-[#9a6b24]">%{Math.round(skill.accuracy)}</span>
                        </div>
                        <p className="mt-1 text-[11px] text-[#667085]">{skill.topicPath}</p>
                      </div>
                    ))}
                  </div>
                )}
              </div>

              <div className="rounded-2xl bg-[#fff8ee]/76 p-4">
                <p className="mb-3 text-[11px] font-black uppercase tracking-[0.16em] text-[#8a641f]">
                  Son öğrenme sinyalleri
                </p>
                {recentSignals.length === 0 ? (
                  <p className="text-xs leading-6 text-[#667085]">
                    “Anlamadım”, quiz cevabı, Wiki aksiyonu ve IDE çıktıları geldikçe ajan köprüsü burada görünür olur.
                  </p>
                ) : (
                  <div className="space-y-2">
                    {recentSignals.slice(0, 3).map((signal, index) => (
                      <div key={`${signal.signalType}-${index}`} className="flex items-center justify-between gap-3 rounded-xl bg-white/60 px-3 py-2">
                        <span className="text-xs font-semibold text-[#172033]">{signal.signalType}</span>
                        <span className="text-[10px] text-[#667085]">{signal.skillTag || signal.topicPath || "genel"}</span>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </div>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
            <div id="tour-course-progress" className="lg:col-span-2">
               <div className="flex items-center justify-between mb-6">
                <h2 className="text-sm font-bold text-[#172033] uppercase tracking-widest flex items-center gap-2">
                  <Activity className="w-4 h-4 text-[#667085]" />
                  Konu İlerlemesi
                </h2>
                <button
                  onClick={() => onViewChange("courses")}
                   className="text-[11px] font-bold text-[#667085] hover:text-[#344054] flex items-center gap-1 transition-colors uppercase tracking-wider"
                >
                  Tümünü Gör
                  <ChevronRight className="w-3 h-3" />
                </button>
              </div>

              {topics.length === 0 ? (
                <div className="py-16 text-center border border-dashed border-[#526d82]/15 rounded-3xl">
                  <p className="text-xs text-[#667085]">Henüz aktif bir öğrenme yolunuz bulunmuyor.</p>
                </div>
              ) : (
                <div className="space-y-4">
                  {topics.slice(0, 4).map((topic) => {
                    const pct = topic.totalSections ? Math.round((topic.completedSections || 0) / topic.totalSections * 100) : 0;
                    return (
                      <div
                        key={topic.id}
                        className="p-5 rounded-2xl bg-[#f7f9fa]/66 border border-[#526d82]/12 backdrop-blur-xl hover:bg-[#f7f4ec]/50 transition-all cursor-pointer group"
                      >
                        <div className="flex items-center justify-between mb-4">
                          <div className="flex items-center gap-3">
                            <div className="w-10 h-10 rounded-xl bg-[#dcecf3]/55 flex items-center justify-center text-lg shadow-inner">
                              {topic.emoji}
                            </div>
                            <div>
                              <p className="text-sm font-semibold text-[#172033] group-hover:text-white transition-colors">{topic.title}</p>
                              <p className="text-[10px] text-[#98a2b3] uppercase font-bold tracking-tighter">{topic.category || 'GENEL'}</p>
                            </div>
                          </div>
                          <div className="text-right">
                             <p className="text-sm font-mono font-bold text-[#667085]">%{pct}</p>
                          </div>
                        </div>
                        <div className="w-full h-1 bg-[#dcecf3]/55 rounded-full overflow-hidden">
                           <div 
                             className="h-full bg-zinc-600 rounded-full transition-all duration-1000 group-hover:bg-zinc-400"
                             style={{ width: `${pct}%` }}
                           />
                        </div>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>

            <div className="space-y-6">
              <h2 className="text-sm font-bold text-[#172033] uppercase tracking-widest flex items-center gap-2">
                <Award className="w-4 h-4 text-[#667085]" />
                Hızlı Erişim
              </h2>
              
              <div className="grid grid-cols-1 gap-3">
                <button
                  onClick={() => onViewChange("chat")}
                  className="p-5 rounded-2xl bg-[#f7f9fa]/66 border border-[#526d82]/12 backdrop-blur-xl hover:border-zinc-600/50 transition-all text-left flex items-center justify-between group"
                >
                  <div className="flex flex-col">
                    <span className="text-xs font-bold text-[#172033] group-hover:text-white transition-colors">Öğrenmeye Devam</span>
                    <span className="text-[10px] text-[#667085]">En son kaldığın ders</span>
                  </div>
                  <div className="w-8 h-8 rounded-full bg-[#dcecf3]/70 flex items-center justify-center group-hover:bg-zinc-700 transition-colors">
                    <ArrowRight className="w-4 h-4 text-[#667085]" />
                  </div>
                </button>

                <button
                  id="tour-wiki-access"
                  onClick={() => onViewChange("wiki")}
                  className="p-5 rounded-2xl bg-[#f7f9fa]/66 border border-[#526d82]/12 backdrop-blur-xl hover:border-zinc-600/50 transition-all text-left flex items-center justify-between group"
                >
                   <div className="flex flex-col">
                    <span className="text-xs font-bold text-[#172033]">Wiki Kütüphanesi</span>
                    <span className="text-[10px] text-[#667085]">Hafıza haritasını keşfet</span>
                  </div>
                  <div className="w-8 h-8 rounded-full bg-[#dcecf3]/70 flex items-center justify-center group-hover:bg-zinc-700 transition-colors">
                    <BookOpen className="w-4 h-4 text-[#667085]" />
                  </div>
                </button>
              </div>

              {/* Tips Section */}
              <div className="p-6 rounded-3xl bg-emerald-500/5 border border-emerald-500/10">
                 <h4 className="text-[10px] font-bold text-emerald-600 uppercase tracking-widest mb-2">Günlük İpucu</h4>
                 <p className="text-[11px] text-[#667085] leading-relaxed italic">
                   "Öğrenilenlerin %70'i ilk 24 saat içinde unutulur. Quizleri düzenli çözerek kalıcı hafızayı güçlendir."
                 </p>
              </div>
            </div>
          </div>
        </div>
      </div>
      )}
    </div>
  );
}
