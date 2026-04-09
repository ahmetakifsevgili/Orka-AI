/*
 * Design: Academic quiz card with title, radio options (A/B/C/D),
 * green check for correct, Previous/Next navigation.
 * Matches reference image styling.
 */

import { useState } from "react";
import { motion } from "framer-motion";
import { CheckCircle2, XCircle, ChevronLeft, ChevronRight } from "lucide-react";
import type { QuizData, QuizAttempt } from "@/lib/types";
import { useQuizHistory } from "@/contexts/QuizHistoryContext";

interface QuizCardProps {
  quiz: QuizData;
  messageId: string;
}

const OPTION_LABELS = ["A", "B", "C", "D", "E", "F"];

export default function QuizCard({ quiz, messageId }: QuizCardProps) {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [submitted, setSubmitted] = useState(false);
  const [currentQuestion, setCurrentQuestion] = useState(0);
  const { addQuizAttempt } = useQuizHistory();

  const handleSubmit = () => {
    if (!selectedId) return;
    setSubmitted(true);

    const selectedOption = quiz.options.find((o) => o.id === selectedId);
    const attempt: QuizAttempt = {
      id: `qa-${Date.now()}`,
      messageId,
      question: quiz.question,
      selectedOptionId: selectedId,
      isCorrect: selectedOption?.isCorrect ?? false,
      explanation: quiz.explanation,
      timestamp: new Date(),
    };
    addQuizAttempt(attempt);
  };

  const handleReset = () => {
    setSelectedId(null);
    setSubmitted(false);
  };

  const getOptionStyle = (optionId: string, isCorrect: boolean) => {
    if (!submitted) {
      return selectedId === optionId
        ? "border-zinc-500 bg-zinc-800/60"
        : "border-zinc-800 hover:border-zinc-700 hover:bg-zinc-800/30";
    }
    if (isCorrect) return "border-green-700/60 bg-green-900/15";
    if (optionId === selectedId && !isCorrect) return "border-red-700/60 bg-red-900/15";
    return "border-zinc-800/50 opacity-40";
  };

  return (
    <motion.div
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.25, ease: "easeOut" }}
      className="bg-zinc-900/80 rounded-xl border border-zinc-800 mt-4 overflow-hidden"
    >
      {/* Quiz Header */}
      <div className="px-6 pt-5 pb-4 border-b border-zinc-800/50">
        <h3 className="text-[15px] font-semibold text-zinc-100">
          {quiz.topic || "Knowledge Check"} — Quiz {currentQuestion + 1}
        </h3>
      </div>

      {/* Question */}
      <div className="px-6 py-5">
        <p className="text-sm text-zinc-200 leading-relaxed mb-5">
          {quiz.question}
        </p>

        {/* Options */}
        <div className="space-y-2.5">
          {quiz.options.map((option, idx) => (
            <button
              key={option.id}
              onClick={() => !submitted && setSelectedId(option.id)}
              disabled={submitted}
              className={`flex items-center gap-3.5 w-full px-4 py-3 rounded-lg border text-left transition-all duration-150 ${getOptionStyle(option.id, option.isCorrect)}`}
            >
              {/* Radio / Check indicator */}
              <div className="flex-shrink-0">
                {submitted && option.isCorrect ? (
                  <CheckCircle2 className="w-5 h-5 text-green-400" />
                ) : submitted && option.id === selectedId && !option.isCorrect ? (
                  <XCircle className="w-5 h-5 text-red-400" />
                ) : (
                  <div
                    className={`w-5 h-5 rounded-full border-2 flex items-center justify-center transition-colors duration-150 ${
                      selectedId === option.id
                        ? "border-zinc-300"
                        : "border-zinc-600"
                    }`}
                  >
                    {selectedId === option.id && (
                      <div className="w-2.5 h-2.5 rounded-full bg-zinc-200" />
                    )}
                  </div>
                )}
              </div>

              {/* Label */}
              <span
                className={`text-sm ${
                  submitted && option.isCorrect
                    ? "text-green-300 font-medium"
                    : submitted && option.id === selectedId && !option.isCorrect
                      ? "text-red-300"
                      : "text-zinc-300"
                }`}
              >
                {OPTION_LABELS[idx]}) {option.text}
              </span>
            </button>
          ))}
        </div>

        {/* Explanation */}
        {submitted && (
          <motion.div
            initial={{ opacity: 0, height: 0 }}
            animate={{ opacity: 1, height: "auto" }}
            transition={{ duration: 0.2 }}
            className="mt-5 p-4 rounded-lg bg-zinc-800/40 border border-zinc-700/30"
          >
            <p className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider mb-1.5">
              Explanation
            </p>
            <p className="text-sm text-zinc-300 leading-relaxed">
              {quiz.explanation}
            </p>
          </motion.div>
        )}
      </div>

      {/* Footer with Previous / Next */}
      <div className="px-6 py-4 border-t border-zinc-800/50 flex items-center justify-between">
        <button
          onClick={handleReset}
          disabled={!submitted}
          className="flex items-center gap-1.5 px-4 py-2 rounded-lg text-sm text-zinc-400 hover:text-zinc-200 hover:bg-zinc-800 transition-colors duration-150 disabled:opacity-30 disabled:cursor-not-allowed"
        >
          <ChevronLeft className="w-4 h-4" />
          Previous
        </button>

        {!submitted ? (
          <button
            onClick={handleSubmit}
            disabled={!selectedId}
            className="px-5 py-2 bg-zinc-800 text-zinc-100 rounded-lg text-sm font-medium hover:bg-zinc-700 transition-colors duration-150 disabled:opacity-30 disabled:cursor-not-allowed"
          >
            Check Answer
          </button>
        ) : null}

        <button
          onClick={() => {
            handleReset();
            setCurrentQuestion((prev) => prev + 1);
          }}
          className="flex items-center gap-1.5 px-4 py-2 rounded-lg text-sm bg-zinc-800 text-zinc-200 hover:bg-zinc-700 transition-colors duration-150"
        >
          Next
          <ChevronRight className="w-4 h-4" />
        </button>
      </div>
    </motion.div>
  );
}
