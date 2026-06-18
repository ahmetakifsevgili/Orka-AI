import { useCallback, useEffect, useMemo, useState } from "react";
import { Bookmark, CheckCircle2, ClipboardCheck, CreditCard, Loader2, Plus, RefreshCcw, Sparkles, Trash2 } from "lucide-react";
import toast from "react-hot-toast";
import type { AdaptiveAssessmentNextItem, ApiTopic, LearningWorkspaceState, QuestionPracticeSessionDto, QuestionPracticeSubmitResponseDto } from "@/lib/types";
import { BookmarksAPI, DailyChallengeAPI, FlashcardsAPI, QuestionPracticeAPI, QuizAPI, ReviewAPI } from "@/services/api";
import QuizCard from "./QuizCard";
import { NextActionCard, WorkspaceHeader, WorkspaceMetric } from "./AgenticWorkspace";

type PanelProps = {
  topic: ApiTopic | null;
  sessionId?: string;
  onOpenChat: () => void;
  onOpenIDE?: () => void;
  mode?: "practice" | "review";
  workspaceState?: LearningWorkspaceState | null;
  onLearningProjectionChanged?: () => void;
};

export default function LearningPanel({ topic, sessionId, onOpenChat, onOpenIDE, mode = "review", workspaceState, onLearningProjectionChanged }: PanelProps) {
  const [loading, setLoading] = useState(false);
  const [flashcards, setFlashcards] = useState<any[]>([]);
  const [reviews, setReviews] = useState<any[]>([]);
  const [bookmarks, setBookmarks] = useState<any[]>([]);
  const [challenge, setChallenge] = useState<any | null>(null);
  const [adaptiveSessionId, setAdaptiveSessionId] = useState<string | null>(null);
  const [adaptiveNext, setAdaptiveNext] = useState<AdaptiveAssessmentNextItem | null>(null);
  const [adaptiveLoading, setAdaptiveLoading] = useState(false);
  const [questionPracticeSession, setQuestionPracticeSession] = useState<QuestionPracticeSessionDto | null>(null);
  const [questionPracticeAnswers, setQuestionPracticeAnswers] = useState<Record<string, string>>({});
  const [questionPracticeResult, setQuestionPracticeResult] = useState<QuestionPracticeSubmitResponseDto | null>(null);
  const [questionPracticeLoading, setQuestionPracticeLoading] = useState(false);
  const [questionPracticeSubmitting, setQuestionPracticeSubmitting] = useState(false);
  const [front, setFront] = useState("");
  const [back, setBack] = useState("");
  const [bookmarkNote, setBookmarkNote] = useState("");

  const topicId = topic?.id;
  const title = topic?.title ?? "Genel çalışma alanı";
  const isPractice = mode === "practice";
  const projectedAction = workspaceState?.missionControl?.primaryMission ?? workspaceState?.orkaLearningState?.primaryNextAction ?? null;
  const projectedTitle = projectedAction?.label ?? (isPractice ? "Start a quiz loop and collect evidence" : reviews.length > 0 ? "Clear due review" : "Add a memory card");
  const projectedReason =
    projectedAction?.reason ??
    (isPractice
      ? "Orka selects questions from weak or uncertain concepts; answers become mastery evidence."
      : reviews.length > 0
        ? "Due review lowers forgetting risk and improves the next Tutor explanation."
        : "There is no due review yet; saving a flashcard or bookmark gives memory something real to use.");
  const projectedPrimaryLabel = projectedAction?.label ?? (isPractice ? "Start quiz loop" : reviews.length > 0 ? "Show due review" : "Add flashcard");

  const refresh = useCallback(async () => {
    setLoading(true);
    try {
      const [cards, due, today, saved] = await Promise.all([
        FlashcardsAPI.list(topicId).catch(() => []),
        ReviewAPI.due(topicId).catch(() => []),
        DailyChallengeAPI.today(topicId).catch(() => null),
        BookmarksAPI.list(topicId).catch(() => []),
      ]);
      setFlashcards(Array.isArray(cards) ? cards : []);
      setReviews(Array.isArray(due) ? due : []);
      setChallenge(today);
      setBookmarks(Array.isArray(saved) ? saved : []);
    } finally {
      setLoading(false);
    }
  }, [topicId]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const challengeQuestions = useMemo(() => {
    if (!challenge?.questions) return [];
    if (Array.isArray(challenge.questions)) return challenge.questions;
    try {
      return JSON.parse(challenge.questionsJson ?? challenge.questions ?? "[]");
    } catch {
      return [];
    }
  }, [challenge]);

  const createFlashcard = async () => {
    if (!front.trim() || !back.trim()) return;
    try {
      await FlashcardsAPI.create({ topicId, front: front.trim(), back: back.trim(), skillTag: topic?.title });
      setFront("");
      setBack("");
      toast.success("Flashcard kaydedildi.");
      await refresh();
    } catch {
      toast.error("Flashcard kaydedilemedi.");
    }
  };

  const createTopicBookmark = async () => {
    if (!topicId) {
      toast.error("Bookmark için önce bir konu seç.");
      return;
    }
    try {
      await BookmarksAPI.create({
        topicId,
        sessionId,
        title,
        note: bookmarkNote.trim() || undefined,
        tags: ["frontend", "learning"],
      });
      setBookmarkNote("");
      toast.success("Bookmark eklendi.");
      await refresh();
    } catch {
      toast.error("Bookmark eklenemedi.");
    }
  };

  const startAdaptivePractice = async () => {
    if (!topicId) {
      toast.error("Quiz loop için önce bir konu seç.");
      return;
    }
    setAdaptiveLoading(true);
    try {
      const session = await QuizAPI.startAdaptive({ topicId, sessionId, minItems: 8, maxItems: 20 });
      setAdaptiveSessionId(session.id);
      const next = await QuizAPI.getAdaptiveNext(session.id);
      setAdaptiveNext(next);
      if (next.isComplete) {
        toast("Bu konu için adaptif soru havuzu henüz hazır değil.");
      }
    } catch {
      toast.error("Quiz loop başlatılamadı.");
    } finally {
      setAdaptiveLoading(false);
    }
  };

  const startQuestionBankPractice = async () => {
    if (!topicId) {
      toast.error("Quiz loop icin once bir konu sec.");
      return;
    }
    setQuestionPracticeLoading(true);
    setQuestionPracticeResult(null);
    setQuestionPracticeAnswers({});
    try {
      const session = await QuestionPracticeAPI.start({
        topicId,
        sessionId,
        mode: "weak_concept_drill",
        count: 8,
      });
      setQuestionPracticeSession(session);
      if (session.status === "empty") {
        toast("Bu konu icin practice-ready soru henuz yok.");
      }
    } catch {
      toast.error("Quiz loop baslatilamadi.");
    } finally {
      setQuestionPracticeLoading(false);
    }
  };

  const selectQuestionPracticeAnswer = (questionItemId: string, optionKey: string) => {
    if (questionPracticeResult) return;
    setQuestionPracticeAnswers((prev) => ({ ...prev, [questionItemId]: optionKey }));
  };

  const submitQuestionBankPractice = async () => {
    if (!questionPracticeSession || questionPracticeSession.questions.length === 0) return;
    setQuestionPracticeSubmitting(true);
    try {
      const result = await QuestionPracticeAPI.submit({
        practiceSetId: questionPracticeSession.practiceSetId,
        topicId,
        sessionId,
        mode: questionPracticeSession.mode,
        answers: questionPracticeSession.questions.map((question) => {
          const selected = questionPracticeAnswers[question.questionItemId];
          return {
            questionItemId: question.questionItemId,
            selectedOptionKey: selected ?? null,
            wasSkipped: !selected,
          };
        }),
      });
      setQuestionPracticeResult(result);
      toast.success("Quiz evidence kaydedildi.");
      onLearningProjectionChanged?.();
      await refresh();
    } catch {
      toast.error("Quiz result kaydedilemedi.");
    } finally {
      setQuestionPracticeSubmitting(false);
    }
  };

  return (
    <div className="flex h-full flex-1 flex-col overflow-hidden bg-transparent">
      <div className="flex-shrink-0 border-b border-[#526d82]/10 px-6 py-5">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
          <WorkspaceHeader
            eyebrow="Review / Quiz"
            title="Review / Quiz"
            description={
              isPractice
                ? `${title} için sıradaki soru, kavram kanıtı ve Code IDE handoff'u aynı yüzeyde tutulur.`
                : `${title} için due review, flashcard, bookmark ve repair memory tek akışta görünür.`
            }
          />
          <div className="flex flex-col items-start gap-2 lg:items-end">
            <button
              onClick={() => void refresh()}
              disabled={loading}
              className="inline-flex items-center gap-2 rounded-xl border border-[#526d82]/14 bg-white/70 px-3 py-2 text-xs font-bold text-[#667085] transition hover:bg-[#eef1f3]"
            >
              {loading ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <RefreshCcw className="h-3.5 w-3.5" />}
              Refresh
            </button>
          </div>
        </div>
      </div>

      <div className="flex-1 overflow-y-auto px-6 py-6">
        <section className="mb-5">
          <NextActionCard
            title={projectedTitle}
            reason={projectedReason}
            primaryLabel={projectedPrimaryLabel}
            onPrimary={isPractice ? () => void startQuestionBankPractice() : () => undefined}
            secondary={
              <button
                onClick={isPractice ? onOpenIDE : onOpenChat}
                className="inline-flex items-center justify-center gap-2 rounded-xl border border-[#526d82]/14 bg-white/65 px-4 py-3 text-xs font-black text-[#172033] transition hover:bg-[#f7f9fa]"
              >
                {isPractice ? "Code IDE" : "Ask Tutor"}
              </button>
            }
          />
        </section>
        <section className="mb-5 grid gap-3 md:grid-cols-4">
          <WorkspaceMetric label="Hafıza kartı" value={flashcards.length} detail="özet kanıt" />
          <WorkspaceMetric label="Due review" value={reviews.length} detail="SRS queue" />
          <WorkspaceMetric label="Daily check" value={challenge ? "ready" : "none"} detail="micro quiz" />
          <WorkspaceMetric label="Kaydedilen not" value={bookmarks.length} detail="bookmark" />
        </section>
        <section className="mb-5 rounded-[1.5rem] border border-[#8fb7a2]/28 bg-[#f2faf5]/76 p-5 shadow-sm">
          <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
            <div>
              <p className="flex items-center gap-2 text-[10px] font-black uppercase tracking-[0.18em] text-[#47725d]">
                <Sparkles className="h-3.5 w-3.5" />
                Quiz evidence loop
              </p>
              <h2 className="mt-1 text-lg font-black text-[#172033]">Questions come from plan and diagnostic concepts.</h2>
              <p className="mt-2 text-sm leading-6 text-[#667085]">
                Every answer is recorded as quiz evidence; Tutor, mastery, and review signals use the same source of truth.
              </p>
            </div>
            <button
              onClick={startQuestionBankPractice}
              disabled={questionPracticeLoading || !topicId}
              className="inline-flex items-center justify-center gap-2 rounded-xl bg-[#172033] px-4 py-2.5 text-xs font-black text-white shadow-sm transition hover:bg-[#243044] disabled:opacity-40"
            >
              {questionPracticeLoading ? <Loader2 className="h-4 w-4 animate-spin" /> : <Sparkles className="h-4 w-4" />}
              Start quiz loop
            </button>
          </div>
          {questionPracticeSession?.status === "empty" && (
            <p className="mt-4 rounded-xl border border-[#8fb7a2]/35 bg-white/65 px-4 py-3 text-xs font-bold text-[#47725d]">
              {questionPracticeSession.emptyState || "Bu konu icin hazir soru yok."}
            </p>
          )}
          {questionPracticeSession?.questions.length ? (
            <div className="mt-5 space-y-3">
              <div className="flex flex-col gap-2 rounded-xl border border-[#8fb7a2]/24 bg-white/70 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                <div>
                  <p className="text-xs font-black uppercase tracking-[0.14em] text-[#47725d]">Live question bank</p>
                  <p className="mt-1 text-sm font-bold text-[#172033]">
                    {questionPracticeSession.totalQuestions} soru hazir · {questionPracticeSession.mode}
                  </p>
                </div>
                {questionPracticeResult ? (
                  <p className="rounded-lg bg-[#d9e7de]/80 px-3 py-2 text-xs font-black text-[#47725d]">
                    {questionPracticeResult.correctCount}/{questionPracticeResult.totalQuestions} dogru · {questionPracticeResult.blankCount} bos
                  </p>
                ) : (
                  <button
                    onClick={() => void submitQuestionBankPractice()}
                    disabled={questionPracticeSubmitting}
                    className="inline-flex items-center justify-center gap-2 rounded-xl bg-[#172033] px-4 py-2 text-xs font-black text-white disabled:opacity-40"
                  >
                    {questionPracticeSubmitting ? <Loader2 className="h-4 w-4 animate-spin" /> : <CheckCircle2 className="h-4 w-4" />}
                    Cevaplari kaydet
                  </button>
                )}
              </div>
              {questionPracticeSession.questions.map((question, index) => {
                const selected = questionPracticeAnswers[question.questionItemId];
                const result = questionPracticeResult?.results.find((item) => item.questionItemId === question.questionItemId);
                return (
                  <div key={question.questionItemId} className="rounded-xl border border-[#526d82]/12 bg-white/76 p-4">
                    <div className="flex flex-wrap items-center gap-2 text-[10px] font-black uppercase tracking-[0.12em] text-[#667085]">
                      <span>Soru {index + 1}</span>
                      {question.conceptKey && <span className="rounded-full bg-[#dcecf3]/70 px-2 py-1 text-[#2d5870]">{question.conceptKey}</span>}
                      <span className="rounded-full bg-[#eef1f3]/80 px-2 py-1">{question.difficulty}</span>
                      <span className="rounded-full bg-[#eef1f3]/80 px-2 py-1">{question.visualReadinessStatus}</span>
                    </div>
                    <p className="mt-3 text-sm font-bold leading-6 text-[#172033]">{question.stem}</p>
                    <div className="mt-3 grid gap-2 md:grid-cols-2">
                      {question.options.map((option) => {
                        const active = selected === option.optionKey;
                        return (
                          <button
                            key={option.optionKey}
                            onClick={() => selectQuestionPracticeAnswer(question.questionItemId, option.optionKey)}
                            disabled={Boolean(questionPracticeResult)}
                            className={`min-h-12 rounded-xl border px-3 py-2 text-left text-sm font-semibold transition ${
                              active
                                ? "border-[#47725d]/45 bg-[#d9e7de]/82 text-[#172033]"
                                : "border-[#526d82]/12 bg-[#f7f9fa]/75 text-[#344054] hover:bg-white"
                            } disabled:cursor-default`}
                          >
                            <span className="mr-2 font-black">{option.optionKey}</span>
                            {option.text}
                          </button>
                        );
                      })}
                    </div>
                    {result && (
                      <div className={`mt-3 rounded-xl px-3 py-2 text-xs font-bold ${
                        result.isBlank
                          ? "bg-[#fff8ee] text-[#8a641f]"
                          : result.isCorrect
                            ? "bg-[#d9e7de] text-[#47725d]"
                            : "bg-[#fee4e2] text-[#9b2c2c]"
                      }`}>
                        {result.isBlank ? "Bos kaydedildi" : result.isCorrect ? "Dogru" : "Yanlis"}
                        {result.learningImpact?.nextTutorMove ? ` · Tutor: ${result.learningImpact.nextTutorMove}` : ""}
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          ) : null}
          {adaptiveNext?.decision && adaptiveSessionId && (
            <div className="mt-4">
              <p className="mb-2 rounded-xl bg-white/58 px-3 py-2 text-xs font-bold text-[#47725d]">
                {adaptiveNext.decision.decisionReason}
              </p>
              <QuizCard
                key={adaptiveNext.decision.id}
                quiz={adaptiveNext.decision.question}
                messageId={`adaptive-${adaptiveNext.decision.id}`}
                topicId={topicId}
                sessionId={sessionId}
              adaptiveAssessment={{
                  sessionId: adaptiveSessionId,
                  decisionId: adaptiveNext.decision.id,
                  onResult: setAdaptiveNext,
                }}
                onLearningProjectionChanged={onLearningProjectionChanged}
              />
            </div>
          )}
          {adaptiveNext?.isComplete && (
            <p className="mt-4 rounded-xl border border-[#8fb7a2]/35 bg-white/65 px-4 py-3 text-xs font-bold text-[#47725d]">
              Quiz loop completed: {adaptiveNext.stopReason || "evidence enough"}.
            </p>
          )}
        </section>
        <div className="grid gap-5 xl:grid-cols-2">
          <section className="rounded-[1.5rem] border border-[#526d82]/12 bg-white/66 p-5 shadow-sm">
            <div className="mb-4 flex items-center justify-between">
              <h2 className="flex items-center gap-2 text-sm font-black text-[#172033]"><CreditCard className="h-4 w-4" /> Flashcards</h2>
              <span className="rounded-full bg-[#dcecf3]/70 px-2.5 py-1 text-[10px] font-bold text-[#2d5870]">{flashcards.length} kart</span>
            </div>
            <div className="grid gap-2 sm:grid-cols-2">
              <input value={front} onChange={(e) => setFront(e.target.value)} placeholder="Ön yüz" className="rounded-xl border border-[#526d82]/14 bg-[#f7f9fa]/80 px-3 py-2 text-sm outline-none focus:border-[#9ec7d9]" />
              <input value={back} onChange={(e) => setBack(e.target.value)} placeholder="Arka yüz" className="rounded-xl border border-[#526d82]/14 bg-[#f7f9fa]/80 px-3 py-2 text-sm outline-none focus:border-[#9ec7d9]" />
            </div>
            <button onClick={createFlashcard} disabled={!front.trim() || !back.trim()} className="mt-3 inline-flex items-center gap-2 rounded-xl bg-[#172033] px-3 py-2 text-xs font-bold text-white disabled:opacity-40">
              <Plus className="h-3.5 w-3.5" /> Kart ekle
            </button>
            <div className="mt-4 space-y-2">
              {flashcards.length === 0 ? <Empty text="Henüz flashcard yok. İlk kartı ekleyerek SRS hattını başlat." /> : flashcards.slice(0, 8).map((card) => (
                <div key={card.id} className="rounded-xl border border-[#526d82]/10 bg-[#f7f9fa]/70 px-3 py-2">
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <p className="text-sm font-bold text-[#172033]">{card.front}</p>
                      <p className="mt-1 text-xs text-[#667085]">{card.back}</p>
                    </div>
                    <button onClick={async () => { await FlashcardsAPI.delete(card.id); await refresh(); }} className="rounded-lg p-1 text-[#98a2b3] hover:bg-red-50 hover:text-red-500" aria-label="Flashcard sil">
                      <Trash2 className="h-3.5 w-3.5" />
                    </button>
                  </div>
                </div>
              ))}
            </div>
          </section>

          <section className="rounded-[1.5rem] border border-[#526d82]/12 bg-white/66 p-5 shadow-sm">
            <div className="mb-4 flex items-center justify-between">
              <h2 className="flex items-center gap-2 text-sm font-black text-[#172033]"><ClipboardCheck className="h-4 w-4" /> Due review</h2>
              <span className="rounded-full bg-[#fff8ee] px-2.5 py-1 text-[10px] font-bold text-[#8a641f]">{reviews.length} due</span>
            </div>
            <div className="space-y-2">
              {reviews.length === 0 ? <Empty text="Bugün bekleyen tekrar yok. Yanlış quiz ve IDE hataları buraya baskı üretir." /> : reviews.slice(0, 8).map((item) => (
                <div key={item.reviewItemId ?? item.id} className="rounded-xl border border-[#526d82]/10 bg-[#f7f9fa]/70 px-3 py-2">
                  <p className="text-sm font-bold text-[#172033]">{item.conceptTag ?? item.skillTag ?? item.learningObjective ?? "Review item"}</p>
                  <p className="text-xs text-[#667085]">{item.status ?? item.origin ?? "due"}</p>
                  <button onClick={async () => { await ReviewAPI.complete(item.reviewItemId ?? item.id, 4); await refresh(); }} className="mt-2 inline-flex items-center gap-1.5 rounded-lg border border-[#547c61]/20 bg-[#d9e7de]/70 px-2.5 py-1 text-[11px] font-bold text-[#547c61]">
                    <CheckCircle2 className="h-3 w-3" /> Tamamla
                  </button>
                </div>
              ))}
            </div>
          </section>

          <section className="rounded-[1.5rem] border border-[#526d82]/12 bg-white/66 p-5 shadow-sm">
            <h2 className="mb-4 text-sm font-black text-[#172033]">Günlük Challenge</h2>
            {!challenge ? <Empty text="Challenge şu an üretilemedi ya da bağlantı yok." /> : (
              <div className="rounded-xl bg-[#f7f9fa]/70 p-4">
                <p className="text-sm font-bold text-[#172033]">{challenge.status ?? "today"}</p>
                <p className="mt-1 text-xs text-[#667085]">{challenge.sourceSkillTag ?? challenge.sourceConceptTag ?? "Zayıf alan odaklı görev"}</p>
                {challengeQuestions.slice(0, 2).map((q: any, i: number) => (
                  <p key={i} className="mt-3 rounded-lg bg-white/70 px-3 py-2 text-xs text-[#344054]">{typeof q === "string" ? q : q.question ?? JSON.stringify(q)}</p>
                ))}
                {challenge.id && (
                  <button onClick={async () => { await DailyChallengeAPI.submit(challenge.id, "Frontend quick submit", 3, topicId); onLearningProjectionChanged?.(); await refresh(); }} className="mt-3 rounded-xl bg-[#172033] px-3 py-2 text-xs font-bold text-white">
                    Deneme gönder
                  </button>
                )}
              </div>
            )}
          </section>

          <section className="rounded-[1.5rem] border border-[#526d82]/12 bg-white/66 p-5 shadow-sm">
            <div className="mb-4 flex items-center justify-between">
              <h2 className="flex items-center gap-2 text-sm font-black text-[#172033]"><Bookmark className="h-4 w-4" /> Bookmarks</h2>
              <span className="rounded-full bg-[#dcecf3]/70 px-2.5 py-1 text-[10px] font-bold text-[#2d5870]">{bookmarks.length} kayıt</span>
            </div>
            <textarea value={bookmarkNote} onChange={(e) => setBookmarkNote(e.target.value)} placeholder="Bu konuyla ilgili kısa not..." rows={3} className="w-full resize-none rounded-xl border border-[#526d82]/14 bg-[#f7f9fa]/80 px-3 py-2 text-sm outline-none focus:border-[#9ec7d9]" />
            <button onClick={createTopicBookmark} className="mt-3 inline-flex items-center gap-2 rounded-xl bg-[#172033] px-3 py-2 text-xs font-bold text-white">
              <Plus className="h-3.5 w-3.5" /> Konuyu kaydet
            </button>
            <div className="mt-4 space-y-2">
              {bookmarks.length === 0 ? <Empty text="Kaydedilmiş not yok. Tutor, Wiki veya kaynaklardan önemli parçaları burada tutabilirsin." /> : bookmarks.slice(0, 8).map((item) => (
                <div key={item.id} className="rounded-xl border border-[#526d82]/10 bg-[#f7f9fa]/70 px-3 py-2">
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <p className="text-sm font-bold text-[#172033]">{item.title}</p>
                      {item.note && <p className="mt-1 text-xs text-[#667085]">{item.note}</p>}
                    </div>
                    <button onClick={async () => { await BookmarksAPI.delete(item.id); await refresh(); }} className="rounded-lg p-1 text-[#98a2b3] hover:bg-red-50 hover:text-red-500" aria-label="Bookmark sil">
                      <Trash2 className="h-3.5 w-3.5" />
                    </button>
                  </div>
                </div>
              ))}
            </div>
          </section>
        </div>

        <button onClick={onOpenChat} className="mt-6 rounded-xl border border-[#526d82]/14 bg-[#eef1f3]/80 px-4 py-2 text-xs font-bold text-[#667085] hover:text-[#172033]">
          Back to Tutor
        </button>
      </div>
    </div>
  );
}

function Empty({ text }: { text: string }) {
  return <p className="rounded-xl border border-dashed border-[#526d82]/16 bg-[#f7f9fa]/55 px-4 py-5 text-center text-xs leading-6 text-[#667085]">{text}</p>;
}
