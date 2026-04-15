/*
 * Design: Academic quiz card with title, radio options (A/B/C/D),
 * green check for correct, Previous/Next navigation.
 * Matches reference image styling.
 */

import { useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { CheckCircle2, XCircle, ChevronLeft, ChevronRight, Loader2, Sparkles, Zap } from "lucide-react";
import type { QuizData, QuizAttempt } from "@/lib/types";
import { useQuizHistory } from "@/contexts/QuizHistoryContext";
import { QuizAPI } from "@/services/api";

interface QuizCardProps {
  quiz: QuizData;
  messageId: string;
  /** Kullanıcı cevabı gönderince çağrılır; ChatPanel bunu backend'e iletir. */
  onSubmitAnswer?: (formattedAnswer: string) => void;
  isBaseline?: boolean;
}

const OPTION_LABELS = ["A", "B", "C", "D", "E", "F"];

export default function QuizCard({ quiz, messageId, onSubmitAnswer, isBaseline = false }: QuizCardProps) {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [submitState, setSubmitState] = useState<"idle" | "evaluating" | "done">("idle");
  const [currentQuestion, setCurrentQuestion] = useState(0);
  const [isCorrectAnswer, setIsCorrectAnswer] = useState(false);
  const { addQuizAttempt } = useQuizHistory();

  const handleSubmit = () => {
    if (!selectedId) return;
    setSubmitState("evaluating");

    // Simulate AI eval delay smoothly
    setTimeout(() => {
        setSubmitState("done");
        const selectedOption = quiz.options.find((o) => o.id === selectedId);
        setIsCorrectAnswer(selectedOption?.isCorrect ?? false);
        const idx = quiz.options.findIndex((o) => o.id === selectedId);
        const label = OPTION_LABELS[idx] ?? "?";

        const attempt: QuizAttempt = {
          id: `qa-${Date.now()}`,
          messageId,
          question: quiz.question,
          selectedOptionId: selectedId,
          isCorrect: selectedOption?.isCorrect ?? false,
          explanation: quiz.explanation,
          timestamp: new Date(),
        };

        // 1. Local context'e ekle (anında UI güncellemesi)
        addQuizAttempt(attempt);

        // 2. Backend'e kaydet — fire-and-forget, hata UI'ı bloke etmez
        QuizAPI.recordAttempt({
          messageId: attempt.messageId,
          question: attempt.question,
          selectedOptionId: attempt.selectedOptionId,
          isCorrect: attempt.isCorrect,
          explanation: attempt.explanation,
        }).catch(() => {
          // Endpoint henüz yoksa veya hata varsa sessizce geç
        });

        // 3. Cevabı sohbet mesajı olarak ChatPanel'e ilet
        if (onSubmitAnswer) {
          const formatted = `**Quiz Cevabım:** ${label}) ${selectedOption?.text ?? ""}`;
          onSubmitAnswer(formatted);
        }
    }, 1200);
  };

  const handleReset = () => {
    setSelectedId(null);
    setSubmitState("idle");
  };

  const getOptionStyle = (optionId: string, isCorrect: boolean) => {
    if (submitState !== "done") {
      return selectedId === optionId
        ? "border-zinc-500 bg-zinc-800/60"
        : "border-zinc-800 hover:border-zinc-700 hover:bg-zinc-800/30";
    }
    if (isCorrect) return "border-green-700/60 bg-green-900/15";
    if (optionId === selectedId && !isCorrect) return "border-red-700/60 bg-red-900/15 animate-shake";
    return "border-zinc-800/50 opacity-40";
  };

  return (
    <motion.div
      initial={{ opacity: 0, scale: isBaseline ? 0.95 : 1, y: 8 }}
      animate={{ opacity: 1, scale: 1, y: 0 }}
      transition={{ duration: 0.3, ease: "easeOut" }}
      className={`rounded-xl border mt-4 overflow-hidden shadow-2xl ${
        isBaseline 
          ? "bg-zinc-950 border-emerald-900/40 shadow-emerald-900/5" 
          : "bg-zinc-900/80 border-zinc-800"
      }`}
    >
      {/* Quiz Header */}
      <div className={`px-6 pt-5 pb-4 border-b ${isBaseline ? "border-emerald-900/30 bg-emerald-950/20" : "border-zinc-800/50"}`}>
        <h3 className={`text-[15px] font-semibold ${isBaseline ? "text-emerald-400" : "text-zinc-100"}`}>
          {isBaseline ? "Seviyeni Belirliyoruz (Sıfır Noktası Testi)" : (quiz.topic || "Knowledge Check") + ` — Quiz ${currentQuestion + 1}`}
        </h3>
      </div>

      {/* Question */}
      <div className="px-6 py-5">
        <p className="text-sm text-zinc-200 leading-relaxed mb-5">
          {quiz.question}
        </p>

        {/* Options */}
        <div className="space-y-2.5 relative">
          {quiz.options.map((option, idx) => (
            <button
              key={option.id}
              onClick={() => submitState === "idle" && setSelectedId(option.id)}
              disabled={submitState !== "idle"}
              className={`flex items-center gap-3.5 w-full px-4 py-3 rounded-lg border text-left transition-all duration-300 ${getOptionStyle(option.id, option.isCorrect)}`}
            >
              {/* Radio / Check indicator */}
              <div className="flex-shrink-0">
                {submitState === "done" && option.isCorrect ? (
                  <motion.div initial={{ scale: 0 }} animate={{ scale: 1 }}>
                    <CheckCircle2 className="w-5 h-5 text-green-400" />
                  </motion.div>
                ) : submitState === "done" && option.id === selectedId && !option.isCorrect ? (
                  <motion.div initial={{ scale: 0 }} animate={{ scale: 1 }}>
                    <XCircle className="w-5 h-5 text-red-400" />
                  </motion.div>
                ) : (
                  <div
                    className={`w-5 h-5 rounded-full border-2 flex items-center justify-center transition-colors duration-150 ${
                      selectedId === option.id
                        ? (isBaseline ? "border-emerald-500" : "border-zinc-300")
                        : "border-zinc-600"
                    }`}
                  >
                    {selectedId === option.id && (
                      <div className={`w-2.5 h-2.5 rounded-full ${isBaseline ? "bg-emerald-500" : "bg-zinc-200"}`} />
                    )}
                  </div>
                )}
              </div>

              {/* Label */}
              <span
                className={`text-sm tracking-wide ${
                  submitState === "done" && option.isCorrect
                    ? "text-green-300 font-medium"
                    : submitState === "done" && option.id === selectedId && !option.isCorrect
                      ? "text-red-300 font-medium"
                      : "text-zinc-300"
                }`}
              >
                {OPTION_LABELS[idx]}) {option.text}
              </span>
            </button>
          ))}
          {submitState === "evaluating" && (
             <div className="absolute inset-0 bg-transparent" /> /* Block clicks while evaluating */
          )}
        </div>

        {/* XP Rozeti — sadece doğru cevapta gösterilir */}
        <AnimatePresence>
          {submitState === "done" && isCorrectAnswer && !isBaseline && (
            <motion.div
              initial={{ opacity: 0, scale: 0.7, y: -8 }}
              animate={{ opacity: 1, scale: 1, y: 0 }}
              exit={{ opacity: 0, scale: 0.7 }}
              transition={{ type: "spring", stiffness: 400, damping: 20, delay: 0.15 }}
              className="mt-3 flex items-center gap-2 px-4 py-2.5 rounded-xl bg-emerald-900/20 border border-emerald-700/30 w-fit"
            >
              <Zap className="w-4 h-4 text-emerald-400 fill-emerald-400" />
              <span className="text-sm font-bold text-emerald-300">+20 XP Kazandınız!</span>
              <span className="text-[10px] text-emerald-600 font-medium uppercase tracking-wider">Tebrikler</span>
            </motion.div>
          )}
        </AnimatePresence>

        {/* Explanation */}
        {submitState === "done" && (
          <motion.div
            initial={{ opacity: 0, height: 0 }}
            animate={{ opacity: 1, height: "auto" }}
            transition={{ duration: 0.3 }}
            className={`mt-4 p-4 rounded-lg border ${
                quiz.options.find(o => o.id === selectedId)?.isCorrect
                   ? "bg-green-950/20 border-green-800/30"
                   : "bg-red-950/20 border-red-800/30"
            }`}
          >
            <p className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider mb-1.5 flex items-center gap-1.5">
              <Sparkles className="w-3 h-3" /> AI Değerlendirmesi
            </p>
            <p className="text-sm text-zinc-300 leading-relaxed">
              {quiz.explanation}
            </p>
          </motion.div>
        )}
      </div>

      {/* Footer */}
      <div className={`px-6 py-4 border-t flex items-center justify-center transition-colors ${
          submitState === "idle" ? "border-zinc-800/50" : "border-transparent bg-zinc-950/40"
      }`}>
        {submitState === "idle" ? (
          <button
            onClick={handleSubmit}
            disabled={!selectedId}
            className={`px-8 py-2.5 rounded-full text-sm font-semibold transition-all duration-200 disabled:opacity-30 disabled:cursor-not-allowed shadow-lg ${
              isBaseline 
                ? "bg-emerald-500 text-emerald-950 hover:bg-emerald-400 shadow-emerald-500/20" 
                : "bg-zinc-100 text-zinc-950 hover:bg-white shadow-white/5"
            }`}
          >
            Cevabı Gönder
          </button>
        ) : submitState === "evaluating" ? (
          <motion.div
            initial={{ opacity: 0, scale: 0.95 }}
            animate={{ opacity: 1, scale: 1 }}
            className="flex items-center gap-2 text-sm text-emerald-400 font-medium"
          >
            <Loader2 className="w-4 h-4 animate-spin" />
            <span>AI Cevabını Değerlendiriyor...</span>
          </motion.div>
        ) : (
          <motion.div
             initial={{ opacity: 0, y: 5 }}
             animate={{ opacity: 1, y: 0 }}
             className="text-sm text-zinc-500 font-medium"
          >
             ✓ İleti Kaydedildi
          </motion.div>
        )}
      </div>
    </motion.div>
  );
}
