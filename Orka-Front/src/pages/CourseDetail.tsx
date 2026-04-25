/**
 * Course Detail Page
 * Module accordion with lesson list, progress tracking, and course overview.
 * Soft neutral web design.
 */

import { useState, useMemo } from "react";
import { useParams, useLocation } from "wouter";
import { motion, AnimatePresence } from "framer-motion";
import {
  ChevronLeft,
  ChevronDown,
  Clock,
  BookOpen,
  Users,
  Star,
  CheckCircle2,
  Circle,
  Play,
  FileText,
  HelpCircle,
  Code2,
  BarChart3,
  GraduationCap,
} from "lucide-react";
import { courses } from "@/lib/courseData";
import type { CourseLesson, CourseLevel } from "@/lib/types";
import OrcaLogo from "@/components/OrcaLogo";
import WikiDrawer from "@/components/WikiDrawer";

const levelColors: Record<CourseLevel, string> = {
  Başlangıç: "text-emerald-600 bg-emerald-500/10 border-emerald-500/20",
  Orta: "text-amber-400 bg-amber-400/10 border-amber-400/20",
  İleri: "text-zinc-700 bg-zinc-500/10 border-zinc-500/20",
};

const lessonTypeIcons: Record<CourseLesson["type"], React.ReactNode> = {
  video: <Play className="w-3.5 h-3.5" />,
  article: <FileText className="w-3.5 h-3.5" />,
  quiz: <HelpCircle className="w-3.5 h-3.5" />,
  exercise: <Code2 className="w-3.5 h-3.5" />,
};

const lessonTypeLabels: Record<CourseLesson["type"], string> = {
  video: "Video",
  article: "Makale",
  quiz: "Sınav",
  exercise: "Alıştırma",
};

// Kurs wiki drawer'i: ders tiklandığında kursun kendi ID'si ile açılır.

