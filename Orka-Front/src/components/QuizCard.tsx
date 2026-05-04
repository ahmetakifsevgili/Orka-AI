/*
 * Design: Academic quiz card with title, radio options (A/B/C/D),
 * green check for correct, Previous/Next navigation.
 * Matches reference image styling.
 */

import { useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { CheckCircle2, XCircle, ChevronLeft, ChevronRight, Loader2, Sparkles, Zap } from "lucide-react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import type { QuizData, QuizAttempt } from "@/lib/types";
import { useQuizHistory } from "@/contexts/QuizHistoryContext";
import { QuizAPI } from "@/services/api";

interface QuizCardProps {
  quiz: QuizData | QuizData[];
  messageId: string;
  topicId?: string;
  sessionId?: string;
  /** Kullanıcı cevabı gönderince çağrılır; ChatPanel bunu backend'e iletir. */
  onSubmitAnswer?: (formattedAnswer: string) => void;
  isBaseline?: boolean;
  onOpenIDE?: (question?: string) => void;
  onOpenWiki?: (topicId: string) => void;
}

const OPTION_LABELS = ["A", "B", "C", "D", "E", "F"];
const isGuid = (value?: string) =>
  Boolean(value && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value));
const stableQuestionHash = (quiz: QuizData) =>
  quiz.questionHash ??
  `${quiz.question}|${quiz.skillTag ?? quiz.topicPath ?? quiz.topic ?? "unknown"}|${quiz.difficulty ?? "orta"}`
    .toLowerCase()
    .replace(/\s+/g, " ")
    .slice(0, 180);

