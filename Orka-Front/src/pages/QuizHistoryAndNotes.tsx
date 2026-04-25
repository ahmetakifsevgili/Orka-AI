/*
 * Design: "Sessiz Lüks" — Quiz history + personal notes page.
 * Sidebar persistent, content in center.
 */

import { Link } from "wouter";
import { ArrowLeft, Target, CheckCircle2, XCircle } from "lucide-react";
import { motion } from "framer-motion";
import OrcaLogo from "@/components/OrcaLogo";
import { useQuizHistory } from "@/contexts/QuizHistoryContext";

export default function QuizHistoryAndNotes() {
  const { attempts } = useQuizHistory();

  const correctCount = attempts.filter((a) => a.isCorrect).length;
  const totalCount = attempts.length;

  return (
    <div className="h-screen flex overflow-hidden bg-zinc-950">
      {/* Minimal sidebar */}
      <div className="w-64 flex-shrink-0 bg-zinc-950 border-r border-zinc-800 flex flex-col">
        <div className="px-4 py-5">
          <Link href="/app" className="flex items-center gap-2.5">
            <OrcaLogo className="w-5 h-5 text-zinc-100" />
            <span className="font-semibold text-zinc-100 text-sm">Orka AI</span>
          </Link>
        </div>
        <div className="px-4 mt-4">
          <Link
            href="/app"
            className="flex items-center gap-2 text-xs text-zinc-500 hover:text-zinc-300 transition-colors duration-150"
          >
            <ArrowLeft className="w-3.5 h-3.5" />
            Back to Learning
          </Link>
        </div>
      </div>

      {/* Main content */}
      <div className="flex-1 overflow-y-auto bg-zinc-900">
        <div className="max-w-3xl mx-auto px-8 py-8">
          <motion.div
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.25 }}
          >
            <h1 className="text-xl font-semibold text-zinc-100 mb-2">Quiz History</h1>
            <p className="text-sm text-zinc-500 mb-6">
              Track your quiz performance and review past attempts.
            </p>

            {/* Summary Stats */}
            {totalCount > 0 && (
              <div className="grid grid-cols-3 gap-3 mb-8">
                <div className="bg-zinc-950 border border-zinc-800 rounded-lg p-4">
                  <p className="text-2xl font-semibold text-zinc-100">{totalCount}</p>
                  <p className="text-xs text-zinc-500 mt-1">Total Attempts</p>
                </div>
                <div className="bg-zinc-950 border border-zinc-800 rounded-lg p-4">
                  <p className="text-2xl font-semibold text-green-400">{correctCount}</p>
                  <p className="text-xs text-zinc-500 mt-1">Correct</p>
                </div>
                <div className="bg-zinc-950 border border-zinc-800 rounded-lg p-4">
                  <p className="text-2xl font-semibold text-zinc-100">
                    {totalCount > 0 ? Math.round((correctCount / totalCount) * 100) : 0}%
                  </p>
                  <p className="text-xs text-zinc-500 mt-1">Accuracy</p>
                </div>
              </div>
            )}

            {/* Quiz Attempts List */}
            {totalCount === 0 ? (
              <div className="text-center py-16">
                <Target className="w-8 h-8 text-zinc-700 mx-auto mb-3" />
                <p className="text-sm text-zinc-500">No quiz attempts yet.</p>
                <p className="text-xs text-zinc-600 mt-1">
                  Complete quizzes in the chat to see your history here.
                </p>
                <Link
                  href="/app"
                  className="inline-block mt-4 text-xs text-zinc-400 hover:text-zinc-200 transition-colors duration-150"
                >
                  Start Learning →
                </Link>
              </div>
            ) : (
              <div className="space-y-3">
                {[...attempts].reverse().map((attempt) => (
                  <div
                    key={attempt.id}
                    className="bg-zinc-950 border border-zinc-800 rounded-lg p-4"
                  >
                    <div className="flex items-start gap-3">
                      {attempt.isCorrect ? (
                        <CheckCircle2 className="w-4 h-4 text-emerald-600 mt-0.5 flex-shrink-0" />
                      ) : (
                        <XCircle className="w-4 h-4 text-amber-600 mt-0.5 flex-shrink-0" />
                      )}
                      <div className="flex-1">
                        <p className="text-sm text-zinc-200">{attempt.question}</p>
                        <p className="text-xs text-zinc-500 mt-2">{attempt.explanation}</p>
                        <p className="text-[10px] text-zinc-600 mt-2">
                          {attempt.timestamp.toLocaleString()}
                        </p>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </motion.div>
        </div>
      </div>
    </div>
  );
}