export default function CourseDetail() {
  const params = useParams<{ id: string }>();
  const [, navigate] = useLocation();
  const [openModules, setOpenModules] = useState<Set<string>>(new Set());
  const [activeWikiTopicId, setActiveWikiTopicId] = useState<string | null>(null);

  const course = useMemo(
    () => courses.find((c) => c.id === params.id),
    [params.id]
  );

  if (!course) {
    return (
      <div className="min-h-screen bg-zinc-950 flex items-center justify-center">
        <div className="text-center">
          <p className="text-zinc-400">Kurs bulunamadı</p>
          <button
            onClick={() => navigate("/courses")}
            className="mt-4 text-zinc-300 hover:text-white transition-colors"
          >
            Kurslar sayfasına dön
          </button>
        </div>
      </div>
    );
  }

  const toggleModule = (moduleId: string) => {
    const newSet = new Set(openModules);
    if (newSet.has(moduleId)) {
      newSet.delete(moduleId);
    } else {
      newSet.add(moduleId);
    }
    setOpenModules(newSet);
  };

  const moduleProgress = course.modules.map((mod) => {
    const completedCount = mod.lessons.filter((l) => l.completed).length;
    const totalCount = mod.lessons.length;
    const percent = Math.round((completedCount / totalCount) * 100);
    return {
      ...mod,
      completedCount,
      totalCount,
      percent,
    };
  });

  const totalCompleted = course.modules.reduce(
    (sum, mod) => sum + mod.lessons.filter((l) => l.completed).length,
    0
  );

  const handleLessonClick = (lesson: CourseLesson) => {
    if (!lesson.completed) return;
    // Wiki drawer'ı kursun ID'si ile aç
    if (course) setActiveWikiTopicId(course.id);
  };

  return (
    <div className="min-h-screen bg-zinc-950">
      {/* Header */}
      <div className="border-b border-zinc-800/50">
        <div className="max-w-4xl mx-auto px-6 py-4 flex items-center gap-4">
          <button
            onClick={() => navigate("/courses")}
            className="flex items-center gap-1.5 text-zinc-500 hover:text-zinc-300 transition-colors text-sm"
          >
            <ChevronLeft className="w-4 h-4" />
            Kurslar
          </button>
          <div className="h-4 w-px bg-zinc-800" />
          <div className="flex items-center gap-2">
            <span className="text-sm text-zinc-500">{course.category}</span>
          </div>
        </div>
      </div>

      {/* Main Content */}
      <div className="max-w-4xl mx-auto px-6 py-8">
        {/* Course Header Card */}
        <div className="bg-zinc-900/30 border border-zinc-800/50 rounded-xl p-6 mb-8">
          <div className="flex items-start gap-4 mb-4">
            <div className="text-3xl">{course.icon}</div>
            <div className="flex-1">
              <div className="flex items-center gap-2 mb-2">
                <h1 className="text-2xl font-bold text-zinc-100">
                  {course.title}
                </h1>
                <span
                  className={`px-2 py-1 rounded-lg text-xs font-medium border ${levelColors[course.level]}`}
                >
                  {course.level}
                </span>
              </div>
              <p className="text-sm text-zinc-400 mb-3">{course.description}</p>
              <div className="flex items-center gap-4 text-xs text-zinc-500">
                <span className="flex items-center gap-1">
                  <Clock className="w-3.5 h-3.5" />
                  {course.estimatedHours} saat
                </span>
                <span className="flex items-center gap-1">
                  <BookOpen className="w-3.5 h-3.5" />
                  {course.modules.length} Modül
                </span>
                <span className="flex items-center gap-1">
                  <Users className="w-3.5 h-3.5" />
                  {course.students} Öğrenci
                </span>
                <span className="flex items-center gap-1">
                  <Star className="w-3.5 h-3.5 fill-amber-400 text-amber-400" />
                  {course.rating}
                </span>
              </div>
            </div>
          </div>

          {/* Progress */}
          <div className="space-y-2 mb-4">
            <div className="flex items-center justify-between text-xs">
              <span className="text-zinc-400">Genel İlerleme</span>
              <span className="text-zinc-300 font-medium">
                {Math.round((totalCompleted / course.modules.reduce((sum, m) => sum + m.lessons.length, 0)) * 100)}%
              </span>
            </div>
            <div className="w-full h-2 bg-zinc-800 rounded-full overflow-hidden relative">
              <div
                className="h-full bg-emerald-500 rounded-full transition-all duration-700 relative"
                style={{
                  width: `${Math.round((totalCompleted / course.modules.reduce((sum, m) => sum + m.lessons.length, 0)) * 100)}%`,
                }}
              >
                <div className="absolute inset-0 bg-emerald-400/20 animate-pulse" />
              </div>
            </div>
          </div>

          {/* Tags */}
          <div className="flex flex-wrap gap-2">
            {course.tags.map((tag) => (
              <span
                key={tag}
                className="px-2 py-1 bg-zinc-800/50 text-zinc-400 rounded text-xs"
              >
                {tag}
              </span>
            ))}
          </div>

          {/* CTA */}
          {!course.enrolled && (
            <div className="mt-6 pt-5 border-t border-zinc-800/50">
              <button className="w-full py-2.5 bg-zinc-100 text-zinc-900 rounded-lg text-sm font-medium hover:bg-white transition-colors flex items-center justify-center gap-2">
                <GraduationCap className="w-4 h-4" />
                Kursa Katıl
              </button>
            </div>
          )}
        </div>

        {/* Module Accordion */}
        <div className="space-y-2">
          <h2 className="text-xs font-medium text-zinc-500 uppercase tracking-wider mb-3 px-1">
            Müfredat
          </h2>
          {moduleProgress.map((mod, modIndex) => {
            const isOpen = openModules.has(mod.id);
            return (
              <div
                key={mod.id}
                className={`border rounded-xl overflow-hidden transition-all duration-300 ${
                  mod.percent === 100
                    ? "bg-zinc-900/40 border-zinc-700/40"
                    : mod.percent > 0
                      ? "bg-amber-500/[0.03] border-amber-500/10 hover:border-amber-500/20"
                      : "bg-zinc-900/30 border-zinc-800/50"
                }`}
              >
                {/* Module Header */}
                <button
                  onClick={() => toggleModule(mod.id)}
                  className="w-full flex items-center gap-4 p-4 hover:bg-zinc-900/50 transition-colors"
                >
                  {/* Module number */}
                  <div className="flex-shrink-0 w-8 h-8 rounded-lg bg-zinc-800 flex items-center justify-center">
                    <span className="text-xs font-semibold text-zinc-400">
                      {modIndex + 1}
                    </span>
                  </div>

                  {/* Module info */}
                  <div className="flex-1 min-w-0 text-left">
                    <div className="flex items-center gap-2">
                      <h3 className="text-sm font-medium text-zinc-200 truncate">
                        {mod.title}
                      </h3>
                      {mod.percent === 100 && (
                        <CheckCircle2 className="w-3.5 h-3.5 text-emerald-500 flex-shrink-0" />
                      )}
                    </div>
                    <p className="text-[11px] text-zinc-600 truncate">
                      {mod.description}
                    </p>
                  </div>

                  {/* Module progress */}
                  <div className="flex items-center gap-3 flex-shrink-0">
                    <div className="text-right">
                      <span className="text-[10px] text-zinc-500">
                        {mod.completedCount}/{mod.totalCount}
                      </span>
                    </div>
                    <div className="w-16 h-1.5 bg-zinc-800 rounded-full overflow-hidden">
                      <div
                        className={`h-full rounded-full transition-all duration-500 ${
                          mod.percent === 100
                            ? "bg-zinc-400"
                            : mod.percent > 0
                              ? "bg-amber-500"
                              : "bg-zinc-700"
                        }`}
                        style={{ width: `${mod.percent}%` }}
                      />
                    </div>
                    <span className="text-[10px] text-zinc-600">{mod.duration}</span>
                    <ChevronDown
                      className={`w-4 h-4 text-zinc-600 transition-transform duration-200 ${
                        isOpen ? "rotate-180" : ""
                      }`}
                    />
                  </div>
                </button>

                {/* Lesson List */}
                <AnimatePresence>
                  {isOpen && (
                    <motion.div
                      initial={{ opacity: 0, height: 0 }}
                      animate={{ opacity: 1, height: "auto" }}
                      exit={{ opacity: 0, height: 0 }}
                      transition={{ duration: 0.2 }}
                      className="overflow-hidden"
                    >
                      <div className="border-t border-zinc-800/50 px-4 py-2">
                        {mod.lessons.map((lesson) => (
                          <button
                            key={lesson.id}
                            onClick={() => handleLessonClick(lesson)}
                            disabled={!lesson.completed}
                            className={`flex items-center gap-3 py-2.5 px-2 rounded-lg transition-all duration-200 w-full text-left ${
                              lesson.completed
                                ? "hover:bg-zinc-800/40 hover:-translate-y-[1px] hover:shadow-sm cursor-pointer"
                                : "opacity-50 cursor-not-allowed"
                            }`}
                          >
                            {/* Completion indicator */}
                            {lesson.completed ? (
                              <CheckCircle2 className="w-4 h-4 text-emerald-500 flex-shrink-0" />
                            ) : (
                              <Circle className="w-4 h-4 text-zinc-700 flex-shrink-0" />
                            )}

                            {/* Lesson type icon */}
                            <div className="flex-shrink-0 w-3.5 h-3.5 text-zinc-600">
                              {lessonTypeIcons[lesson.type]}
                            </div>

                            {/* Lesson info */}
                            <div className="flex-1 min-w-0">
                              <p className="text-sm text-zinc-300 truncate">
                                {lesson.title}
                              </p>
                            </div>

                            {/* Lesson meta */}
                            <div className="flex items-center gap-2 flex-shrink-0">
                              <span className="text-[10px] text-zinc-600">
                                {lessonTypeLabels[lesson.type]}
                              </span>
                              <span className="text-[10px] text-zinc-600">
                                {lesson.duration}
                              </span>
                            </div>
                          </button>
                        ))}
                      </div>
                    </motion.div>
                  )}
                </AnimatePresence>
              </div>
            );
          })}
        </div>
      </div>

      {/* Wiki Drawer */}
      <AnimatePresence>
        {activeWikiTopicId && (
          <WikiDrawer
            topicId={activeWikiTopicId}
            onClose={() => setActiveWikiTopicId(null)}
          />
        )}
      </AnimatePresence>
    </div>
  );
}
