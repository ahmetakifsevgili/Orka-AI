/*
 * Müfredatlarım — Kullanıcının kendi oluşturduğu konu listesi.
 * Statik courseData tamamen kaldırıldı; TopicsAPI.getAll() kullanılıyor.
 * Konu adı arama + kategori filtresi desteklenir.
 * Premium dark zinc design.
 */

import { useState, useEffect, useMemo } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { useLocation } from "wouter";
import {
  BookOpen,
  Search,
  ChevronLeft,
  MessageSquare,
  Plus,
  Loader2,
  BarChart3,
  Inbox,
  ChevronDown,
  CheckCircle2,
  Circle,
  FileText,
  Code,
  HelpCircle,
  Clock,
  Layers,
  Users,
  Star,
} from "lucide-react";
import { TopicsAPI } from "@/services/api";
import type { ApiTopic } from "@/lib/types";
import OrcaLogo from "@/components/OrcaLogo";
import WikiDrawer from "@/components/WikiDrawer";

const PHASE_LABELS: Record<number, string> = {
  0: "Keşif",
  1: "Planlama",
  2: "Öğrenme",
  3: "Tamamlandı",
};

const PHASE_COLORS: Record<number, string> = {
  0: "text-sky-400 bg-sky-400/10 border-sky-400/20",
  1: "text-violet-400 bg-violet-400/10 border-violet-400/20",
  2: "text-amber-400 bg-amber-400/10 border-amber-400/20",
  3: "text-emerald-400 bg-emerald-400/10 border-emerald-400/20",
};

