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
  quiz: QuizData | QuizData[];
  messageId: string;
  /** Kullanıcı cevabı gönderince çağrılır; ChatPanel bunu backend'e iletir. */
  onSubmitAnswer?: (formattedAnswer: string) => void;
  isBaseline?: boolean;
  onOpenIDE?: (question?: string) => void;
}

const OPTION_LABELS = ["A", "B", "C", "D", "E", "F"];

export default function QuizCard({ quiz, messageId, onSubmitAnswer, onOpenIDE, isBaseline = false }: QuizCardProps) {
  const isArray = Array.isArray(quiz);
  // Güvenlik: coding soruları daima dizinin sonuna alınır ki IDE submission akışı
  // orta konumda kalıp QuizCard'ı kitlemesin. Backend prompt kısıtı ana savunma,
  // bu client-side sort fallback.
  const quizArray = (() => {
    const base = isArray ? [...(quiz as QuizData[])] : [quiz as QuizData];
    return base.sort((a, b) => {
      const aCoding = a.type === "coding" ? 1 : 0;
      const bCoding = b.type === "coding" ? 1 : 0;
      return aCoding - bCoding;
    });
  })();
  const totalQuestions = quizArray.length;

  const [currentQuestionIdx, setCurrentQuestionIdx] = useState(0);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [submitState, setSubmitState] = useState<"idle" | "evaluating" | "done">("idle");
  const [isCorrectAnswer, setIsCorrectAnswer] = useState(false);
  
  // Track answers for all questions
  const [collectedAnswers, setCollectedAnswers] = useState<{question: string, text: string, isCorrect: boolean, topic?: string}[]>([]);

  const { addQuizAttempt } = useQuizHistory();

  const currentQuiz = quizArray[currentQuestionIdx];

  const handleSubmit = () => {
    if (!selectedId) return;
    setSubmitState("evaluating");

    setTimeout(() => {
        setSubmitState("done");
        const selectedOption = currentQuiz.options.find((o) => o.id === selectedId);
        setIsCorrectAnswer(selectedOption?.isCorrect ?? false);
        const idx = currentQuiz.options.findIndex((o) => o.id === selectedId);
        const label = OPTION_LABELS[idx] ?? "?";

        const attempt: QuizAttempt = {
          id: `qa-${Date.now()}`,
          messageId,
          question: currentQuiz.question,
          selectedOptionId: selectedId,
          isCorrect: selectedOption?.isCorrect ?? false,
          explanation: currentQuiz.explanation,
          timestamp: new Date(),
        };

        addQuizAttempt(attempt);

        QuizAPI.recordAttempt({
          messageId: attempt.messageId,
          question: attempt.question,
          selectedOptionId: attempt.selectedOptionId,
          isCorrect: attempt.isCorrect,
          explanation: attempt.explanation,
        }).catch(() => {});

        // Save answer to collected array
        const newCollected = [...collectedAnswers, {
            question: currentQuiz.question,
            text: `${label}) ${selectedOption?.text ?? ""}`,
            isCorrect: attempt.isCorrect,
            topic: currentQuiz.topic
        }];
        setCollectedAnswers(newCollected);

        // If it's a single quiz, or we're on the last question, submit to backend chat
        if (!isArray || currentQuestionIdx === totalQuestions - 1) {
            if (onSubmitAnswer) {
              if (isArray) {
                 // Submit aggregated summary of test
                 const score = newCollected.filter(a => a.isCorrect).length;
                 const summaryRows = newCollected.map((a, i) => `Soru ${i+1}: ${a.text} (${a.isCorrect ? 'Doğru' : 'Yanlış'})`).join("\n");
                 
                 const failedTopicsList = Array.from(new Set(newCollected.filter(a => !a.isCorrect && a.topic).map(a => a.topic)));
                 const failedTopicsStr = failedTopicsList.length > 0 ? `\n\nHata Yapılan Konular: ${failedTopicsList.join(", ")}` : "";

                 const formatted = `**Seviye Testi Tamamlandı:** ${score}/${totalQuestions} Doğru.\n\n${summaryRows}${failedTopicsStr}`;
                 onSubmitAnswer(formatted);
              } else {
                 const formatted = `**Quiz Cevabım:** ${label}) ${selectedOption?.text ?? ""}`;
                 onSubmitAnswer(formatted);
              }
            }
        }
    }, 1200);
  };

  const handleNextQuestion = () => {
      if (currentQuestionIdx < totalQuestions - 1) {
          setCurrentQuestionIdx(prev => prev + 1);
          setSelectedId(null);
          setSubmitState("idle");
          setIsCorrectAnswer(false);
      }
  };

  const getOptionStyle = (optionId: string, isCorrect: boolean) => {
    if (submitState !== "done") {
      return selectedId === optionId
        ? "border-zinc-500 bg-zinc-800/60"
        : "border-zinc-800 hover:border-zinc-700 hover:bg-zinc-800/30";
    }
    if (isCorrect) return "border-emerald-700/60 bg-emerald-900/15";
    if (optionId === selectedId && !isCorrect) return "border-amber-700/60 bg-amber-900/15 animate-shake";
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
      <div className={`px-6 pt-5 pb-4 border-b ${isBaseline ? "border-emerald-900/30 bg-emerald-950/20" : "border-zinc-800/50"}`}>
        <h3 className={`text-[15px] font-semibold ${isBaseline ? "text-emerald-400" : "text-zinc-100"}`}>
          {isBaseline 
            ? `Seviyeni Belirliyoruz (Test ${currentQuestionIdx + 1}/${totalQuestions})` 
            : (currentQuiz.topic || "Knowledge Check") + ` — Quiz ${currentQuestionIdx + 1}${isArray ? `/${totalQuestions}` : ''}`
          }
        </h3>
      </div>

      <div className="px-6 py-5">
        <p className="text-sm text-zinc-200 leading-relaxed mb-5">
          {currentQuiz.question}
        </p>

        <div className="space-y-2.5 relative">
          {currentQuiz.type === 'coding' ? (
            <div className="flex flex-col items-center justify-center p-8 border border-zinc-800/60 rounded-xl bg-zinc-900/40 text-center">
                <div className="w-12 h-12 rounded-full bg-emerald-900/20 flex items-center justify-center mb-4">
                    <span className="text-xl">&lt;/&gt;</span>
                </div>
                <h4 className="text-sm font-semibold text-zinc-200 mb-2">Bu Bir Kodlama Sorusu</h4>
                <p className="text-xs text-zinc-400 max-w-[250px] mb-6 leading-relaxed">
                    Sağ taraftaki İnteraktif IDE'yi kullanarak kodunuzu yazın ve algoritmayı çözün. Bitirdiğinizde IDE içindeki <strong>"Hocaya Gönder"</strong> butonuna basarak cevabınızı iletebilirsiniz.
                </p>
                <button
                    onClick={() => onOpenIDE?.(currentQuiz.question)}
                    className="flex items-center gap-2 px-6 py-2.5 bg-emerald-600 hover:bg-emerald-500 text-zinc-950 rounded-lg font-medium text-sm transition-colors shadow-lg shadow-emerald-900/20"
                >
                    IDE'yi Aç ve Kod Yaz
                </button>
            </div>
          ) : (
            currentQuiz.options.map((option, idx) => (
              <button
                key={option.id}
                onClick={() => submitState === "idle" && setSelectedId(option.id)}
                disabled={submitState !== "idle"}
                className={`flex items-center gap-3.5 w-full px-4 py-3 rounded-lg border text-left transition-all duration-300 ${getOptionStyle(option.id, option.isCorrect)}`}
              >
                <div className="flex-shrink-0">
                  {submitState === "done" && option.isCorrect ? (
                    <motion.div initial={{ scale: 0 }} animate={{ scale: 1 }}>
                      <CheckCircle2 className="w-5 h-5 text-emerald-400" />
                    </motion.div>
                  ) : submitState === "done" && option.id === selectedId && !option.isCorrect ? (
                    <motion.div initial={{ scale: 0 }} animate={{ scale: 1 }}>
                      <XCircle className="w-5 h-5 text-amber-400" />
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

                <span
                  className={`text-sm tracking-wide ${
                    submitState === "done" && option.isCorrect
                      ? "text-emerald-300 font-medium"
                      : submitState === "done" && option.id === selectedId && !option.isCorrect
                        ? "text-amber-300 font-medium"
                        : "text-zinc-300"
                  }`}
                >
                  {OPTION_LABELS[idx]}) {option.text}
                </span>
              </button>
            ))
          )}
          {submitState === "evaluating" && (
             <div className="absolute inset-0 bg-transparent" />
          )}
        </div>

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

        {submitState === "done" && currentQuiz.explanation && (
          <motion.div
            initial={{ opacity: 0, height: 0 }}
            animate={{ opacity: 1, height: "auto" }}
            transition={{ duration: 0.3 }}
            className={`mt-4 p-4 rounded-lg border ${
                currentQuiz.options.find(o => o.id === selectedId)?.isCorrect
                   ? "bg-emerald-950/20 border-emerald-800/30"
                   : "bg-amber-950/20 border-amber-800/30"
            }`}
          >
            <p className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider mb-1.5 flex items-center gap-1.5">
              <Sparkles className="w-3 h-3" /> AI Değerlendirmesi
            </p>
            <p className="text-sm text-zinc-300 leading-relaxed">
              {currentQuiz.explanation}
            </p>
          </motion.div>
        )}
      </div>

      <div className={`px-6 py-4 border-t flex flex-col items-center gap-3 transition-colors ${
          submitState === "idle" ? "border-zinc-800/50" : "border-transparent bg-zinc-950/40"
      }`}>
        {submitState === "idle" ? (
          <>
            {currentQuiz.type !== "coding" && (
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
            )}
            
            {!isBaseline && (
                <button
                    onClick={() => onSubmitAnswer?.("[SKIP_QUIZ]")}
                    className="text-[11px] text-zinc-500 hover:text-zinc-300 transition-colors uppercase tracking-[0.1em] font-medium"
                >
                    Konuyu Atla ve Geç →
                </button>
            )}
          </>
        ) : submitState === "evaluating" ? (
          <motion.div
            initial={{ opacity: 0, scale: 0.95 }}
            animate={{ opacity: 1, scale: 1 }}
            className="flex items-center gap-2 text-sm text-emerald-400 font-medium h-10"
          >
            <Loader2 className="w-4 h-4 animate-spin" />
            <span>AI Cevabını Değerlendiriyor...</span>
          </motion.div>
        ) : (
          isArray && currentQuestionIdx < totalQuestions - 1 ? (
             <button
                onClick={handleNextQuestion}
                className="flex items-center gap-2 px-6 py-2 rounded-full text-sm font-semibold bg-emerald-900/60 text-emerald-400 hover:bg-emerald-800/60 border border-emerald-700/50 transition-all duration-200 shadow-lg shadow-emerald-900/20"
             >
                Sıradaki Soru <ChevronRight className="w-4 h-4" />
             </button>
          ) : (
             <motion.div
               initial={{ opacity: 0, y: 5 }}
               animate={{ opacity: 1, y: 0 }}
               className="text-sm text-emerald-500/80 font-medium flex items-center gap-2 h-10"
             >
                <div className="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-pulse" />
                ✓ İzlenim Kaydedildi
             </motion.div>
          )
        )}
      </div>
    </motion.div>
  );
}
