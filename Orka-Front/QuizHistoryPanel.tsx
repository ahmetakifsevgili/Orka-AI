/*
 * Design: Inline quiz history panel shown within the main app layout.
 * Shows summary stats and attempt list.
 * Replaces the standalone /history page.
 */

import { CheckCircle2, XCircle, BarChart3, Target, TrendingUp } from "lucide-react";
import { useQuizHistory } from "@/contexts/QuizHistoryContext";

export default function QuizHistoryPanel() {
  const { attempts } = useQuizHistory();
  const correctCount = attempts.filter((a) => a.isCorrect).length;
  const totalCount = attempts.length;
  const accuracy = totalCount > 0 ? Math.round((correctCount / totalCount) * 100) : 0;

  return (
    <div className="flex-1 flex flex-col bg-zinc-900 h-full overflow-hidden">
      <div className="flex-1 overflow-y-auto">
        <div className="max-w-2xl mx-auto w-full px-6 py-8">
          {/* Header */}
          <div className="mb-8">
            <h1 className="text-xl font-bold text-zinc-100 mb-1">Quiz History</h1>
            <p className="text-sm text-zinc-500">
              Track your quiz performance and review past attempts
            </p>
          </div>

          {/* Stats Cards */}
          <div className="grid grid-cols-3 gap-3 mb-8">
            <div className="p-4 rounded-xl bg-zinc-800/30 border border-zinc-800/50">
              <div className="flex items-center gap-2 mb-2">
                <BarChart3 className="w-4 h-4 text-zinc-500" />
                <span className="text-[10px] font-medium text-zinc-500 uppercase tracking-wider">
                  Total
                </span>
              </div>
              <p className="text-2xl font-bold text-zinc-100">{totalCount}</p>
              <p className="text-[11px] text-zinc-500 mt-0.5">quizzes taken</p>
            </div>
            <div className="p-4 rounded-xl bg-zinc-800/30 border border-zinc-800/50">
              <div className="flex items-center gap-2 mb-2">
                <Target className="w-4 h-4 text-zinc-500" />
                <span className="text-[10px] font-medium text-zinc-500 uppercase tracking-wider">
                  Correct
                </span>
              </div>
              <p className="text-2xl font-bold text-zinc-100">{correctCount}</p>
              <p className="text-[11px] text-zinc-500 mt-0.5">correct answers</p>
            </div>
            <div className="p-4 rounded-xl bg-zinc-800/30 border border-zinc-800/50">
              <div className="flex items-center gap-2 mb-2">
                <TrendingUp className="w-4 h-4 text-zinc-500" />
                <span className="text-[10px] font-medium text-zinc-500 uppercase tracking-wider">
                  Accuracy
                </span>
              </div>
              <p className="text-2xl font-bold text-zinc-100">{accuracy}%</p>
              <p className="text-[11px] text-zinc-500 mt-0.5">success rate</p>
            </div>
          </div>

          {/* Attempts List */}
          {totalCount === 0 ? (
            <div className="text-center py-16">
              <div className="w-12 h-12 rounded-xl bg-zinc-800 border border-zinc-700 flex items-center justify-center mx-auto mb-4">
                <BarChart3 className="w-5 h-5 text-zinc-500" />
              </div>
              <p className="text-sm text-zinc-400 mb-1">No quizzes taken yet</p>
              <p className="text-xs text-zinc-600">
                Start a conversation and quizzes will appear to test your knowledge
              </p>
            </div>
          ) : (
            <div className="space-y-2">
              <p className="text-[10px] font-medium text-zinc-500 uppercase tracking-wider mb-3">
                Recent Attempts
              </p>
              {[...attempts].reverse().map((attempt) => (
                <div
                  key={attempt.id}
                  className="flex items-start gap-3 p-4 rounded-xl bg-zinc-800/20 border border-zinc-800/50 hover:bg-zinc-800/30 transition-colors duration-150"
                >
                  <div className="flex-shrink-0 mt-0.5">
                    {attempt.isCorrect ? (
                      <CheckCircle2 className="w-4.5 h-4.5 text-green-500" />
                    ) : (
                      <XCircle className="w-4.5 h-4.5 text-red-500" />
                    )}
                  </div>
                  <div className="flex-1 min-w-0">
                    <p className="text-sm text-zinc-200 leading-relaxed mb-1">
                      {attempt.question}
                    </p>
                    <p className="text-xs text-zinc-500 leading-relaxed">
                      {attempt.explanation}
                    </p>
                    <p className="text-[10px] text-zinc-600 mt-2">
                      {attempt.timestamp.toLocaleString()}
                    </p>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
