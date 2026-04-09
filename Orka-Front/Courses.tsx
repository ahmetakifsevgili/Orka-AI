/*
 * Courses Catalog Page
 * Grid layout with course cards, progress indicators, category filters.
 * Premium dark zinc design.
 */

import { useState, useMemo } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { useLocation } from "wouter";
import {
  BookOpen,
  Clock,
  Users,
  Star,
  ArrowRight,
  Search,
  GraduationCap,
  BarChart3,
  ChevronLeft,
} from "lucide-react";
import { courses } from "@/lib/courseData";
import type { CourseCategory, CourseLevel } from "@/lib/types";
import OrcaLogo from "@/components/OrcaLogo";

const categories: (CourseCategory | "Tümü")[] = [
  "Tümü",
  "Programlama",
  "Yapay Zeka",
  "Veri Bilimi",
  "Web Geliştirme",
  "Veritabanı",
];

const levelColors: Record<CourseLevel, string> = {
  Başlangıç: "text-emerald-400 bg-emerald-400/10 border-emerald-400/20",
  Orta: "text-amber-400 bg-amber-400/10 border-amber-400/20",
  İleri: "text-red-400 bg-red-400/10 border-red-400/20",
};

export default function Courses() {
  const [, navigate] = useLocation();
  const [selectedCategory, setSelectedCategory] = useState<CourseCategory | "Tümü">("Tümü");
  const [searchQuery, setSearchQuery] = useState("");

  const filteredCourses = useMemo(() => {
    return courses.filter((course) => {
      const matchesCategory =
        selectedCategory === "Tümü" || course.category === selectedCategory;
      const matchesSearch =
        !searchQuery ||
        course.title.toLowerCase().includes(searchQuery.toLowerCase()) ||
        course.description.toLowerCase().includes(searchQuery.toLowerCase()) ||
        course.tags.some((t) =>
          t.toLowerCase().includes(searchQuery.toLowerCase())
        );
      return matchesCategory && matchesSearch;
    });
  }, [selectedCategory, searchQuery]);

  const enrolledCourses = courses.filter((c) => c.enrolled);
  const totalProgress =
    enrolledCourses.length > 0
      ? Math.round(
          enrolledCourses.reduce((sum, c) => sum + c.progress, 0) /
            enrolledCourses.length
        )
      : 0;

  return (
    <div className="min-h-screen bg-zinc-950">
      {/* Header */}
      <div className="border-b border-zinc-800/50">
        <div className="max-w-6xl mx-auto px-6 py-4 flex items-center gap-4">
          <button
            onClick={() => navigate("/app")}
            className="flex items-center gap-1.5 text-zinc-500 hover:text-zinc-300 transition-colors text-sm"
          >
            <ChevronLeft className="w-4 h-4" />
            Geri
          </button>
          <div className="h-4 w-px bg-zinc-800" />
          <div className="flex items-center gap-2">
            <OrcaLogo className="w-5 h-5 text-zinc-400" />
            <span className="text-sm font-medium text-zinc-300">Kurslar</span>
          </div>
        </div>
      </div>

      <div className="max-w-6xl mx-auto px-6 py-8">
        {/* Stats Row */}
        <div className="grid grid-cols-3 gap-4 mb-8">
          <div className="bg-zinc-900/50 border border-zinc-800/50 rounded-xl p-4">
            <div className="flex items-center gap-3">
              <div className="w-9 h-9 rounded-lg bg-zinc-800 flex items-center justify-center">
                <BookOpen className="w-4 h-4 text-zinc-400" />
              </div>
              <div>
                <p className="text-xl font-semibold text-zinc-100">
                  {enrolledCourses.length}
                </p>
                <p className="text-xs text-zinc-500">Kayıtlı Kurs</p>
              </div>
            </div>
          </div>
          <div className="bg-zinc-900/50 border border-zinc-800/50 rounded-xl p-4">
            <div className="flex items-center gap-3">
              <div className="w-9 h-9 rounded-lg bg-zinc-800 flex items-center justify-center">
                <BarChart3 className="w-4 h-4 text-zinc-400" />
              </div>
              <div>
                <p className="text-xl font-semibold text-zinc-100">
                  %{totalProgress}
                </p>
                <p className="text-xs text-zinc-500">Ortalama İlerleme</p>
              </div>
            </div>
          </div>
          <div className="bg-zinc-900/50 border border-zinc-800/50 rounded-xl p-4">
            <div className="flex items-center gap-3">
              <div className="w-9 h-9 rounded-lg bg-zinc-800 flex items-center justify-center">
                <GraduationCap className="w-4 h-4 text-zinc-400" />
              </div>
              <div>
                <p className="text-xl font-semibold text-zinc-100">
                  {courses.length}
                </p>
                <p className="text-xs text-zinc-500">Toplam Kurs</p>
              </div>
            </div>
          </div>
        </div>

        {/* Search + Filters */}
        <div className="flex items-center gap-4 mb-6">
          <div className="relative flex-1 max-w-sm">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-zinc-500" />
            <input
              type="text"
              placeholder="Kurs ara..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="w-full bg-zinc-900/50 border border-zinc-800 rounded-lg pl-9 pr-4 py-2 text-sm text-zinc-200 placeholder:text-zinc-600 focus:outline-none focus:border-zinc-700 transition-colors"
            />
          </div>
          <div className="flex items-center gap-1.5">
            {categories.map((cat) => (
              <button
                key={cat}
                onClick={() => setSelectedCategory(cat)}
                className={`px-3 py-1.5 rounded-lg text-xs font-medium transition-all duration-150 ${
                  selectedCategory === cat
                    ? "bg-zinc-800 text-zinc-100 border border-zinc-700"
                    : "text-zinc-500 hover:text-zinc-300 border border-transparent hover:border-zinc-800"
                }`}
              >
                {cat}
              </button>
            ))}
          </div>
        </div>

        {/* Course Grid */}
        <AnimatePresence mode="popLayout">
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {filteredCourses.map((course, i) => (
              <motion.div
                key={course.id}
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -10 }}
                transition={{ duration: 0.2, delay: i * 0.05 }}
                onClick={() => navigate(`/courses/${course.id}`)}
                className="group bg-zinc-900/30 border border-zinc-800/50 rounded-xl p-5 cursor-pointer hover:border-zinc-700/80 hover:bg-zinc-900/50 transition-all duration-200"
              >
                {/* Top row: icon + level badge */}
                <div className="flex items-start justify-between mb-3">
                  <span className="text-2xl">{course.icon}</span>
                  <span
                    className={`text-[10px] font-medium px-2 py-0.5 rounded-full border ${levelColors[course.level]}`}
                  >
                    {course.level}
                  </span>
                </div>

                {/* Title */}
                <h3 className="text-sm font-semibold text-zinc-100 mb-1.5 group-hover:text-white transition-colors">
                  {course.title}
                </h3>

                {/* Description */}
                <p className="text-xs text-zinc-500 leading-relaxed mb-4 line-clamp-2">
                  {course.description}
                </p>

                {/* Tags */}
                <div className="flex flex-wrap gap-1.5 mb-4">
                  {course.tags.map((tag) => (
                    <span
                      key={tag}
                      className="text-[10px] text-zinc-500 bg-zinc-800/50 px-2 py-0.5 rounded"
                    >
                      {tag}
                    </span>
                  ))}
                </div>

                {/* Progress bar (only for enrolled) */}
                {course.enrolled && (
                  <div className="mb-4">
                    <div className="flex items-center justify-between mb-1.5">
                      <span className="text-[10px] text-zinc-500">İlerleme</span>
                      <span className="text-[10px] font-medium text-zinc-400">
                        %{course.progress}
                      </span>
                    </div>
                    <div className="h-1.5 bg-zinc-800 rounded-full overflow-hidden">
                      <motion.div
                        initial={{ width: 0 }}
                        animate={{ width: `${course.progress}%` }}
                        transition={{ duration: 0.8, delay: i * 0.1, ease: "easeOut" }}
                        className="h-full bg-zinc-400 rounded-full"
                      />
                    </div>
                  </div>
                )}

                {/* Bottom meta */}
                <div className="flex items-center justify-between pt-3 border-t border-zinc-800/50">
                  <div className="flex items-center gap-3 text-[10px] text-zinc-600">
                    <span className="flex items-center gap-1">
                      <Clock className="w-3 h-3" />
                      {course.estimatedHours} saat
                    </span>
                    <span className="flex items-center gap-1">
                      <BookOpen className="w-3 h-3" />
                      {course.totalLessons} ders
                    </span>
                    <span className="flex items-center gap-1">
                      <Users className="w-3 h-3" />
                      {course.students.toLocaleString("tr-TR")}
                    </span>
                  </div>
                  <div className="flex items-center gap-1 text-[10px] text-zinc-500">
                    <Star className="w-3 h-3 fill-zinc-500" />
                    {course.rating}
                  </div>
                </div>

                {/* CTA */}
                <div className="mt-4">
                  <div
                    className={`flex items-center justify-center gap-1.5 py-2 rounded-lg text-xs font-medium transition-all duration-150 ${
                      course.enrolled
                        ? "bg-zinc-800 text-zinc-300 group-hover:bg-zinc-700"
                        : "border border-zinc-700 text-zinc-400 group-hover:bg-zinc-800 group-hover:text-zinc-200"
                    }`}
                  >
                    {course.enrolled ? "Devam Et" : "Kursa Katıl"}
                    <ArrowRight className="w-3 h-3" />
                  </div>
                </div>
              </motion.div>
            ))}
          </div>
        </AnimatePresence>

        {filteredCourses.length === 0 && (
          <div className="text-center py-16">
            <Search className="w-8 h-8 text-zinc-700 mx-auto mb-3" />
            <p className="text-sm text-zinc-500">Aramanızla eşleşen kurs bulunamadı.</p>
          </div>
        )}
      </div>
    </div>
  );
}
