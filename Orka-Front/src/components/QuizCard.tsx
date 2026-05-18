import { useEffect, useMemo, useState } from "react";
import { motion } from "framer-motion";
import { BookOpen, CheckCircle2, XCircle, ChevronRight, Loader2, Sparkles, Code2, MessageSquareText } from "lucide-react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { safeMarkdownComponents, safeMarkdownUrlTransform } from "@/lib/contentSafety";
import type { AdaptiveAssessmentNextItem, LearningSignalConfidenceDto, MisconceptionSignalDto, PlanDiagnosticMeta, QuizAttempt, QuizData, QuizResultLearningImpactDto, RemediationSeedDto } from "@/lib/types";
import { useQuizHistory } from "@/contexts/QuizHistoryContext";
import { QuizAPI, type QuizAttemptRecordResponse } from "@/services/api";

interface QuizCardProps {
  quiz: QuizData | QuizData[];
  messageId: string;
  topicId?: string;
  sessionId?: string;
  planDiagnostic?: PlanDiagnosticMeta;
  onPlanComplete?: (completion: {
    planGenerated: boolean;
    generatedPlanRootTopicId?: string;
    generatedTopicIds?: string[];
    message?: string;
    score?: number;
    total?: number;
    skipped?: boolean;
  }) => void;
  isBaseline?: boolean;
  onOpenWiki?: (topicId: string) => void;
  onOpenIDE?: (question?: string) => void;
  adaptiveAssessment?: {
    sessionId: string;
    decisionId: string;
    onResult?: (result: AdaptiveAssessmentNextItem) => void;
  };
}

const OPTION_LABELS = ["A", "B", "C", "D", "E", "F"];
const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

type SubmittedAnswer = {
  isCorrect: boolean;
  isVerified: boolean;
  result: string;
  explanation?: string | null;
  skill?: string;
  learningImpact?: QuizResultLearningImpactDto | null;
};

const stableQuestionHash = (quiz: QuizData) =>
  quiz.questionHash ??
  `${quiz.question}|${quiz.skillTag ?? quiz.topicPath ?? quiz.topic ?? "unknown"}|${quiz.difficulty ?? "orta"}`
    .toLowerCase()
    .replace(/\s+/g, " ")
    .slice(0, 180);

const confidenceStatusLabel = (confidence?: LearningSignalConfidenceDto | null, fallback?: string | null) => {
  const status = (confidence?.status ?? fallback ?? "").toLowerCase();
  if (status === "usable") return "Kanıt güvenilir";
  if (status === "observed_only") return "Kanıt düşük";
  if (status === "ignored") return "Plan girdisi yapılmadı";
  return "Kanıt izleniyor";
};

const buildSourceRefs = (quiz: QuizData) => {
  const base =
    quiz.sourceRefs && typeof quiz.sourceRefs === "object" && !Array.isArray(quiz.sourceRefs)
      ? { ...(quiz.sourceRefs as Record<string, unknown>) }
      : quiz.sourceRefs
        ? { rawSourceRefs: quiz.sourceRefs }
        : {};

  return {
    ...base,
    assessmentItemId: quiz.assessmentItemId,
    assessmentItemKey: quiz.assessmentItemKey,
    conceptKey: quiz.conceptKey,
    conceptTag: quiz.conceptTag,
    cognitiveSkill: quiz.cognitiveSkill,
    misconceptionTarget: quiz.misconceptionTarget,
    evidenceExpected: quiz.evidenceExpected,
    scoringRule: quiz.scoringRule,
    learningOutcomeIds: quiz.learningOutcomeIds,
    knowledgeTracingStateId: quiz.knowledgeTracingStateId,
    masteryProbability: quiz.masteryProbability,
    itemQualityStatus: quiz.itemQualityStatus,
  };
};

