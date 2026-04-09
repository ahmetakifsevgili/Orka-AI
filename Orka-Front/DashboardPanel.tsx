/*
 * Design: Dashboard overview panel with learning stats,
 * recent activity, and quick actions.
 */

import {
  BookOpen,
  Brain,
  Target,
  Clock,
  TrendingUp,
  ArrowRight,
} from "lucide-react";
import { useQuizHistory } from "@/contexts/QuizHistoryContext";
import type { Topic } from "@/lib/types";

interface DashboardPanelProps {
  topics: Topic[];
  onViewChange: (view: string) => void;
}

export default function DashboardPanel({ topics, onViewChange }: DashboardPanelProps) {
  const { attempts } = useQuizHistory();
  const correctCount = attempts.filter((a) => a.isCorrect).length;
  const totalLessons = topics.reduce((sum, t) => sum + t.subLessons.length, 0);
  const completedLessons = topics.reduce(
    (sum, t) => sum + t.subLessons.filter((s) => s.completed).length,
    0
  );

  return (
    <div className="flex-1 flex flex-col bg-zinc-900 h-full overflow-hidden">
      <div className="flex-1 overflow-y-auto">
        <div className="max-w-2xl mx-auto w-full px-6 py-8">
          {/* Header */}
          <div className="mb-8">
            <h1 className="text-xl font-bold text-zinc-100 mb-1">Dashboard</h1>
            <p className="text-sm text-zinc-500">
              Your learning overview and progress
            </p>
          </div>

          {/* Stats Grid */}
          <div className="grid grid-cols-2 gap-3 mb-8">
            <div className="p-5 rounded-xl bg-zinc-800/30 border border-zinc-800/50">
              <div className="flex items-center gap-2 mb-3">
                <BookOpen className="w-4 h-4 text-zinc-500" />
                <span className="text-[10px] font-medium text-zinc-500 uppercase tracking-wider">
                  Topics
                </span>
              </div>
              <p className="text-3xl font-bold text-zinc-100">{topics.length}</p>
              <p className="text-[11px] text-zinc-500 mt-1">active learning paths</p>
            </div>
            <div className="p-5 rounded-xl bg-zinc-800/30 border border-zinc-800/50">
              <div className="flex items-center gap-2 mb-3">
                <Brain className="w-4 h-4 text-zinc-500" />
                <span className="text-[10px] font-medium text-zinc-500 uppercase tracking-wider">
                  Lessons
                </span>
              </div>
              <p className="text-3xl font-bold text-zinc-100">
                {completedLessons}/{totalLessons}
              </p>
              <p className="text-[11px] text-zinc-500 mt-1">lessons completed</p>
            </div>
            <div className="p-5 rounded-xl bg-zinc-800/30 border border-zinc-800/50">
              <div className="flex items-center gap-2 mb-3">
                <Target className="w-4 h-4 text-zinc-500" />
                <span className="text-[10px] font-medium text-zinc-500 uppercase tracking-wider">
                  Quizzes
                </span>
              </div>
              <p className="text-3xl font-bold text-zinc-100">{attempts.length}</p>
              <p className="text-[11px] text-zinc-500 mt-1">
                {correctCount} correct answers
              </p>
            </div>
            <div className="p-5 rounded-xl bg-zinc-800/30 border border-zinc-800/50">
              <div className="flex items-center gap-2 mb-3">
                <Clock className="w-4 h-4 text-zinc-500" />
                <span className="text-[10px] font-medium text-zinc-500 uppercase tracking-wider">
                  Streak
                </span>
              </div>
              <p className="text-3xl font-bold text-zinc-100">7</p>
              <p className="text-[11px] text-zinc-500 mt-1">day learning streak</p>
            </div>
          </div>

          {/* Topic Progress */}
          <div className="mb-8">
            <div className="flex items-center justify-between mb-4">
              <h2 className="text-sm font-semibold text-zinc-200">Topic Progress</h2>
              <button
                onClick={() => onViewChange("courses")}
                className="flex items-center gap-1 text-xs text-zinc-500 hover:text-zinc-300 transition-colors"
              >
                View all
                <ArrowRight className="w-3 h-3" />
              </button>
            </div>
            <div className="space-y-3">
              {topics.map((topic) => {
                const completed = topic.subLessons.filter((s) => s.completed).length;
                const total = topic.subLessons.length;
                const pct = Math.round((completed / total) * 100);
                return (
                  <div
                    key={topic.id}
                    className="p-4 rounded-xl bg-zinc-800/20 border border-zinc-800/50"
                  >
                    <div className="flex items-center justify-between mb-2">
                      <div className="flex items-center gap-2">
                        <span>{topic.icon}</span>
                        <span className="text-sm text-zinc-200">{topic.title}</span>
                      </div>
                      <span className="text-xs text-zinc-500">
                        {completed}/{total} lessons
                      </span>
                    </div>
                    <div className="w-full h-1.5 bg-zinc-800 rounded-full overflow-hidden">
                      <div
                        className="h-full bg-zinc-500 rounded-full transition-all duration-500"
                        style={{ width: `${pct}%` }}
                      />
                    </div>
                  </div>
                );
              })}
            </div>
          </div>

          {/* Quick Actions */}
          <div>
            <h2 className="text-sm font-semibold text-zinc-200 mb-4">Quick Actions</h2>
            <div className="grid grid-cols-2 gap-3">
              <button
                onClick={() => onViewChange("chat")}
                className="p-4 rounded-xl border border-zinc-800/50 hover:bg-zinc-800/30 transition-colors duration-150 text-left group"
              >
                <TrendingUp className="w-5 h-5 text-zinc-500 mb-2 group-hover:text-zinc-300 transition-colors" />
                <p className="text-sm text-zinc-300 group-hover:text-zinc-100 transition-colors">
                  Continue Learning
                </p>
                <p className="text-[11px] text-zinc-600 mt-0.5">
                  Pick up where you left off
                </p>
              </button>
              <button
                onClick={() => onViewChange("wiki")}
                className="p-4 rounded-xl border border-zinc-800/50 hover:bg-zinc-800/30 transition-colors duration-150 text-left group"
              >
                <BookOpen className="w-5 h-5 text-zinc-500 mb-2 group-hover:text-zinc-300 transition-colors" />
                <p className="text-sm text-zinc-300 group-hover:text-zinc-100 transition-colors">
                  Browse Wiki
                </p>
                <p className="text-[11px] text-zinc-600 mt-0.5">
                  Explore your knowledge base
                </p>
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
