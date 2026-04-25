import { useState } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { CheckCircle2, ChevronRight, Code2, Loader2, Sparkles, XCircle, Zap } from "lucide-react";
import type { QuizAttempt, QuizData } from "@/lib/types";
import { useQuizHistory } from "@/contexts/QuizHistoryContext";
import { QuizAPI } from "@/services/api";
import MarkdownRender from "./MarkdownRender";

interface QuizCardProps {
  quiz: QuizData | QuizData[];
  messageId: string;
  onSubmitAnswer?: (formattedAnswer: string) => void;
  isBaseline?: boolean;
  onOpenIDE?: (question?: string) => void;
}

const OPTION_LABELS = ["A", "B", "C", "D", "E", "F"];

export default function QuizCard({ quiz, messageId, onSubmitAnswer, onOpenIDE, isBaseline = false }: QuizCardProps) {
  const isArray = Array.isArray(quiz);
  const quizArray = (isArray ? [...quiz] : [quiz]).sort((a, b) => {
    const aCoding = a.type === "coding" ? 1 : 0;
    const bCoding = b.type === "coding" ? 1 : 0;
    return aCoding - bCoding;
  });
  const totalQuestions = quizArray.length;
  const [currentQuestionIdx, setCurrentQuestionIdx] = useState(0);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [submitState, setSubmitState] = useState<"idle" | "evaluating" | "done">("idle");
  const [isCorrectAnswer, setIsCorrectAnswer] = useState(false);
  const [collectedAnswers, setCollectedAnswers] = useState<
    { question: string; text: string; isCorrect: boolean; topic?: string }[]
  >([]);
  const { addQuizAttempt } = useQuizHistory();
  const currentQuiz = quizArray[currentQuestionIdx];

  const selectedOption = currentQuiz.options.find((option) => option.id === selectedId);

  const handleSubmit = () => {
    if (!selectedId) return;
    setSubmitState("evaluating");

    setTimeout(() => {
      setSubmitState("done");
      const correct = selectedOption?.isCorrect ?? false;
      setIsCorrectAnswer(correct);
      const idx = currentQuiz.options.findIndex((option) => option.id === selectedId);
      const label = OPTION_LABELS[idx] ?? "?";

      const attempt: QuizAttempt = {
        id: `qa-${Date.now()}`,
        messageId,
        question: currentQuiz.question,
        selectedOptionId: selectedId,
        isCorrect: correct,
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

      const nextCollected = [
        ...collectedAnswers,
        {
          question: currentQuiz.question,
          text: `${label}) ${selectedOption?.text ?? ""}`,
          isCorrect: correct,
          topic: currentQuiz.topic,
        },
      ];
      setCollectedAnswers(nextCollected);

      if (!isArray || currentQuestionIdx === totalQuestions - 1) {
        if (!onSubmitAnswer) return;
        if (isArray) {
          const score = nextCollected.filter((answer) => answer.isCorrect).length;
          const summaryRows = nextCollected
            .map((answer, i) => `Soru ${i + 1}: ${answer.text} (${answer.isCorrect ? "Doğru" : "Yanlış"})`)
            .join("\n");
          const failedTopics = Array.from(
            new Set(nextCollected.filter((answer) => !answer.isCorrect && answer.topic).map((answer) => answer.topic))
          );
          const failedTopicsText = failedTopics.length > 0 ? `\n\nHata Yapılan Konular: ${failedTopics.join(", ")}` : "";
          onSubmitAnswer(`**Seviye Testi Tamamlandı:** ${score}/${totalQuestions} Doğru.\n\n${summaryRows}${failedTopicsText}`);
        } else {
          onSubmitAnswer(`**Quiz Cevabım:** ${label}) ${selectedOption?.text ?? ""}`);
        }
      }
    }, 900);
  };

  const handleNextQuestion = () => {
    if (currentQuestionIdx >= totalQuestions - 1) return;
    setCurrentQuestionIdx((prev) => prev + 1);
    setSelectedId(null);
    setSubmitState("idle");
    setIsCorrectAnswer(false);
  };

  const getOptionStyle = (optionId: string, isCorrect: boolean) => {
    if (submitState !== "done") {
      return selectedId === optionId
        ? "border-emerald-500/40 bg-emerald-500/10"
        : "soft-border hover:bg-surface-muted";
    }
    if (isCorrect) return "border-emerald-500/40 bg-emerald-500/10";
    if (optionId === selectedId && !isCorrect) return "border-amber-500/40 bg-amber-500/10";
    return "soft-border opacity-55";
  };

  const answerIsCorrect = currentQuiz.options.find((option) => option.id === selectedId)?.isCorrect ?? false;

  return (
    <motion.div
      initial={{ opacity: 0, scale: isBaseline ? 0.98 : 1, y: 8 }}
      animate={{ opacity: 1, scale: 1, y: 0 }}
      transition={{ duration: 0.24, ease: "easeOut" }}
      className="mt-4 overflow-hidden rounded-xl border soft-border soft-surface soft-shadow"
    >
      <div className={`border-b px-6 py-4 ${isBaseline ? "border-emerald-500/25 bg-emerald-500/10" : "soft-border"}`}>
        <p className="text-[11px] font-medium uppercase tracking-wide soft-text-muted">
          {isBaseline ? "Seviye belirleme" : currentQuiz.topic || "Quiz"}
        </p>
        <h3 className="mt-1 text-[15px] font-semibold text-foreground">
          {isBaseline
            ? `Test ${currentQuestionIdx + 1}/${totalQuestions}`
            : `Soru ${currentQuestionIdx + 1}${isArray ? `/${totalQuestions}` : ""}`}
        </h3>
      </div>

      <div className="px-6 py-5">
        <div className="prose prose-orka prose-sm mb-5 max-w-none prose-p:my-1.5 prose-p:leading-relaxed">
          <MarkdownRender>{currentQuiz.question}</MarkdownRender>
        </div>

        {currentQuiz.type === "coding" ? (
          <div className="rounded-xl border soft-border soft-muted p-6 text-center">
            <div className="mx-auto mb-4 flex h-11 w-11 items-center justify-center rounded-lg bg-emerald-500/10 text-emerald-700 dark:text-emerald-300">
              <Code2 className="h-5 w-5" />
            </div>
            <h4 className="text-sm font-semibold text-foreground">Kodlama sorusu</h4>
            <p className="mx-auto mt-2 max-w-sm text-xs leading-relaxed soft-text-muted">
              IDE'yi aç, çözümünü yaz ve bitince cevabını hocaya gönder.
            </p>
            <button
              onClick={() => onOpenIDE?.(currentQuiz.question)}
              className="mt-5 rounded-lg bg-foreground px-5 py-2.5 text-sm font-medium text-background transition-opacity hover:opacity-90"
            >
              IDE'yi aç
            </button>
          </div>
        ) : (
          <div className="relative space-y-2.5">
            {currentQuiz.options.map((option, idx) => (
              <button
                key={option.id}
                onClick={() => submitState === "idle" && setSelectedId(option.id)}
                disabled={submitState !== "idle"}
                className={`flex w-full items-start gap-3.5 rounded-lg border px-4 py-3 text-left transition-colors ${getOptionStyle(
                  option.id,
                  option.isCorrect
                )}`}
              >
                <div className="mt-0.5 flex-shrink-0">
                  {submitState === "done" && option.isCorrect ? (
                    <CheckCircle2 className="h-5 w-5 text-emerald-600" />
                  ) : submitState === "done" && option.id === selectedId && !option.isCorrect ? (
                    <XCircle className="h-5 w-5 text-amber-600" />
                  ) : (
                    <div
                      className={`flex h-5 w-5 items-center justify-center rounded-full border-2 ${
                        selectedId === option.id ? "border-emerald-500" : "border-current text-muted-foreground"
                      }`}
                    >
                      {selectedId === option.id && <div className="h-2.5 w-2.5 rounded-full bg-emerald-500" />}
                    </div>
                  )}
                </div>
                <span
                  className={`flex min-w-0 items-baseline gap-1 text-sm ${
                    submitState === "done" && option.isCorrect
                      ? "font-medium text-emerald-700 dark:text-emerald-300"
                      : submitState === "done" && option.id === selectedId && !option.isCorrect
                        ? "font-medium text-amber-700 dark:text-amber-300"
                        : "text-foreground"
                  }`}
                >
                  <span className="font-semibold">{OPTION_LABELS[idx]})</span>
                  <span className="quiz-option-inline flex-1">
                    <MarkdownRender inline>{option.text}</MarkdownRender>
                  </span>
                </span>
              </button>
            ))}
            {submitState === "evaluating" && <div className="absolute inset-0" />}
          </div>
        )}

        <AnimatePresence>
          {submitState === "done" && isCorrectAnswer && !isBaseline && (
            <motion.div
              initial={{ opacity: 0, y: -4 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0 }}
              className="mt-3 flex w-fit items-center gap-2 rounded-lg border border-emerald-500/25 bg-emerald-500/10 px-4 py-2.5"
            >
              <Zap className="h-4 w-4 text-emerald-600" />
              <span className="text-sm font-semibold text-emerald-700 dark:text-emerald-300">+20 XP</span>
            </motion.div>
          )}
        </AnimatePresence>

        {submitState === "done" && currentQuiz.explanation && (
          <motion.div
            initial={{ opacity: 0, height: 0 }}
            animate={{ opacity: 1, height: "auto" }}
            transition={{ duration: 0.24 }}
            className={`mt-4 rounded-lg border p-4 ${
              answerIsCorrect ? "border-emerald-500/25 bg-emerald-500/10" : "border-amber-500/25 bg-amber-500/10"
            }`}
          >
            <p className="mb-1.5 flex items-center gap-1.5 text-[11px] font-medium uppercase tracking-wide soft-text-muted">
              <Sparkles className="h-3 w-3" />
              Değerlendirme
            </p>
            <div className="prose prose-orka prose-sm max-w-none prose-p:my-1.5 prose-p:leading-relaxed">
              <MarkdownRender>{currentQuiz.explanation}</MarkdownRender>
            </div>
          </motion.div>
        )}
      </div>

      <div className="flex flex-col items-center gap-3 border-t soft-border px-6 py-4">
        {submitState === "idle" ? (
          <>
            {currentQuiz.type !== "coding" && (
              <button
                onClick={handleSubmit}
                disabled={!selectedId}
                className="rounded-full bg-foreground px-8 py-2.5 text-sm font-semibold text-background transition-opacity hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-30"
              >
                Cevabı gönder
              </button>
            )}
            <button
              onClick={() => onSubmitAnswer?.(isBaseline ? "[SKIP_QUIZ_BASELINE]" : "[SKIP_QUIZ]")}
              className="text-[11px] font-medium uppercase tracking-[0.1em] soft-text-muted transition-colors hover:text-foreground"
            >
              {isBaseline ? "Hiç bilmiyorum, en temelden başla" : "Konuyu atla ve geç"}
            </button>
          </>
        ) : submitState === "evaluating" ? (
          <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }} className="flex h-10 items-center gap-2 text-sm font-medium soft-text-muted">
            <Loader2 className="h-4 w-4 animate-spin" />
            <span>Cevabın değerlendiriliyor...</span>
          </motion.div>
        ) : isArray && currentQuestionIdx < totalQuestions - 1 ? (
          <button
            onClick={handleNextQuestion}
            className="flex items-center gap-2 rounded-full bg-emerald-500/10 px-6 py-2 text-sm font-semibold text-emerald-700 transition-colors hover:bg-emerald-500/15 dark:text-emerald-300"
          >
            Sıradaki soru
            <ChevronRight className="h-4 w-4" />
          </button>
        ) : (
          <motion.div initial={{ opacity: 0, y: 5 }} animate={{ opacity: 1, y: 0 }} className="flex h-10 items-center gap-2 text-sm font-medium text-emerald-700 dark:text-emerald-300">
            <div className="h-1.5 w-1.5 rounded-full bg-emerald-500" />
            İzlenim kaydedildi
          </motion.div>
        )}
      </div>
    </motion.div>
  );
}