export default function QuizCard({ quiz, messageId, topicId, sessionId, onSubmitAnswer, onOpenIDE, onOpenWiki, isBaseline = false }: QuizCardProps) {
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
  const [recordError, setRecordError] = useState<string | null>(null);
  const [quizRunId] = useState(() => {
    const existing = quizArray.find((q) => isGuid(q.quizRunId))?.quizRunId;
    return existing ?? crypto.randomUUID();
  });

  // Track answers for all questions
  const [collectedAnswers, setCollectedAnswers] = useState<Array<{
    question: string;
    text: string;
    isCorrect: boolean;
    topic?: string;
    skillTag?: string;
    topicPath?: string;
    difficulty?: string;
    cognitiveType?: string;
    questionHash?: string;
  }>>([]);

  const { addQuizAttempt } = useQuizHistory();

  const currentQuiz = quizArray[currentQuestionIdx];
  const [confirmingZeroStart, setConfirmingZeroStart] = useState(false);

  const handleStartFromZero = () => {
    if (!confirmingZeroStart) {
      setConfirmingZeroStart(true);
      return;
    }
    if (!onSubmitAnswer) return;

    const totalQ = quizArray.length;
    const skillSet = new Set<string>();
    const topicSet = new Set<string>();

    const summaryRows = quizArray
      .map((q, i) => {
        const skill = q.skillTag ?? q.topicPath ?? q.topic ?? "unknown";
        if (skill !== "unknown") skillSet.add(skill);
        if (q.topic) topicSet.add(q.topic);
        return `Soru ${i + 1}: SIFIRDAN_BAŞLAT (Yanlış) | Beceri: ${skill} | Zorluk: ${q.difficulty ?? "unknown"} | Tip: ${q.cognitiveType ?? "unknown"}`;
      })
      .join("\n");

    const failedSkillsStr =
      skillSet.size > 0 ? `\n\nHata Yapılan Beceriler: ${Array.from(skillSet).join(", ")}` : "";
    const failedTopicsStr =
      topicSet.size > 0 ? `\n\nHata Yapılan Konular: ${Array.from(topicSet).join(", ")}` : "";

    const formatted = `**Seviye Testi Tamamlandı:** 0/${totalQ} Doğru.\n\n${summaryRows}${failedSkillsStr}${failedTopicsStr}`;
    onSubmitAnswer(formatted);
  };

  const handleSubmit = () => {
    if (!selectedId) return;
    setRecordError(null);
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
          quizRunId,
          questionId: currentQuiz.questionId ?? `${messageId}-${currentQuestionIdx}`,
          topicId,
          sessionId,
          question: currentQuiz.question,
          selectedOptionId: `${label}) ${selectedOption?.text ?? selectedId}`,
          isCorrect: selectedOption?.isCorrect ?? false,
          explanation: currentQuiz.explanation,
          skillTag: currentQuiz.skillTag ?? currentQuiz.topic,
          topicPath: currentQuiz.topicPath ?? currentQuiz.topic,
          difficulty: currentQuiz.difficulty,
          cognitiveType: currentQuiz.cognitiveType,
          questionHash: stableQuestionHash(currentQuiz),
          sourceRefsJson: currentQuiz.sourceRefs ? JSON.stringify(currentQuiz.sourceRefs) : undefined,
          timestamp: new Date(),
        };

        addQuizAttempt(attempt);

        QuizAPI.recordAttempt({
          messageId: attempt.messageId,
          quizRunId: attempt.quizRunId,
          questionId: attempt.questionId,
          topicId: attempt.topicId,
          sessionId: attempt.sessionId,
          question: attempt.question,
          selectedOptionId: attempt.selectedOptionId,
          isCorrect: attempt.isCorrect,
          explanation: attempt.explanation,
          skillTag: attempt.skillTag,
          topicPath: attempt.topicPath,
          difficulty: attempt.difficulty,
          cognitiveType: attempt.cognitiveType,
          questionHash: attempt.questionHash,
          sourceRefsJson: attempt.sourceRefsJson,
        }).catch(() => setRecordError("Quiz kaydı şu an backend'e ulaşamadı; cevap akışı devam ediyor."));

        // Save answer to collected array
        const newCollected = [...collectedAnswers, {
            question: currentQuiz.question,
            text: `${label}) ${selectedOption?.text ?? ""}`,
            isCorrect: attempt.isCorrect,
            topic: currentQuiz.topic,
            skillTag: attempt.skillTag,
            topicPath: attempt.topicPath,
            difficulty: attempt.difficulty,
            cognitiveType: attempt.cognitiveType,
            questionHash: attempt.questionHash,
        }];
        setCollectedAnswers(newCollected);

        // If it's a single quiz, or we're on the last question, submit to backend chat
        if (!isArray || currentQuestionIdx === totalQuestions - 1) {
            if (onSubmitAnswer) {
              if (isArray) {
                 // Submit aggregated summary of test
                 const score = newCollected.filter(a => a.isCorrect).length;
                 const summaryRows = newCollected.map((a, i) =>
                   `Soru ${i+1}: ${a.text} (${a.isCorrect ? 'Doğru' : 'Yanlış'}) | Beceri: ${a.skillTag ?? a.topicPath ?? a.topic ?? 'unknown'} | Zorluk: ${a.difficulty ?? 'unknown'} | Tip: ${a.cognitiveType ?? 'unknown'}`
                 ).join("\n");

                 const failedTopicsList = Array.from(new Set(newCollected.filter(a => !a.isCorrect && a.topic).map(a => a.topic)));
                 const failedSkillsList = Array.from(new Set(newCollected.filter(a => !a.isCorrect).map(a => a.skillTag ?? a.topicPath).filter(Boolean)));
                 const failedSkillsStr = failedSkillsList.length > 0 ? `\nHata Yapılan Beceriler: ${failedSkillsList.join(", ")}` : "";
                 const failedTopicsStr = failedTopicsList.length > 0 ? `\n\nHata Yapılan Konular: ${failedTopicsList.join(", ")}` : "";

                 const formatted = `**Seviye Testi Tamamlandı:** ${score}/${totalQuestions} Doğru.\n\n${summaryRows}${failedSkillsStr}${failedTopicsStr}`;
                 onSubmitAnswer(formatted);
              } else {
                 const formatted = `**Quiz Cevabım:** ${label}) ${selectedOption?.text ?? ""}\nSonuç: ${attempt.isCorrect ? "Doğru" : "Yanlış"}\nBeceri: ${attempt.skillTag ?? "unknown"}\nKonu Yolu: ${attempt.topicPath ?? currentQuiz.topic ?? "unknown"}\nZorluk: ${attempt.difficulty ?? "unknown"}\nTip: ${attempt.cognitiveType ?? "unknown"}`;
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
        ? "border-[#9ec7d9] bg-[#dcecf3]/70 shadow-sm"
        : "border-[#526d82]/14 hover:border-[#9ec7d9]/70 hover:bg-white/80";
    }
    if (isCorrect) return "border-[#8fb7a2]/55 bg-[#f2faf5]/90";
    if (optionId === selectedId && !isCorrect) return "border-[#e8c46f]/60 bg-[#fff8ee]/95 animate-shake";
    return "border-[#526d82]/10 opacity-55";
  };

  return (
    <motion.div
      initial={{ opacity: 0, scale: isBaseline ? 0.95 : 1, y: 8 }}
      animate={{ opacity: 1, scale: 1, y: 0 }}
      transition={{ duration: 0.3, ease: "easeOut" }}
      className={`rounded-[1.5rem] border mt-4 overflow-hidden shadow-[0_18px_48px_rgba(66,91,112,0.12)] ${
        isBaseline
          ? "bg-[#f2faf5]/90 border-[#8fb7a2]/35"
          : "bg-white/76 border-[#526d82]/14 backdrop-blur-xl"
      }`}
    >
      <div className={`px-6 pt-5 pb-4 border-b ${isBaseline ? "border-[#8fb7a2]/25 bg-[#f2faf5]/80" : "border-[#526d82]/12"}`}>
        <h3 className={`text-[15px] font-semibold ${isBaseline ? "text-[#47725d]" : "text-[#172033]"}`}>
          {isBaseline
            ? `Seviyeni Belirliyoruz (Test ${currentQuestionIdx + 1}/${totalQuestions})`
            : (currentQuiz.topic || "Knowledge Check") + ` — Quiz ${currentQuestionIdx + 1}${isArray ? `/${totalQuestions}` : ''}`
          }
        </h3>
      </div>

      <div className="px-6 py-5">
        <div className="text-sm text-[#172033] leading-relaxed mb-5 prose prose-sm max-w-none prose-p:my-1 prose-img:rounded-xl prose-img:border prose-img:border-[#dcecf3] prose-img:my-3 prose-strong:text-[#172033] prose-code:text-[#2d5870] prose-code:bg-[#eaf4f7] prose-code:px-1 prose-code:rounded">
          <ReactMarkdown
            remarkPlugins={[remarkGfm]}
            components={{
              img({ src, alt }) {
                return (
                  <img
                    src={src}
                    alt={alt || ""}
                    loading="lazy"
                    className="my-3 rounded-xl border border-[#dcecf3] max-w-full bg-[#f7f9fa]"
                    onError={(e) => {
                      const target = e.currentTarget;
                      target.onerror = null;
                      target.style.display = "none";
                      const fallback = document.createElement("div");
                      fallback.className = "my-3 rounded-xl border border-[#dcecf3] bg-[#f7f9fa] px-4 py-3 text-xs text-[#98a2b3] flex items-center gap-2";
                      fallback.innerHTML = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="8.5" cy="8.5" r="1.5"/><path d="m21 15-5-5L5 21"/></svg> Görsel yüklenemedi`;
                      target.parentNode?.insertBefore(fallback, target.nextSibling);
                    }}
                  />
                );
              },
            }}
          >
            {currentQuiz.question ?? ""}
          </ReactMarkdown>
        </div>

        <div className="space-y-2.5 relative">
          {currentQuiz.type === 'coding' ? (
            <div className="flex flex-col items-center justify-center p-8 border border-[#526d82]/14 rounded-[1.25rem] bg-[#f7fbff]/82 text-center">
                <div className="w-12 h-12 rounded-full bg-[#ddebe3]/85 flex items-center justify-center mb-4">
                    <span className="text-xl">&lt;/&gt;</span>
                </div>
                <h4 className="text-sm font-semibold text-[#172033] mb-2">Bu Bir Kodlama Sorusu</h4>
                <p className="text-xs text-[#667085] max-w-[250px] mb-6 leading-relaxed">
                    Sağ taraftaki İnteraktif IDE'yi kullanarak kodunuzu yazın ve algoritmayı çözün. Bitirdiğinizde IDE içindeki <strong>"Hocaya Gönder"</strong> butonuna basarak cevabınızı iletebilirsiniz.
                </p>
                <button
                    onClick={() => onOpenIDE?.(currentQuiz.question)}
                    className="flex items-center gap-2 px-6 py-2.5 bg-[#172033] hover:bg-[#24324d] text-white rounded-lg font-medium text-sm transition-colors shadow-lg shadow-emerald-900/20"
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
                      <CheckCircle2 className="w-5 h-5 text-[#47725d]" />
                    </motion.div>
                  ) : submitState === "done" && option.id === selectedId && !option.isCorrect ? (
                    <motion.div initial={{ scale: 0 }} animate={{ scale: 1 }}>
                      <XCircle className="w-5 h-5 text-amber-400" />
                    </motion.div>
                  ) : (
                    <div
                      className={`w-5 h-5 rounded-full border-2 flex items-center justify-center transition-colors duration-150 ${
                        selectedId === option.id
                          ? (isBaseline ? "border-emerald-500" : "border-[#2d5870]")
                          : "border-[#98a2b3]"
                      }`}
                    >
                      {selectedId === option.id && (
                        <div className={`w-2.5 h-2.5 rounded-full ${isBaseline ? "bg-emerald-500" : "bg-[#2d5870]"}`} />
                      )}
                    </div>
                  )}
                </div>

                <span
                  className={`text-sm tracking-wide ${
                    submitState === "done" && option.isCorrect
                      ? "text-[#47725d] font-medium"
                      : submitState === "done" && option.id === selectedId && !option.isCorrect
                        ? "text-[#9a6b24] font-medium"
                        : "text-[#344054]"
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
              className="mt-3 flex items-center gap-2 px-4 py-2.5 rounded-xl bg-[#ddebe3]/85 border border-emerald-700/30 w-fit"
            >
              <Zap className="w-4 h-4 text-[#47725d] fill-emerald-400" />
              <span className="text-sm font-bold text-[#47725d]">+20 XP Kazandınız!</span>
              <span className="text-[10px] text-[#6f947f] font-medium uppercase tracking-wider">Tebrikler</span>
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
                   ? "bg-[#f2faf5]/90 border-[#8fb7a2]/30"
                   : "bg-[#fff8ee]/95 border-[#e8c46f]/35"
            }`}
          >
            <p className="text-[11px] font-medium text-[#667085] uppercase tracking-wider mb-1.5 flex items-center gap-1.5">
              <Sparkles className="w-3 h-3" /> AI Değerlendirmesi
            </p>
            <div className="text-sm text-[#344054] leading-relaxed prose prose-sm max-w-none prose-p:my-1 prose-img:rounded-lg prose-img:my-2 prose-strong:text-[#344054]">
              <ReactMarkdown
                remarkPlugins={[remarkGfm]}
                components={{
                  img({ src, alt }) {
                    return (
                      <img
                        src={src}
                        alt={alt || ""}
                        loading="lazy"
                        className="my-2 rounded-lg border border-[#dcecf3] max-w-full bg-[#f7f9fa]"
                        onError={(e) => {
                          const target = e.currentTarget;
                          target.onerror = null;
                          target.style.display = "none";
                          const fallback = document.createElement("div");
                          fallback.className = "my-2 rounded-lg border border-[#dcecf3] bg-[#f7f9fa] px-4 py-3 text-xs text-[#98a2b3] flex items-center gap-2";
                          fallback.innerHTML = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="8.5" cy="8.5" r="1.5"/><path d="m21 15-5-5L5 21"/></svg> Görsel yüklenemedi`;
                          target.parentNode?.insertBefore(fallback, target.nextSibling);
                        }}
                      />
                    );
                  },
                }}
              >
                {currentQuiz.explanation ?? ""}
              </ReactMarkdown>
            </div>
            {!isCorrectAnswer && topicId && onOpenWiki && (
              <button
                type="button"
                onClick={() => onOpenWiki(topicId)}
                className="mt-3 inline-flex items-center gap-2 rounded-full border border-[#e8c46f]/35 bg-[#fff8ee] px-3 py-1.5 text-[11px] font-extrabold text-[#8a641f] transition hover:-translate-y-0.5 hover:bg-[#f7f4ec]"
              >
                Bu konuyu sağ Wiki'de aç
              </button>
            )}
          </motion.div>
        )}
        {recordError && (
          <p className="mt-3 text-[11px] text-amber-400">
            {recordError}
          </p>
        )}
      </div>

      <div className={`px-6 py-4 border-t flex flex-col items-center gap-3 transition-colors ${
          submitState === "idle" ? "border-[#526d82]/12" : "border-transparent bg-white/45"
      }`}>
        {submitState === "idle" ? (
          <>
            {currentQuiz.type !== "coding" && (
                <button
                    onClick={handleSubmit}
                    disabled={!selectedId}
                    className={`px-8 py-2.5 rounded-full text-sm font-semibold transition-all duration-200 disabled:opacity-30 disabled:cursor-not-allowed shadow-lg ${
                    isBaseline
                        ? "bg-[#8fb7a2] text-[#173224] hover:bg-[#7fac96] shadow-[#8fb7a2]/20"
                        : "bg-[#172033] text-white hover:bg-[#24324d] shadow-slate-900/10"
                    }`}
                >
                    Cevabı Gönder
                </button>
            )}

            {!isBaseline && (
                <button
                    onClick={() => onSubmitAnswer?.("[SKIP_QUIZ]")}
                    className="text-[11px] text-[#667085] hover:text-[#344054] transition-colors uppercase tracking-[0.1em] font-medium"
                >
                    Konuyu Atla ve Geç →
                </button>
            )}

            {isBaseline && collectedAnswers.length === 0 && (
                <button
                    onClick={handleStartFromZero}
                    onBlur={() => setConfirmingZeroStart(false)}
                    className={`text-[11px] transition-colors uppercase tracking-[0.1em] font-medium ${
                      confirmingZeroStart
                        ? "text-amber-400 hover:text-[#9a6b24]"
                        : "text-[#667085] hover:text-[#47725d]"
                    }`}
                    title="Hiç soru çözmeden, en temel seviyeden müfredatı oluştur"
                >
                    {confirmingZeroStart
                      ? "Eminim, sıfırdan başlat ↵"
                      : "Hiç bilmiyorum, sıfırdan başlat →"}
                </button>
            )}
          </>
        ) : submitState === "evaluating" ? (
          <motion.div
            initial={{ opacity: 0, scale: 0.95 }}
            animate={{ opacity: 1, scale: 1 }}
            className="flex items-center gap-2 text-sm text-[#47725d] font-medium h-10"
          >
            <Loader2 className="w-4 h-4 animate-spin" />
            <span>AI Cevabını Değerlendiriyor...</span>
          </motion.div>
        ) : (
          isArray && currentQuestionIdx < totalQuestions - 1 ? (
             <button
                onClick={handleNextQuestion}
                className="flex items-center gap-2 px-6 py-2 rounded-full text-sm font-semibold bg-emerald-900/60 text-[#47725d] hover:bg-emerald-800/60 border border-emerald-700/50 transition-all duration-200 shadow-lg shadow-emerald-900/20"
             >
                Sıradaki Soru <ChevronRight className="w-4 h-4" />
             </button>
          ) : (
             <motion.div
               initial={{ opacity: 0, y: 5 }}
               animate={{ opacity: 1, y: 0 }}
               className="text-sm text-emerald-500/80 font-medium flex items-center gap-2 h-10"
             >
                <div className="w-1.5 h-1.5 rounded-full bg-[#8fb7a2] animate-pulse" />
                ✓ İzlenim Kaydedildi
             </motion.div>
          )
        )}
      </div>
    </motion.div>
  );
}