export default function Courses() {
  const [, navigate] = useLocation();
  const [topics, setTopics] = useState<ApiTopic[]>([]);
  const [loading, setLoading] = useState(true);
  const [searchQuery, setSearchQuery] = useState("");

  useEffect(() => {
    TopicsAPI.getAll()
      .then((r) => setTopics(r.data ?? []))
      .catch((err: unknown) => {
        console.error("[Courses] TopicsAPI.getAll failed:", err);
      })
      .finally(() => setLoading(false));
  }, []);

  const filtered = useMemo(() => {
    if (!searchQuery.trim()) return topics;
    const q = searchQuery.toLowerCase();
    return topics.filter(
      (t) =>
        t.title.toLowerCase().includes(q) ||
        (t.category ?? "").toLowerCase().includes(q)
    );
  }, [topics, searchQuery]);

  const [selectedCourse, setSelectedCourse] = useState<ApiTopic | null>(null);
  const [expandedModules, setExpandedModules] = useState<Record<string, boolean>>({"1": true});
  const [wikiTopicId, setWikiTopicId] = useState<string | null>(null);

  // Mocking the detailed modules from Screenshot 4
  const MOCK_MODULES = [
    { id: "1", title: "Giriş ve Kurulum", desc: "Python'ı tanıyın, geliştirme ortamınızı kurun.", progress: 4, total: 4, duration: "1.5 saat", completed: true,
      lessons: [
        { id: "l1", title: "Python Nedir?", type: "Makale", duration: "10 dk", completed: true },
        { id: "l2", title: "Kurulum ve IDE Seçimi", type: "Makale", duration: "15 dk", completed: true },
        { id: "l3", title: "İlk Programınız", type: "Alıştırma", duration: "20 dk", completed: true },
        { id: "l4", title: "Modül Sınavı", type: "Sınav", duration: "10 dk", completed: true }
      ]
    },
    { id: "2", title: "Değişkenler ve Veri Tipleri", desc: "Temel veri tipleri, tip dönüşümleri ve string işlemleri.", progress: 5, total: 5, duration: "2 saat", completed: true, lessons: [] },
    { id: "3", title: "Kontrol Akışı", desc: "If/else, döngüler, koşullu ifadeler ve hata yönetimi.", progress: 2, total: 5, duration: "2.5 saat", completed: false, lessons: [] },
    { id: "4", title: "Fonksiyonlar", desc: "Fonksiyon tanımlama, parametreler, lambda ve dekoratörler.", progress: 0, total: 5, duration: "3 saat", completed: false, lessons: [] },
    { id: "5", title: "Nesne Yönelimli Programlama", desc: "Sınıflar, kalıtım, polimorfizm ve soyut sınıflar.", progress: 0, total: 5, duration: "3 saat", completed: false, lessons: [] }
  ];

  const toggleModule = (id: string) => {
    setExpandedModules(prev => ({ ...prev, [id]: !prev[id] }));
  };

  const handleLessonClick = (lesson: any, topicId: string) => {
    if (lesson.completed) {
      setWikiTopicId(topicId);
    }
  };

  if (selectedCourse) {
     return (
        <div className="min-h-screen bg-zinc-950 relative">
          {wikiTopicId && (
            <div className="fixed inset-y-0 right-0 z-50 flex shadow-2xl border-l border-zinc-800">
               <WikiDrawer topicId={wikiTopicId} onClose={() => setWikiTopicId(null)} />
            </div>
          )}
          
          <div className="border-b border-zinc-800/50">
            <div className="max-w-5xl mx-auto px-6 py-4 flex items-center gap-4">
              <button
                onClick={() => setSelectedCourse(null)}
                className="flex items-center gap-1.5 text-zinc-500 hover:text-zinc-300 transition-colors text-sm"
              >
                <ChevronLeft className="w-4 h-4" />
                Kurslar
              </button>
              <div className="h-4 w-px bg-zinc-800" />
              <div className="flex items-center gap-2 text-sm text-zinc-400">
                <span>Programlama</span>
              </div>
            </div>
          </div>

          <div className="max-w-3xl mx-auto px-6 py-8">
             <div className="bg-zinc-900/40 border border-zinc-800/50 rounded-xl p-6 mb-8 mt-2">
                <div className="flex items-start gap-4 mb-4">
                   <span className="text-4xl">{selectedCourse.emoji || "🐍"}</span>
                   <div className="flex-1">
                      <div className="flex items-center gap-3 mb-1">
                         <h1 className="text-2xl font-bold text-zinc-100">{selectedCourse.title}</h1>
                         <span className="px-2 py-0.5 rounded bg-emerald-500/10 text-emerald-500 border border-emerald-500/20 text-[10px] font-medium uppercase">Başlangıç</span>
                      </div>
                      <p className="text-sm text-zinc-400 mb-4">{selectedCourse.category || "Sıfırdan Python programlama. Değişkenler, veri tipleri, kontrol akışı."}</p>
                      <div className="flex items-center gap-4 text-xs text-zinc-500">
                         <span className="flex items-center gap-1.5"><Clock className="w-3.5 h-3.5" /> 12 saat</span>
                         <span className="flex items-center gap-1.5"><Layers className="w-3.5 h-3.5" /> 5 Modül</span>
                         <span className="flex items-center gap-1.5"><Users className="w-3.5 h-3.5" /> 2340 Öğrenci</span>
                         <span className="flex items-center gap-1.5 text-amber-500"><Star className="w-3.5 h-3.5 fill-current" /> 4.8</span>
                      </div>
                   </div>
                </div>

                <div className="space-y-2 mt-6">
                   <div className="flex items-center justify-between text-xs font-medium text-zinc-400">
                      <span>Genel İlerleme</span>
                      <span>46%</span>
                   </div>
                   <div className="w-full h-1.5 bg-zinc-800 rounded-full overflow-hidden">
                      <div className="h-full bg-zinc-400 rounded-full" style={{ width: "46%" }} />
                   </div>
                   <div className="flex gap-2 mt-4 pt-2">
                      <span className="px-2.5 py-1 rounded bg-zinc-800/80 text-zinc-400 text-[11px]">Python</span>
                      <span className="px-2.5 py-1 rounded bg-zinc-800/80 text-zinc-400 text-[11px]">Temel</span>
                      <span className="px-2.5 py-1 rounded bg-zinc-800/80 text-zinc-400 text-[11px]">OOP</span>
                   </div>
                </div>
             </div>

             <h3 className="text-xs font-semibold text-zinc-500 uppercase tracking-wider mb-4">MÜFREDAT</h3>
             <div className="space-y-3">
                {MOCK_MODULES.map((mod) => (
                  <div key={mod.id} className="bg-zinc-900/30 border border-zinc-800/50 rounded-xl overflow-hidden">
                     <button 
                       onClick={() => toggleModule(mod.id)}
                       className="w-full flex items-center gap-4 p-4 hover:bg-zinc-900/60 transition-colors text-left"
                     >
                        <div className="w-8 h-8 rounded bg-zinc-800 flex items-center justify-center text-sm text-zinc-400 font-medium flex-shrink-0">
                           {mod.id}
                        </div>
                        <div className="flex-1 min-w-0">
                           <div className="flex items-center gap-2 mb-1">
                              <h4 className="font-medium text-zinc-200 truncate">{mod.title}</h4>
                              {mod.completed && <CheckCircle2 className="w-4 h-4 text-emerald-500" />}
                           </div>
                           <p className="text-xs text-zinc-500 truncate">{mod.desc}</p>
                        </div>
                        <div className="flex items-center gap-6 ml-4">
                           <div className="flex items-center gap-2">
                             <span className="text-[11px] text-zinc-400">{mod.progress}/{mod.total}</span>
                             <div className="w-16 h-1 bg-zinc-800 rounded-full overflow-hidden flex-shrink-0">
                               <div className={`h-full rounded-full ${mod.completed ? 'bg-emerald-500' : 'bg-zinc-500'}`} style={{ width: `${(mod.progress/mod.total)*100}%` }} />
                             </div>
                           </div>
                           <span className="text-xs text-zinc-400">{mod.duration}</span>
                           <ChevronDown className={`w-4 h-4 text-zinc-600 transition-transform ${expandedModules[mod.id] ? 'rotate-180' : ''}`} />
                        </div>
                     </button>
                     
                     {expandedModules[mod.id] && mod.lessons.length > 0 && (
                        <div className="border-t border-zinc-800/50 bg-zinc-950/50 px-4 py-2">
                           {mod.lessons.map(lesson => (
                              <button 
                                key={lesson.id} 
                                onClick={() => handleLessonClick(lesson, selectedCourse.id)}
                                className="w-full flex items-center justify-between py-2.5 group focus:outline-none"
                              >
                                 <div className="flex items-center gap-3">
                                    {lesson.completed ? (
                                      <CheckCircle2 className="w-3.5 h-3.5 text-emerald-500 flex-shrink-0" />
                                    ) : (
                                      <Circle className="w-3.5 h-3.5 text-zinc-700 flex-shrink-0" />
                                    )}
                                    <div className="flex items-center gap-2 text-sm text-zinc-400 group-hover:text-zinc-200 transition-colors">
                                       {lesson.type === "Makale" && <FileText className="w-3.5 h-3.5 opacity-60" />}
                                       {lesson.type === "Alıştırma" && <Code className="w-3.5 h-3.5 opacity-60" />}
                                       {lesson.type === "Sınav" && <HelpCircle className="w-3.5 h-3.5 opacity-60" />}
                                       <span>{lesson.title}</span>
                                    </div>
                                 </div>
                                 <div className="flex items-center gap-3 text-xs text-zinc-600">
                                    <span>{lesson.type}</span>
                                    <span>{lesson.duration}</span>
                                 </div>
                              </button>
                           ))}
                        </div>
                     )}
                  </div>
                ))}
             </div>
          </div>
        </div>
     );
  }

  // Özet istatistikler
  const totalTopics = topics.length;
  const completedTopics = topics.filter((t) => t.currentPhase === 3).length; // 3 = Completed
  const learningTopics = topics.filter((t) => t.currentPhase === 2).length;  // 2 = Learning
  const avgProgress =
    totalTopics > 0
      ? Math.round(topics.reduce((s, t) => s + (t.progressPercentage ?? 0), 0) / totalTopics)
      : 0;

  return (
    <div className="min-h-screen bg-zinc-950">
      {/* Header */}
      <div className="border-b border-zinc-800/50">
        <div className="max-w-5xl mx-auto px-6 py-4 flex items-center gap-4">
          <button
            onClick={() => navigate("/app")}
            className="flex items-center gap-1.5 text-zinc-500 hover:text-zinc-300 transition-colors text-sm"
          >
            <ChevronLeft className="w-4 h-4" />
            Geri
          </button>
          <div className="h-4 w-px bg-zinc-800" />
          <div className="flex items-center gap-2">
            <OrcaLogo className="w-4 h-4 text-zinc-500" />
            <span className="text-sm text-zinc-400 font-medium">Müfredatlarım</span>
          </div>
        </div>
      </div>

      <div className="max-w-5xl mx-auto px-6 py-8">
        {/* İstatistik kartları */}
        {!loading && totalTopics > 0 && (
          <motion.div
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.3 }}
            className="grid grid-cols-3 gap-4 mb-8"
          >
            <div className="bg-zinc-900/40 border border-zinc-800/50 rounded-xl p-4">
              <div className="flex items-center gap-2 mb-1">
                <BookOpen className="w-4 h-4 text-zinc-500" />
                <span className="text-xs text-zinc-500">Toplam Konu</span>
              </div>
              <p className="text-2xl font-semibold text-zinc-100">{totalTopics}</p>
            </div>
            <div className="bg-zinc-900/40 border border-zinc-800/50 rounded-xl p-4">
              <div className="flex items-center gap-2 mb-1">
                <BarChart3 className="w-4 h-4 text-zinc-500" />
                <span className="text-xs text-zinc-500">Ort. İlerleme</span>
              </div>
              <p className="text-2xl font-semibold text-zinc-100">%{avgProgress}</p>
            </div>
            <div className="bg-zinc-900/40 border border-zinc-800/50 rounded-xl p-4">
              <div className="flex items-center gap-2 mb-1">
                <MessageSquare className="w-4 h-4 text-zinc-500" />
                <span className="text-xs text-zinc-500">Aktif / Tamamlanan</span>
              </div>
              <p className="text-2xl font-semibold text-zinc-100">
                {learningTopics} / {completedTopics}
              </p>
            </div>
          </motion.div>
        )}

        {/* Arama ve yeni konu */}
        <div className="flex flex-col sm:flex-row gap-3 mb-6">
          <div className="flex gap-2 flex-wrap mb-2 sm:mb-0">
             <button className="px-3 py-1.5 rounded-full bg-zinc-800 text-zinc-200 text-xs font-medium">Tümü</button>
             <button className="px-3 py-1.5 rounded-full border border-zinc-800 text-zinc-400 hover:text-zinc-200 hover:border-zinc-700 text-xs transition-colors">Programlama</button>
             <button className="px-3 py-1.5 rounded-full border border-zinc-800 text-zinc-400 hover:text-zinc-200 hover:border-zinc-700 text-xs transition-colors">Yapay Zeka</button>
          </div>
          <div className="relative flex-1">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-zinc-600" />
            <input
              type="text"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="Konu ara..."
              className="w-full pl-9 pr-4 py-2 bg-zinc-900/50 border border-zinc-800 rounded-lg text-sm text-zinc-200 placeholder-zinc-600 focus:outline-none focus:border-zinc-700 transition-colors"
            />
          </div>
        </div>

        {/* İçerik */}
        {loading ? (
          <div className="flex items-center justify-center py-24">
            <Loader2 className="w-5 h-5 text-zinc-600 animate-spin" />
          </div>
        ) : totalTopics === 0 ? (
          /* Boş state */
          <motion.div
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.3 }}
            className="flex flex-col items-center justify-center py-24 text-center"
          >
            <div className="w-14 h-14 rounded-xl bg-zinc-900 border border-zinc-800 flex items-center justify-center mb-5">
              <Inbox className="w-6 h-6 text-zinc-600" />
            </div>
            <h2 className="text-base font-medium text-zinc-300 mb-2">
              Henüz müfredat yok
            </h2>
            <p className="text-sm text-zinc-600 mb-6 max-w-xs leading-relaxed">
              Sohbet ekranında{" "}
              <code className="bg-zinc-900 px-1 rounded text-xs">/plan</code>{" "}
              yazarak veya yeni konu oluşturarak müfredatını başlat.
            </p>
            <button
              onClick={() => navigate("/app")}
              className="flex items-center gap-2 px-5 py-2.5 bg-zinc-800 hover:bg-zinc-700 text-zinc-200 rounded-lg text-sm font-medium transition-colors"
            >
              <MessageSquare className="w-4 h-4" />
              Sohbete Git
            </button>
          </motion.div>
        ) : (
          /* Konu kartları */
          <AnimatePresence mode="popLayout">
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
              {filtered.map((topic, i) => (
                <motion.div
                  key={topic.id}
                  layout
                  initial={{ opacity: 0, y: 10 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, scale: 0.96 }}
                  transition={{ duration: 0.2, delay: i * 0.04 }}
                >
                  <button
                    onClick={() => setSelectedCourse(topic)}
                    className="w-full text-left bg-zinc-900/40 border border-zinc-800/50 rounded-xl p-5 hover:border-zinc-700 hover:bg-zinc-900/60 transition-all duration-150 group"
                  >
                    {/* Emoji + Phase badge */}
                    <div className="flex items-start justify-between mb-3">
                      <span className="text-2xl">
                        {topic.emoji || "📚"}
                      </span>
                      {topic.currentPhase != null && (
                        <span
                          className={`px-2 py-0.5 rounded-md text-[10px] font-medium border ${
                            PHASE_COLORS[topic.currentPhase] ?? 'text-zinc-400 bg-zinc-800 border-zinc-700'
                          }`}
                        >
                          {PHASE_LABELS[topic.currentPhase] ?? "Plan"}
                        </span>
                      )}
                    </div>

                    {/* Başlık */}
                    <h3 className="text-sm font-medium text-zinc-200 mb-1 group-hover:text-zinc-100 transition-colors line-clamp-2">
                      {topic.title}
                    </h3>

                    {/* Kategori */}
                    {topic.category && (
                      <p className="text-xs text-zinc-600 mb-3">{topic.category}</p>
                    )}

                    {/* Progress bar */}
                    <div className="space-y-1.5 mt-3">
                      <div className="flex items-center justify-between text-[10px] text-zinc-600">
                        <span>
                          {topic.completedSections ?? 0}/{topic.totalSections ?? 5} bölüm
                        </span>
                        <span>{Math.round(topic.progressPercentage ?? 0)}%</span>
                      </div>
                      <div className="w-full h-1.5 bg-zinc-800 rounded-full overflow-hidden">
                        <div
                          className="h-full bg-zinc-500 rounded-full transition-all duration-300"
                          style={{ width: `${Math.round(topic.progressPercentage ?? 0)}%` }}
                        />
                      </div>
                    </div>

                    {/* CTA */}
                    <div className="flex items-center justify-between mt-4">
                       <span className="text-xs text-zinc-500 flex items-center gap-1.5"><Clock className="w-3 h-3" /> 12s</span>
                       <div className="flex items-center gap-1 text-xs text-emerald-500 transition-colors bg-emerald-500/10 px-2 py-1 rounded">
                         <span>Devam Et</span>
                       </div>
                    </div>
                  </button>
                </motion.div>
              ))}

              {/* Arama sonucu boş */}
              {filtered.length === 0 && searchQuery && (
                <motion.div
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                  className="col-span-full text-center py-12"
                >
                  <p className="text-sm text-zinc-500">
                    "{searchQuery}" için sonuç bulunamadı.
                  </p>
                </motion.div>
              )}
            </div>
          </AnimatePresence>
        )}
      </div>
    </div>
  );
}