export default function QuizCard({
  quiz,
  messageId,
  topicId,
  sessionId,
  planDiagnostic,
  onPlanComplete,
  onOpenWiki,
  onOpenIDE,
  adaptiveAssessment,
  isBaseline = false,
}: QuizCardProps) {
  const quizArray = useMemo(() => {
    const base = Array.isArray(quiz) ? [...quiz] : [quiz];
    return base.sort((a, b) => (a.type === "coding" ? 1 : 0) - (b.type === "coding" ? 1 : 0));
  }, [quiz]);

  const totalQuestions = quizArray.length;
  const currentQuiz = quizArray[Math.min(totalQuestions - 1, 0)];
  const [currentQuestionIdx, setCurrentQuestionIdx] = useState(0);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [submitState, setSubmitState] = useState<"idle" | "evaluating" | "done">("idle");
  const [recordError, setRecordError] = useState<string | null>(null);
  const [completionNote, setCompletionNote] = useState<string | null>(null);
  const [recoveryHint, setRecoveryHint] = useState<string | null>(null);
  const [misconceptionSignal, setMisconceptionSignal] = useState<MisconceptionSignalDto | null>(null);
  const [signalConfidence, setSignalConfidence] = useState<LearningSignalConfidenceDto | null>(null);
  const [remediationSeed, setRemediationSeed] = useState<RemediationSeedDto | null>(null);
  const [confirmingZeroStart, setConfirmingZeroStart] = useState(false);
  const [answers, setAnswers] = useState<SubmittedAnswer[]>([]);
  const [questionStartedAt, setQuestionStartedAt] = useState(() => Date.now());
  const { addQuizAttempt } = useQuizHistory();

  const activeQuiz = quizArray[currentQuestionIdx] ?? currentQuiz;
  const quizRunId = planDiagnostic?.quizRunId ?? quizArray.find((item) => item.quizRunId && GUID_RE.test(item.quizRunId))?.quizRunId;
  const selectedOption = activeQuiz.options.find((option) => option.id === selectedId);
  const submittedAnswer = answers[currentQuestionIdx];
  const isCorrectAnswer = submittedAnswer?.isCorrect ?? false;
  const hasVerifiedResult = Boolean(submittedAnswer?.isVerified);
  const isLastQuestion = currentQuestionIdx >= totalQuestions - 1;
  const recoveryTopicId = planDiagnostic?.topicId ?? topicId;
  const recoveryTopicTitle = activeQuiz.topicPath ?? activeQuiz.topic ?? activeQuiz.skillTag ?? "bu konu";

  useEffect(() => {
    setQuestionStartedAt(Date.now());
  }, [currentQuestionIdx, activeQuiz.questionId, activeQuiz.question]);

  const buildAttempt = (): QuizAttempt | null => {
    if (!selectedOption || !selectedId) return null;
    const idx = activeQuiz.options.findIndex((option) => option.id === selectedId);
    const label = OPTION_LABELS[idx] ?? "?";
    const sourceRefs = buildSourceRefs(activeQuiz);
    return {
      id: `qa-${Date.now()}`,
      messageId,
      quizRunId,
      questionId: activeQuiz.questionId ?? `${messageId}-${currentQuestionIdx}`,
      topicId: planDiagnostic?.topicId ?? topicId,
      sessionId,
      question: activeQuiz.question,
      selectedOptionId: `${label}) ${selectedOption.text}`,
      skillTag: activeQuiz.skillTag ?? activeQuiz.topic,
      assessmentItemId: activeQuiz.assessmentItemId,
      conceptKey: activeQuiz.conceptKey,
      conceptTag: activeQuiz.conceptTag,
      cognitiveSkill: activeQuiz.cognitiveSkill,
      misconceptionTarget: activeQuiz.misconceptionTarget,
      evidenceExpected: activeQuiz.evidenceExpected,
      scoringRule: activeQuiz.scoringRule,
      learningOutcomeIdsJson: activeQuiz.learningOutcomeIds ? JSON.stringify(activeQuiz.learningOutcomeIds) : undefined,
      knowledgeTracingStateId: activeQuiz.knowledgeTracingStateId,
      masteryProbability: activeQuiz.masteryProbability,
      itemQualityStatus: activeQuiz.itemQualityStatus,
      topicPath: activeQuiz.topicPath ?? activeQuiz.topic,
      difficulty: activeQuiz.difficulty,
      cognitiveType: activeQuiz.cognitiveType,
      questionHash: stableQuestionHash(activeQuiz),
      sourceRefsJson: JSON.stringify(sourceRefs),
      responseTimeMs: Math.max(0, Date.now() - questionStartedAt),
      wasSkipped: false,
      timestamp: new Date(),
    };
  };

  const recordAttempt = async (attempt: QuizAttempt) => {
    const payload = {
      messageId: attempt.messageId,
      quizRunId: attempt.quizRunId,
      questionId: attempt.questionId,
      topicId: attempt.topicId,
      sessionId: attempt.sessionId,
      question: attempt.question,
      selectedOptionId: attempt.selectedOptionId,
      skillTag: attempt.skillTag,
      assessmentItemId: attempt.assessmentItemId,
      conceptKey: attempt.conceptKey,
      conceptTag: attempt.conceptTag,
      cognitiveSkill: attempt.cognitiveSkill,
      misconceptionTarget: attempt.misconceptionTarget,
      evidenceExpected: attempt.evidenceExpected,
      scoringRule: attempt.scoringRule,
      learningOutcomeIdsJson: attempt.learningOutcomeIdsJson,
      topicPath: attempt.topicPath,
      difficulty: attempt.difficulty,
      cognitiveType: attempt.cognitiveType,
      questionHash: attempt.questionHash,
      sourceRefsJson: attempt.sourceRefsJson,
      responseTimeMs: attempt.responseTimeMs,
      wasSkipped: attempt.wasSkipped,
      confidenceSelfRating: attempt.confidenceSelfRating,
    };

    if (adaptiveAssessment) {
      return await QuizAPI.answerAdaptive(adaptiveAssessment.sessionId, {
        ...payload,
        decisionId: adaptiveAssessment.decisionId,
      });
    }

    if (planDiagnostic) {
      return await QuizAPI.recordPlanDiagnosticAttempt(planDiagnostic.planRequestId, payload);
    } else {
      return await QuizAPI.recordAttempt(payload);
    }
  };

  const toSubmittedAnswer = (
    attempt: QuizAttempt,
    response: QuizAttemptRecordResponse | AdaptiveAssessmentNextItem | { learningImpact?: QuizResultLearningImpactDto | null } | null
  ): SubmittedAnswer => {
    const learningImpact =
      response && "learningImpact" in response
        ? response.learningImpact
        : response && "latestLearningImpact" in response
          ? response.latestLearningImpact
          : null;
    const result = (learningImpact?.result ?? "unverified").toLowerCase();
    const isCorrect = result === "correct";
    const isVerified = result === "correct" || result === "wrong" || result === "blank" || result === "partial";
    return {
      isCorrect,
      isVerified,
      result,
      explanation: undefined,
      skill: attempt.skillTag,
      learningImpact,
    };
  };

  const finalizePlanIfNeeded = async (nextAnswers: SubmittedAnswer[]) => {
    if (!planDiagnostic || !isLastQuestion) return;
    const result = await QuizAPI.finalizePlanDiagnostic(planDiagnostic.planRequestId);
    const score = nextAnswers.filter((answer) => answer.isCorrect).length;
    setCompletionNote(
      result.planGenerated
        ? "Plan hazır. Bu quiz chat'e cevap mesajı olarak basılmadı."
        : result.message ?? "Plan üretimi için seviye testi tamamlandı."
    );
    onPlanComplete?.({ ...result, score, total: totalQuestions });
  };

  const handleSubmit = async () => {
    const attempt = buildAttempt();
    if (!attempt) return;

    setRecordError(null);
    setRecoveryHint(null);
    setMisconceptionSignal(null);
    setSignalConfidence(null);
    setRemediationSeed(null);
    setSubmitState("evaluating");
    await wait(350);

    try {
      const attemptResult = await recordAttempt(attempt);
      const submitted = toSubmittedAnswer(attempt, attemptResult);
      if (!submitted.isCorrect && attemptResult && "remediationSeed" in attemptResult) {
        setMisconceptionSignal(attemptResult.misconceptionSignal ?? null);
        setSignalConfidence(attemptResult.learningSignalConfidence ?? null);
        setRemediationSeed(attemptResult.remediationSeed ?? null);
      }
      addQuizAttempt({
        ...attempt,
        isCorrect: submitted.isCorrect,
        explanation: submitted.explanation ?? "",
      });
      const nextAnswers = [...answers, submitted];
      setAnswers(nextAnswers);
      setSubmitState("done");
      if (attemptResult && "isComplete" in attemptResult && adaptiveAssessment?.onResult) {
        adaptiveAssessment.onResult(attemptResult);
        if (attemptResult.isComplete) {
          setCompletionNote("Adaptif pratik tamamlandı. Orka yeni kanıtı mastery hesabına ekledi.");
        }
      }
      await finalizePlanIfNeeded(nextAnswers);
    } catch {
      setSubmitState("done");
      const nextAnswers = [...answers, { isCorrect: false, isVerified: false, result: "unverified", skill: attempt.skillTag }];
      setAnswers(nextAnswers);
      setRecordError("Quiz kaydı backend'e ulaşamadı; cevap ekranda kaldı, chat'e komut basılmadı.");
    }
  };

  const handleNextQuestion = () => {
    if (!isLastQuestion) {
      setCurrentQuestionIdx((prev) => prev + 1);
      setSelectedId(null);
      setSubmitState("idle");
      setRecordError(null);
      setCompletionNote(null);
      setRecoveryHint(null);
      setMisconceptionSignal(null);
      setSignalConfidence(null);
      setRemediationSeed(null);
    }
  };

  const handleStartFromZero = async () => {
    if (!confirmingZeroStart) {
      setConfirmingZeroStart(true);
      return;
    }
    if (!planDiagnostic) return;

    setSubmitState("evaluating");
    setRecordError(null);
    try {
      const result = await QuizAPI.skipPlanDiagnostic(planDiagnostic.planRequestId);
      setSubmitState("done");
      setCompletionNote("Seviye testi atlandı. Orka bunu sahte yanlış cevap gibi kaydetmedi.");
      onPlanComplete?.({ ...result, skipped: true, score: 0, total: totalQuestions });
    } catch {
      setSubmitState("idle");
      setRecordError("Seviye testini atlama isteği backend'e ulaşamadı; istersen normal cevaplayarak devam et.");
    }
  };

  const getOptionStyle = (optionId: string) => {
    if (submitState !== "done") {
      return selectedId === optionId
        ? "border-[#9ec7d9] bg-[#dcecf3]/70 shadow-sm"
        : "border-[#526d82]/14 hover:border-[#9ec7d9]/70 hover:bg-white/80";
    }
    if (optionId === selectedId && isCorrectAnswer) return "border-[#8fb7a2]/55 bg-[#f2faf5]/90";
    if (optionId === selectedId && !hasVerifiedResult) return "border-[#9ec7d9] bg-[#dcecf3]/70 shadow-sm";
    if (optionId === selectedId && !isCorrectAnswer) return "border-[#e8c46f]/60 bg-[#fff8ee]/95";
    return "border-[#526d82]/10 opacity-60";
  };

  return (
    <motion.div
      initial={{ opacity: 0, scale: isBaseline ? 0.97 : 1, y: 8 }}
      animate={{ opacity: 1, scale: 1, y: 0 }}
      transition={{ duration: 0.25, ease: "easeOut" }}
      className={`mt-4 overflow-hidden rounded-[1.5rem] border shadow-[0_18px_48px_rgba(66,91,112,0.12)] ${
        isBaseline ? "border-[#8fb7a2]/35 bg-[#f2faf5]/90" : "border-[#526d82]/14 bg-white/76 backdrop-blur-xl"
      }`}
    >
      <div className={`border-b px-6 pb-4 pt-5 ${isBaseline ? "border-[#8fb7a2]/25 bg-[#f2faf5]/80" : "border-[#526d82]/12"}`}>
        <p className={`text-[11px] font-black uppercase tracking-[0.14em] ${isBaseline ? "text-[#47725d]" : "text-[#667085]"}`}>
          {adaptiveAssessment ? "Adaptif pratik" : planDiagnostic ? "Seviye testi" : "Quiz"}
        </p>
        <h3 className={`mt-1 text-[15px] font-semibold ${isBaseline ? "text-[#47725d]" : "text-[#172033]"}`}>
          {activeQuiz.topic || planDiagnostic?.topicTitle || "Bilgi kontrolü"} · Soru {currentQuestionIdx + 1}/{totalQuestions}
        </h3>
      </div>

      <div className="px-6 py-5">
        <div className="mb-5 max-w-none text-sm leading-relaxed text-[#172033] prose prose-sm prose-p:my-1 prose-strong:text-[#172033] prose-code:rounded prose-code:bg-[#eaf4f7] prose-code:px-1 prose-code:text-[#2d5870]">
          <ReactMarkdown remarkPlugins={[remarkGfm]} urlTransform={safeMarkdownUrlTransform} components={safeMarkdownComponents}>{activeQuiz.question ?? ""}</ReactMarkdown>
        </div>

        {activeQuiz.type === "coding" ? (
          <div className="flex flex-col items-center justify-center rounded-[1.25rem] border border-[#526d82]/14 bg-[#f7fbff]/82 p-8 text-center">
            <div className="mb-4 flex h-12 w-12 items-center justify-center rounded-full bg-[#ddebe3]/85">
              <Code2 className="h-5 w-5 text-[#47725d]" />
            </div>
            <h4 className="mb-2 text-sm font-semibold text-[#172033]">Kod sorusu</h4>
            <p className="mb-6 max-w-[300px] text-xs leading-relaxed text-[#667085]">
              Bu soru için Orka IDE/sandbox akışını kullan. Kod sonucunu Tutor'a gönderdiğinde hata da öğrenme sinyali olur.
            </p>
            <button
              onClick={() => onOpenIDE?.(activeQuiz.question)}
              className="rounded-lg bg-[#172033] px-6 py-2.5 text-sm font-medium text-white shadow-lg transition-colors hover:bg-[#24324d]"
            >
              Orka IDE'yi aç
            </button>
          </div>
        ) : (
          <div className="space-y-2.5">
            {activeQuiz.options.map((option, idx) => (
              <button
                key={option.id}
                onClick={() => submitState === "idle" && setSelectedId(option.id)}
                disabled={submitState !== "idle"}
                className={`flex w-full items-center gap-3.5 rounded-lg border px-4 py-3 text-left transition-all duration-200 ${getOptionStyle(option.id)}`}
              >
                <span className="flex-shrink-0">
                  {submitState === "done" && option.id === selectedId && isCorrectAnswer ? (
                    <CheckCircle2 className="h-5 w-5 text-[#47725d]" />
                  ) : submitState === "done" && option.id === selectedId && hasVerifiedResult && !isCorrectAnswer ? (
                    <XCircle className="h-5 w-5 text-amber-500" />
                  ) : (
                    <span
                      className={`flex h-5 w-5 items-center justify-center rounded-full border-2 ${
                        selectedId === option.id ? "border-[#2d5870]" : "border-[#98a2b3]"
                      }`}
                    >
                      {selectedId === option.id && <span className="h-2.5 w-2.5 rounded-full bg-[#2d5870]" />}
                    </span>
                  )}
                </span>
                <span className="text-sm tracking-wide text-[#344054]">
                  {OPTION_LABELS[idx]}) {option.text}
                </span>
              </button>
            ))}
          </div>
        )}

        {submitState === "done" && selectedOption && (
          <div
            className={`mt-4 rounded-lg border p-4 ${
              isCorrectAnswer ? "border-[#8fb7a2]/30 bg-[#f2faf5]/90" : "border-[#e8c46f]/35 bg-[#fff8ee]/95"
            }`}
          >
            <p className="mb-1.5 flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-wider text-[#667085]">
              <Sparkles className="h-3 w-3" />
              {isCorrectAnswer ? "Doğru cevap" : hasVerifiedResult ? "Tekrar edilmesi iyi olur" : "Güvenli pratik olarak kaydedildi"}
            </p>
            <p className="text-sm leading-relaxed text-[#344054]">
              {isCorrectAnswer
                ? "Bu kavramı doğru bağlamda kullandın."
                : !hasVerifiedResult
                  ? "Bu eski quiz akışı server tarafından doğrulanamadı. Orka bunu mastery kanıtı gibi kullanmadı; gerekirse server tarafından üretilen yeni bir mikro quiz ile ölçebilir."
                  : "Bu cevap doğru değil. Orka bunu öğrenme baskısına çevirebilir; panik değil, bir sonraki küçük tekrar noktası."}
            </p>
            {submittedAnswer?.explanation && (
              <div className="mt-3 text-sm leading-relaxed text-[#344054] prose prose-sm max-w-none prose-p:my-1">
                <ReactMarkdown remarkPlugins={[remarkGfm]} urlTransform={safeMarkdownUrlTransform} components={safeMarkdownComponents}>{submittedAnswer.explanation}</ReactMarkdown>
              </div>
            )}
            {hasVerifiedResult && !isCorrectAnswer && (
              <div className="mt-4 rounded-xl border border-[#e8c46f]/28 bg-white/58 p-3">
                <p className="text-[10px] font-black uppercase tracking-[0.14em] text-[#8a641f]">
                  Toparlanma adımı
                </p>
                <p className="mt-1 text-xs leading-5 text-[#667085]">
                  Yanlış cevap kaydedildi; ilerleme davranışı değişmeden buradan kısa bir telafi adımı seçebilirsin.
                </p>
                {(misconceptionSignal || remediationSeed) && (
                  <div className="mt-3 rounded-lg border border-[#e8c46f]/24 bg-[#fff8ee]/62 px-3 py-2 text-[11px] leading-5 text-[#667085]">
                    <p className="font-black text-[#8a641f]">
                      Yanılgı sinyali: {misconceptionSignal?.userSafeLabel ?? remediationSeed?.userSafeMisconceptionLabel ?? "güvenli şekilde izlendi"}
                    </p>
                    <p className="mt-1">
                      {remediationSeed?.reason ?? misconceptionSignal?.safeHint ?? "Bu sinyal kesin teşhis değil; kısa telafi için kullanılabilir."}
                    </p>
                    <p className="mt-1 font-bold text-[#8a641f]">
                      Kanıt durumu: {confidenceStatusLabel(signalConfidence, remediationSeed?.confidenceStatus ?? misconceptionSignal?.confidenceStatus)}
                    </p>
                  </div>
                )}
                <div className="mt-3 flex flex-wrap gap-2">
                  <button
                    type="button"
                    onClick={() =>
                      setRecoveryHint(
                        `${recoveryTopicTitle} konusunda şu quiz sorusunu anlamadım: "${activeQuiz.question}". Cevabımı ve doğru mantığı kısa adımlarla açıklar mısın?`
                      )
                    }
                    className="inline-flex items-center gap-1.5 rounded-lg bg-[#172033] px-3 py-2 text-[11px] font-black text-white transition hover:bg-[#243044] focus:outline-none focus:ring-2 focus:ring-[#9ec7d9]"
                  >
                    <MessageSquareText className="h-3.5 w-3.5" />
                    Tutor’a sor
                  </button>
                  {recoveryTopicId && onOpenWiki ? (
                    <button
                      type="button"
                      onClick={() => onOpenWiki(recoveryTopicId)}
                      className="inline-flex items-center gap-1.5 rounded-lg border border-[#526d82]/14 bg-white/75 px-3 py-2 text-[11px] font-black text-[#172033] transition hover:bg-white focus:outline-none focus:ring-2 focus:ring-[#9ec7d9]"
                    >
                      <BookOpen className="h-3.5 w-3.5" />
                      Wiki’de tekrar et
                    </button>
                  ) : null}
                  {onOpenIDE ? (
                    <button
                      type="button"
                      onClick={() => onOpenIDE(activeQuiz.question)}
                      className="inline-flex items-center gap-1.5 rounded-lg border border-[#526d82]/14 bg-white/75 px-3 py-2 text-[11px] font-black text-[#172033] transition hover:bg-white focus:outline-none focus:ring-2 focus:ring-[#9ec7d9]"
                    >
                      <Code2 className="h-3.5 w-3.5" />
                      Benzer pratik çöz
                    </button>
                  ) : null}
                  {!isLastQuestion ? (
                    <button
                      type="button"
                      onClick={handleNextQuestion}
                      className="inline-flex items-center gap-1.5 rounded-lg border border-[#8fb7a2]/28 bg-[#f2faf5]/85 px-3 py-2 text-[11px] font-black text-[#47725d] transition hover:bg-[#e7f4ec] focus:outline-none focus:ring-2 focus:ring-[#9ec7d9]"
                    >
                      Sıradaki soruya geç
                      <ChevronRight className="h-3.5 w-3.5" />
                    </button>
                  ) : null}
                </div>
                {recoveryHint ? (
                  <p className="mt-3 rounded-lg border border-[#526d82]/12 bg-[#f7f9fa]/72 px-3 py-2 text-[11px] leading-5 text-[#667085]">
                    Tutor’a şu notu sor: “{recoveryHint}”
                  </p>
                ) : null}
              </div>
            )}
          </div>
        )}

        {recordError && <p className="mt-3 text-[11px] font-medium text-amber-600">{recordError}</p>}
        {completionNote && <p className="mt-3 text-[12px] font-semibold text-[#47725d]">✓ {completionNote}</p>}
      </div>

      <div className="flex flex-col items-center gap-3 border-t border-[#526d82]/12 bg-white/45 px-6 py-4">
        {submitState === "idle" ? (
          <>
            {activeQuiz.type !== "coding" && (
              <button
                onClick={handleSubmit}
                disabled={!selectedId}
                className="rounded-full bg-[#172033] px-8 py-2.5 text-sm font-semibold text-white shadow-lg transition-all hover:bg-[#24324d] disabled:cursor-not-allowed disabled:opacity-35"
              >
                Cevabı Gönder
              </button>
            )}
            {planDiagnostic && answers.length === 0 && (
              <button
                onClick={handleStartFromZero}
                onBlur={() => setConfirmingZeroStart(false)}
                className={`text-[11px] font-bold uppercase tracking-[0.1em] transition-colors ${
                  confirmingZeroStart ? "text-amber-600 hover:text-amber-700" : "text-[#667085] hover:text-[#47725d]"
                }`}
              >
                {confirmingZeroStart ? "Eminim, sıfırdan başlat ↵" : "Hiç bilmiyorum, sıfırdan başlat →"}
              </button>
            )}
          </>
        ) : submitState === "evaluating" ? (
          <div className="flex h-10 items-center gap-2 text-sm font-medium text-[#47725d]">
            <Loader2 className="h-4 w-4 animate-spin" />
            <span>Yanıt değerlendiriliyor...</span>
          </div>
        ) : !isLastQuestion ? (
          <button
            onClick={handleNextQuestion}
            className="flex items-center gap-2 rounded-full border border-[#8fb7a2]/40 bg-[#f2faf5] px-6 py-2 text-sm font-semibold text-[#47725d] transition hover:bg-[#e7f4ec]"
          >
            Sıradaki soru <ChevronRight className="h-4 w-4" />
          </button>
        ) : (
          <div className="flex h-10 items-center gap-2 text-sm font-medium text-[#47725d]">
            <span className="h-1.5 w-1.5 rounded-full bg-[#8fb7a2]" />
            Quiz akışı tamamlandı
          </div>
        )}
      </div>
    </motion.div>
  );
}
