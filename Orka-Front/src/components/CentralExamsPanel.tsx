import { useEffect, useMemo, useState } from "react";
import { AlertCircle, BookOpenCheck, CalendarDays, CheckCircle2, ChevronRight, Circle, GraduationCap, Loader2, XCircle } from "lucide-react";
import { CentralExamsAPI } from "@/services/api";
import type {
  CentralExamDenemeBlueprintDto,
  CentralExamDto,
  CentralExamStudyHomeDto,
  PracticeContentBlockDto,
  PracticeQuestionDto,
  PracticeResultDto,
  PracticeSessionDto,
} from "@/lib/types";

export default function CentralExamsPanel() {
  const [exams, setExams] = useState<CentralExamDto[]>([]);
  const [home, setHome] = useState<CentralExamStudyHomeDto | null>(null);
  const [denemeler, setDenemeler] = useState<CentralExamDenemeBlueprintDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [practiceLoading, setPracticeLoading] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [practiceSession, setPracticeSession] = useState<PracticeSessionDto | null>(null);
  const [practiceResult, setPracticeResult] = useState<PracticeResultDto | null>(null);
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [practiceError, setPracticeError] = useState<string | null>(null);

  useEffect(() => {
    let alive = true;
    setLoading(true);
    Promise.all([
      CentralExamsAPI.getCentralExams(),
      CentralExamsAPI.getKpssStudyHome(),
      CentralExamsAPI.getKpssDenemeler(),
    ])
      .then(([examList, kpssHome, kpssDenemeler]) => {
        if (!alive) return;
        setExams(examList);
        setHome(kpssHome);
        setDenemeler(kpssDenemeler);
      })
      .finally(() => {
        if (alive) setLoading(false);
      });

    return () => {
      alive = false;
    };
  }, []);

  const kpss = useMemo(() => exams.find((exam) => exam.examCode === "KPSS"), [exams]);
  const scaffoldedExams = useMemo(() => exams.filter((exam) => exam.examCode !== "KPSS"), [exams]);
  const entry = home?.recommendedEntryPoint;
  const miniDeneme = denemeler[0];
  const questionsById = useMemo(() => {
    const map = new Map<string, PracticeQuestionDto>();
    practiceSession?.questions.forEach((question) => map.set(question.questionId, question));
    return map;
  }, [practiceSession]);

  const startPractice = async () => {
    setPracticeLoading(true);
    setPracticeError(null);
    setPracticeResult(null);
    setAnswers({});
    try {
      const session = await CentralExamsAPI.startKpssTurkceParagrafPractice({ limit: 5 });
      setPracticeSession(session);
    } catch {
      setPracticeError("Exam drill could not start. Try again later.");
    } finally {
      setPracticeLoading(false);
    }
  };

  const submitPractice = async () => {
    if (!practiceSession?.practiceSetId || practiceSession.practiceSetId === "00000000-0000-0000-0000-000000000000") return;
    setSubmitting(true);
    setPracticeError(null);
    try {
      const result = await CentralExamsAPI.submitKpssTurkceParagrafPractice({
        practiceSetId: practiceSession.practiceSetId,
        answers: practiceSession.questions.map((question) => ({
          questionId: question.questionId,
          selectedOptionKey: answers[question.questionId] || null,
        })),
      });
      setPracticeResult(result);
    } catch {
      setPracticeError("Answers could not be saved. The exam drill may have refreshed.");
    } finally {
      setSubmitting(false);
    }
  };

  const renderBlocks = (blocks?: PracticeContentBlockDto[]) => {
    const safeBlocks = (blocks ?? []).filter((block) => block.text || block.contentJson || block.altText || block.caption || block.fileName);
    if (safeBlocks.length === 0) return null;

    return (
      <div className="mt-3 space-y-2">
        {safeBlocks.map((block, index) => (
          <div key={`${block.blockType}-${block.sortOrder}-${index}`} className="rounded-lg border border-[#e6ebf1] bg-white p-3 text-sm leading-6 text-[#344054]">
            {(block.blockType === "image" || block.assetType === "image") && (
              <div className="flex items-start gap-2">
                <span className="rounded-full bg-[#eef2f6] px-2 py-0.5 text-[11px] font-black uppercase text-[#667085]">Gorsel</span>
                <div>
                  <div className="font-bold text-[#172033]">{block.caption || block.altText || "Gorsel icerik"}</div>
                  {block.altText && <div className="text-xs text-[#667085]">Alt metin: {block.altText}</div>}
                </div>
              </div>
            )}
            {(block.blockType === "table" || block.blockType === "chart") && (
              <div>
                <div className="font-bold text-[#172033]">{block.caption || (block.blockType === "table" ? "Tablo" : "Grafik")}</div>
                {block.text && <div>{block.text}</div>}
              </div>
            )}
            {block.blockType === "formula" && <div className="font-mono text-[#172033]">{block.text || block.caption || "Formul icerigi"}</div>}
            {!["image", "table", "chart", "formula"].includes(block.blockType) && block.text && <div>{block.text}</div>}
            {block.longDescription && <div className="mt-1 text-xs text-[#667085]">{block.longDescription}</div>}
          </div>
        ))}
      </div>
    );
  };

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center text-[#667085]">
        <Loader2 className="mr-2 h-4 w-4 animate-spin" />
        Merkezi Sinavlar yukleniyor
      </div>
    );
  }

  return (
    <div className="h-full overflow-y-auto px-6 py-6">
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-5">
        <section className="rounded-2xl border border-[#d7dee8] bg-white/84 p-5 shadow-sm">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div>
              <div className="flex items-center gap-2 text-sm font-black text-[#526d82]">
                <GraduationCap className="h-4 w-4" />
                Merkezi Sinavlar
              </div>
              <h1 className="mt-2 text-2xl font-black text-[#172033]">KPSS calisma alani</h1>
              <p className="mt-2 max-w-2xl text-sm leading-6 text-[#667085]">
                KPSS hazirlik iskeleti Orka'nin sinav agaci, soru bankasi, exam drill ve ogrenme hafizasi ile baglanan merkezi sinav alanidir.
              </p>
            </div>
            <div className="rounded-xl border border-[#e5e9f0] bg-[#f8fafc] px-4 py-3 text-sm text-[#526d82]">
              <div className="font-black">{kpss?.availabilityStatus === "available" ? "Kullanima acik" : "Hazirlaniyor"}</div>
              <div>{home?.supportedVariants?.map((variant) => variant.displayName).join(" / ") || "KPSS Lisans / KPSS Onlisans"}</div>
            </div>
          </div>
          <div className="mt-4 rounded-xl border border-[#f0d7a7] bg-[#fff8ee] p-3 text-sm text-[#7a5b16]">
            {home?.userSafeVerificationLabel || "Resmi mufredat iddiasi degildir; dogrulanmis kaynak eklendiginde resmi kaynak etiketi gosterilir."}
          </div>
        </section>

        <section className="rounded-2xl border border-[#d7dee8] bg-white/84 p-5 shadow-sm">
          <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
            <div>
              <div className="text-xs font-black uppercase tracking-widest text-[#8a98a8]">Sinav modulleri</div>
              <h2 className="text-lg font-black text-[#172033]">Orka icinde merkezi sinav kabugu</h2>
            </div>
            <div className="text-xs font-bold text-[#667085]">Resmi kapsam etiketi yalnizca dogrulanmis kaynakla acilir.</div>
          </div>
          <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
            {exams.map((exam) => (
              <div key={exam.examCode} className="rounded-xl border border-[#e6ebf1] bg-[#fbfcfd] p-4">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <div className="text-lg font-black text-[#172033]">{exam.examCode}</div>
                    <div className="mt-1 text-sm font-bold text-[#526d82]">{exam.displayName}</div>
                  </div>
                  <span className={`rounded-full px-2 py-1 text-[11px] font-black ${exam.availabilityStatus === "available" ? "bg-[#e9f7ef] text-[#27724d]" : "bg-[#eef2f6] text-[#667085]"}`}>
                    {exam.availabilityStatus === "available" ? "Kullanimda" : "Hazirlik iskeleti"}
                  </span>
                </div>
                <p className="mt-3 min-h-[42px] text-xs leading-5 text-[#667085]">{exam.description}</p>
                <div className="mt-3 flex flex-wrap gap-2 text-[11px] font-bold text-[#526d82]">
                  {exam.supportedVariants.map((variant) => (
                    <span key={variant.variantCode} className="rounded-full bg-white px-2 py-1">
                      {variant.displayName}
                    </span>
                  ))}
                </div>
                <div className="mt-3 grid grid-cols-2 gap-2 text-[11px] font-bold">
                  <span className={exam.capabilities?.hasPractice ? "text-[#27724d]" : "text-[#98a2b3]"}>Question flow {exam.capabilities?.hasPractice ? "ready" : "limited"}</span>
                  <span className={exam.capabilities?.hasMiniDeneme ? "text-[#27724d]" : "text-[#98a2b3]"}>Mini deneme {exam.capabilities?.hasMiniDeneme ? "var" : "yok"}</span>
                  <span className={exam.capabilities?.hasQuestionBank ? "text-[#27724d]" : "text-[#98a2b3]"}>Soru bankasi {exam.capabilities?.hasQuestionBank ? "var" : "bos"}</span>
                  <span className={exam.canClaimOfficial ? "text-[#27724d]" : "text-[#98a2b3]"}>Resmi etiket {exam.canClaimOfficial ? "var" : "yok"}</span>
                </div>
              </div>
            ))}
          </div>
          {scaffoldedExams.length > 0 && (
            <div className="mt-4 rounded-xl border border-[#e5e9f0] bg-[#f8fafc] p-3 text-sm leading-6 text-[#667085]">
              YKS, LGS ve YDS bu pakette yalnizca guvenli hazirlik iskeleti olarak gorunur; question flow ve deneme akisi sahte hazirlik icerigi uretmeden kapali kalir.
            </div>
          )}
        </section>

        <div className="grid gap-5 lg:grid-cols-[1.35fr_0.9fr]">
          <section className="rounded-2xl border border-[#d7dee8] bg-white/84 p-5 shadow-sm">
            <div className="mb-4 flex items-center justify-between">
              <div>
                <div className="text-xs font-black uppercase tracking-widest text-[#8a98a8]">Sabit calisma yapisi</div>
                <h2 className="text-lg font-black text-[#172033]">{home?.displayName || "KPSS hazirlik iskeleti"}</h2>
              </div>
              <BookOpenCheck className="h-5 w-5 text-[#526d82]" />
            </div>
            <div className="space-y-3">
              {(home?.sections ?? []).map((section) => (
                <div key={section.id} className="rounded-xl border border-[#e6ebf1] bg-[#fbfcfd] p-3">
                  <div className="text-sm font-black text-[#172033]">{section.name}</div>
                  <div className="mt-2 grid gap-2 md:grid-cols-2">
                    {section.subjects.map((subject) => (
                      <div key={subject.id} className="rounded-lg border border-[#eef1f5] bg-white p-3">
                        <div className="font-bold text-[#344054]">{subject.name}</div>
                        <div className="mt-1 space-y-1 text-xs text-[#667085]">
                          {subject.topics.map((topic) => (
                            <div key={topic.id} className="flex items-center justify-between gap-2">
                              <span>{topic.name}</span>
                              <span className="rounded-full bg-[#edf4f0] px-2 py-0.5 font-bold text-[#47725d]">
                                {topic.practiceReadyCount} ready questions
                              </span>
                            </div>
                          ))}
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </section>

          <aside className="flex flex-col gap-5">
            <section className="rounded-2xl border border-[#d7dee8] bg-white/84 p-5 shadow-sm">
              <div className="text-xs font-black uppercase tracking-widest text-[#8a98a8]">Mini Deneme</div>
              <h2 className="mt-1 text-lg font-black text-[#172033]">{miniDeneme?.name || "KPSS Mini Deneme"}</h2>
              <p className="mt-2 text-sm leading-6 text-[#667085]">
                {miniDeneme?.description || "Resmi OSYM simulasyonu degil; Orka soru bankasindaki yayina hazir sorularla dar kapsamli deneme akisi."}
              </p>
              <div className="mt-4 grid grid-cols-2 gap-3">
                <div className="rounded-xl border border-[#e6ebf1] bg-[#fbfcfd] p-3">
                  <div className="text-2xl font-black text-[#172033]">{miniDeneme?.availableQuestionCount ?? 0}</div>
                  <div className="text-xs font-bold text-[#667085]">uygun soru</div>
                </div>
                <div className="rounded-xl border border-[#e6ebf1] bg-[#fbfcfd] p-3">
                  <div className="text-2xl font-black text-[#172033]">{miniDeneme?.totalQuestionCount ?? 5}</div>
                  <div className="text-xs font-bold text-[#667085]">hedef soru</div>
                </div>
              </div>
              {!miniDeneme?.hasEnoughQuestions && (
                <div className="mt-4 rounded-xl border border-[#e5e9f0] bg-[#f8fafc] p-3 text-sm leading-6 text-[#667085]">
                  {miniDeneme?.emptyState || "Mini deneme icin yeterli yayina hazir soru yok; taslak veya incelemedeki sorular kullanilmaz."}
                </div>
              )}
            </section>

            <section className="rounded-2xl border border-[#d7dee8] bg-white/84 p-5 shadow-sm">
              <div className="flex items-center gap-2 text-sm font-black text-[#526d82]">
                <CalendarDays className="h-4 w-4" />
                Sinav geri sayimi
              </div>
              <p className="mt-3 text-sm leading-6 text-[#667085]">
                {home?.countdown?.userSafeLabel || "KPSS sinav tarihi dogrulanmis kaynakla yapilandirilmadi."}
              </p>
              <div className="mt-3 rounded-lg bg-[#f8fafc] px-3 py-2 text-xs font-bold text-[#8a98a8]">
                Resmi tarih etiketi yalnizca dogrulanmis kaynak metadatasi varsa gosterilir.
              </div>
            </section>

            <section className="rounded-2xl border border-[#d7dee8] bg-white/84 p-5 shadow-sm">
              <div className="text-xs font-black uppercase tracking-widest text-[#8a98a8]">First exam drill</div>
              <h2 className="mt-1 text-lg font-black text-[#172033]">{entry?.title || "KPSS Turkce Paragraf"}</h2>
              <p className="mt-2 text-sm leading-6 text-[#667085]">
                {entry?.description || "Turkce paragraf ve anlam sorulari icin dar kapsamli question flow girisi."}
              </p>
              <div className="mt-4 rounded-xl border border-[#e6ebf1] bg-[#fbfcfd] p-3">
                <div className="text-2xl font-black text-[#172033]">{entry?.practiceReadyCount ?? 0}</div>
                <div className="text-xs font-bold text-[#667085]">ready exam questions</div>
              </div>
              {entry?.hasPracticeReadyQuestions ? (
                <button
                  type="button"
                  onClick={startPractice}
                  disabled={practiceLoading}
                  className="mt-4 flex w-full items-center justify-between rounded-xl bg-[#172033] px-4 py-3 text-sm font-black text-white disabled:cursor-not-allowed disabled:opacity-70"
                >
                  {practiceLoading ? "Question flow preparing" : "Start exam drill"}
                  {practiceLoading ? <Loader2 className="h-4 w-4 animate-spin" /> : <ChevronRight className="h-4 w-4" />}
                </button>
              ) : (
                <div className="mt-4 rounded-xl border border-[#e5e9f0] bg-[#f8fafc] p-3 text-sm leading-6 text-[#667085]">
                  {entry?.emptyState || "Bu alanda henuz yayina hazir exam question yok. Icerik eklendiginde burada cozulebilir hale gelir."}
                </div>
              )}
            </section>
          </aside>
        </div>

        <section className="rounded-2xl border border-[#d7dee8] bg-white/84 p-5 shadow-sm">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div>
              <div className="text-xs font-black uppercase tracking-widest text-[#8a98a8]">KPSS Turkce Paragraf drill</div>
              <h2 className="mt-1 text-lg font-black text-[#172033]">Coz, gonder, sonucu incele</h2>
              <p className="mt-2 max-w-2xl text-sm leading-6 text-[#667085]">
                Yayina hazir sorularla dar pilot akis. Cevap anahtari ve aciklama sadece gonderimden sonra gosterilir.
              </p>
            </div>
            {practiceSession?.status === "ready" && !practiceResult && (
              <button
                type="button"
                onClick={submitPractice}
                disabled={submitting}
                className="rounded-xl bg-[#27724d] px-4 py-3 text-sm font-black text-white disabled:cursor-not-allowed disabled:opacity-70"
              >
                {submitting ? "Gonderiliyor" : "Cevaplari gonder"}
              </button>
            )}
          </div>

          {practiceError && (
            <div className="mt-4 flex items-start gap-2 rounded-xl border border-[#f1c2c2] bg-[#fff5f5] p-3 text-sm text-[#9b1c1c]">
              <AlertCircle className="mt-0.5 h-4 w-4" />
              {practiceError}
            </div>
          )}

          {!practiceSession && !practiceResult && (
            <div className="mt-4 rounded-xl border border-[#e5e9f0] bg-[#f8fafc] p-4 text-sm leading-6 text-[#667085]">
              {entry?.hasPracticeReadyQuestions
                ? "Exam drill baslatildiginda sorular burada acilir. Seceneklerde dogru cevap bilgisi tasinmaz."
                : entry?.emptyState || "Bu alanda henuz yayina hazir exam question yok."}
            </div>
          )}

          {practiceSession?.status === "empty" && (
            <div className="mt-4 rounded-xl border border-[#e5e9f0] bg-[#f8fafc] p-4 text-sm leading-6 text-[#667085]">
              {practiceSession.emptyState || "Bu alanda henuz yayina hazir exam question yok."}
            </div>
          )}

          {practiceSession?.status === "ready" && !practiceResult && (
            <div className="mt-5 space-y-4">
              {practiceSession.questions.map((question, index) => (
                <article key={question.questionId} className="rounded-xl border border-[#e6ebf1] bg-[#fbfcfd] p-4">
                  <div className="flex flex-wrap items-center justify-between gap-2 text-xs font-bold text-[#667085]">
                    <span>Soru {index + 1}</span>
                    <span>{question.examContext.subjectCode || "TURKCE"} / {question.examContext.topicCode || "PARAGRAF"}</span>
                  </div>
                  {question.stimuli?.map((stimulus) => (
                    <div key={`${question.questionId}-${stimulus.sortOrder}-${stimulus.title}`} className="mt-3 rounded-lg border border-[#e6ebf1] bg-white p-3">
                      <div className="text-xs font-black uppercase tracking-widest text-[#8a98a8]">{stimulus.stimulusType}</div>
                      {stimulus.title && <div className="mt-1 font-black text-[#172033]">{stimulus.title}</div>}
                      {stimulus.contentText && <p className="mt-2 whitespace-pre-wrap text-sm leading-6 text-[#344054]">{stimulus.contentText}</p>}
                    </div>
                  ))}
                  {question.stem && <p className="mt-3 whitespace-pre-wrap text-sm leading-6 text-[#344054]">{question.stem}</p>}
                  {renderBlocks(question.contentBlocks)}
                  <div className="mt-4 grid gap-2">
                    {question.options.map((option) => {
                      const selected = answers[question.questionId] === option.optionKey;
                      return (
                        <button
                          key={`${question.questionId}-${option.optionKey}`}
                          type="button"
                          onClick={() => setAnswers((current) => ({ ...current, [question.questionId]: option.optionKey }))}
                          className={`flex w-full items-start gap-3 rounded-xl border p-3 text-left text-sm transition ${
                            selected ? "border-[#27724d] bg-[#edf8f1] text-[#172033]" : "border-[#e6ebf1] bg-white text-[#344054] hover:border-[#b8c5d6]"
                          }`}
                        >
                          <span className="mt-0.5 flex h-6 w-6 shrink-0 items-center justify-center rounded-full border border-current text-xs font-black">
                            {option.optionKey}
                          </span>
                          <span className="min-w-0 flex-1">
                            {option.text && <span>{option.text}</span>}
                            {renderBlocks(option.contentBlocks)}
                          </span>
                        </button>
                      );
                    })}
                  </div>
                </article>
              ))}
            </div>
          )}

          {practiceResult && (
            <div className="mt-5 space-y-4">
              <div className="grid gap-3 md:grid-cols-4">
                {[
                  ["Toplam", practiceResult.totalQuestions],
                  ["Dogru", practiceResult.correctCount],
                  ["Yanlis", practiceResult.wrongCount],
                  ["Bos", practiceResult.blankCount],
                ].map(([label, value]) => (
                  <div key={label} className="rounded-xl border border-[#e6ebf1] bg-[#fbfcfd] p-3">
                    <div className="text-2xl font-black text-[#172033]">{value}</div>
                    <div className="text-xs font-bold text-[#667085]">{label}</div>
                  </div>
                ))}
              </div>

              {practiceResult.nextAction && (
                <div className="rounded-xl border border-[#d6eadf] bg-[#f1faf5] p-4 text-sm leading-6 text-[#27724d]">
                  <div className="font-black">{practiceResult.nextAction.title}</div>
                  <div>{practiceResult.nextAction.reason}</div>
                  {practiceResult.studyContext?.pathLabel && <div className="mt-1 text-xs font-bold">Calisma baglami: {practiceResult.studyContext.pathLabel}</div>}
                </div>
              )}

              {practiceResult.tutorRemediationContext && (
                <div className="rounded-xl border border-[#e5e9f0] bg-white p-4 text-sm leading-6 text-[#667085]">
                  <div className="font-black text-[#172033]">Tutor telafi baglami</div>
                  {practiceResult.tutorRemediationContext}
                </div>
              )}

              <div className="space-y-3">
                {practiceResult.results.map((result, index) => {
                  const question = questionsById.get(result.questionId);
                  const displayQuestion = question ?? result;
                  return (
                    <article key={result.questionId} className="rounded-xl border border-[#e6ebf1] bg-[#fbfcfd] p-4">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <div className="text-sm font-black text-[#172033]">Soru {index + 1}</div>
                        <div className={`flex items-center gap-1 text-xs font-black ${result.isBlank ? "text-[#8a98a8]" : result.isCorrect ? "text-[#27724d]" : "text-[#b42318]"}`}>
                          {result.isBlank ? <Circle className="h-4 w-4" /> : result.isCorrect ? <CheckCircle2 className="h-4 w-4" /> : <XCircle className="h-4 w-4" />}
                          {result.isBlank ? "Bos" : result.isCorrect ? "Dogru" : "Yanlis"}
                        </div>
                      </div>
                      {displayQuestion.stimuli?.map((stimulus) => (
                        <div key={`${result.questionId}-review-${stimulus.sortOrder}-${stimulus.title}`} className="mt-3 rounded-lg border border-[#e6ebf1] bg-white p-3">
                          <div className="text-xs font-black uppercase tracking-widest text-[#8a98a8]">{stimulus.stimulusType}</div>
                          {stimulus.title && <div className="mt-1 font-black text-[#172033]">{stimulus.title}</div>}
                          {stimulus.contentText && <p className="mt-2 whitespace-pre-wrap text-sm leading-6 text-[#344054]">{stimulus.contentText}</p>}
                        </div>
                      ))}
                      {displayQuestion.stem && <p className="mt-3 whitespace-pre-wrap text-sm leading-6 text-[#344054]">{displayQuestion.stem}</p>}
                      {renderBlocks(displayQuestion.contentBlocks)}
                      <div className="mt-3 flex flex-wrap gap-2 text-xs font-bold text-[#667085]">
                        <span>Secimin: {result.selectedOptionKey || "Bos"}</span>
                        <span>Dogru cevap: {result.correctOptionKey || "-"}</span>
                      </div>
                      {result.explanation && (
                        <div className="mt-3 rounded-lg border border-[#e5e9f0] bg-white p-3 text-sm leading-6 text-[#344054]">
                          {result.explanation}
                        </div>
                      )}
                    </article>
                  );
                })}
              </div>
            </div>
          )}
        </section>
      </div>
    </div>
  );
}
