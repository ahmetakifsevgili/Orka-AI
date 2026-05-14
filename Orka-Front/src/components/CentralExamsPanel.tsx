import { useEffect, useMemo, useState } from "react";
import { BookOpenCheck, CalendarDays, ChevronRight, GraduationCap, Loader2 } from "lucide-react";
import { CentralExamsAPI } from "@/services/api";
import type { CentralExamDenemeBlueprintDto, CentralExamDto, CentralExamStudyHomeDto } from "@/lib/types";

export default function CentralExamsPanel() {
  const [exams, setExams] = useState<CentralExamDto[]>([]);
  const [home, setHome] = useState<CentralExamStudyHomeDto | null>(null);
  const [denemeler, setDenemeler] = useState<CentralExamDenemeBlueprintDto[]>([]);
  const [loading, setLoading] = useState(true);

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

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center text-[#667085]">
        <Loader2 className="mr-2 h-4 w-4 animate-spin" />
        Merkezi Sınavlar yükleniyor
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
                Merkezi Sınavlar
              </div>
              <h1 className="mt-2 text-2xl font-black text-[#172033]">KPSS çalışma alanı</h1>
              <p className="mt-2 max-w-2xl text-sm leading-6 text-[#667085]">
                KPSS hazırlık iskeleti Orka’nın sınav ağacı, soru bankası, pratik ve ileride öğrenme hafızasıyla bağlanacak merkezi sınav alanıdır.
              </p>
            </div>
            <div className="rounded-xl border border-[#e5e9f0] bg-[#f8fafc] px-4 py-3 text-sm text-[#526d82]">
              <div className="font-black">{kpss?.availabilityStatus === "available" ? "Kullanıma açık" : "Hazırlanıyor"}</div>
              <div>{home?.supportedVariants?.map((variant) => variant.displayName).join(" / ") || "KPSS Lisans / KPSS Önlisans"}</div>
            </div>
          </div>
          <div className="mt-4 rounded-xl border border-[#f0d7a7] bg-[#fff8ee] p-3 text-sm text-[#7a5b16]">
            {home?.userSafeVerificationLabel || "Resmi müfredat iddiası değildir; doğrulanmış kaynak eklendiğinde resmi kaynak etiketi gösterilir."}
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
                  <span className={exam.capabilities?.hasPractice ? "text-[#27724d]" : "text-[#98a2b3]"}>Pratik {exam.capabilities?.hasPractice ? "var" : "yok"}</span>
                  <span className={exam.capabilities?.hasMiniDeneme ? "text-[#27724d]" : "text-[#98a2b3]"}>Mini deneme {exam.capabilities?.hasMiniDeneme ? "var" : "yok"}</span>
                  <span className={exam.capabilities?.hasQuestionBank ? "text-[#27724d]" : "text-[#98a2b3]"}>Soru bankasi {exam.capabilities?.hasQuestionBank ? "var" : "bos"}</span>
                  <span className={exam.canClaimOfficial ? "text-[#27724d]" : "text-[#98a2b3]"}>Resmi etiket {exam.canClaimOfficial ? "var" : "yok"}</span>
                </div>
              </div>
            ))}
          </div>
          {scaffoldedExams.length > 0 && (
            <div className="mt-4 rounded-xl border border-[#e5e9f0] bg-[#f8fafc] p-3 text-sm leading-6 text-[#667085]">
              YKS, LGS ve YDS bu pakette yalnizca guvenli hazirlik iskeleti olarak gorunur; pratik ve deneme akisi sahte hazirlik icerigi uretmeden kapali kalir.
            </div>
          )}
        </section>

        <div className="grid gap-5 lg:grid-cols-[1.35fr_0.9fr]">
          <section className="rounded-2xl border border-[#d7dee8] bg-white/84 p-5 shadow-sm">
            <div className="mb-4 flex items-center justify-between">
              <div>
                <div className="text-xs font-black uppercase tracking-widest text-[#8a98a8]">Sabit çalışma yapısı</div>
                <h2 className="text-lg font-black text-[#172033]">{home?.displayName || "KPSS hazırlık iskeleti"}</h2>
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
                                {topic.practiceReadyCount} pratik
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
                Sınav geri sayımı
              </div>
              <p className="mt-3 text-sm leading-6 text-[#667085]">
                {home?.countdown?.userSafeLabel || "KPSS sınav tarihi doğrulanmış kaynakla yapılandırılmadı."}
              </p>
              <div className="mt-3 rounded-lg bg-[#f8fafc] px-3 py-2 text-xs font-bold text-[#8a98a8]">
                Resmi tarih etiketi yalnızca doğrulanmış kaynak metadata’sı varsa gösterilir.
              </div>
            </section>

            <section className="rounded-2xl border border-[#d7dee8] bg-white/84 p-5 shadow-sm">
              <div className="text-xs font-black uppercase tracking-widest text-[#8a98a8]">İlk pratik kapısı</div>
              <h2 className="mt-1 text-lg font-black text-[#172033]">{entry?.title || "KPSS Türkçe Paragraf"}</h2>
              <p className="mt-2 text-sm leading-6 text-[#667085]">
                {entry?.description || "Türkçe paragraf ve anlam soruları için dar kapsamlı pratik girişi."}
              </p>
              <div className="mt-4 rounded-xl border border-[#e6ebf1] bg-[#fbfcfd] p-3">
                <div className="text-2xl font-black text-[#172033]">{entry?.practiceReadyCount ?? 0}</div>
                <div className="text-xs font-bold text-[#667085]">yayına hazır pratik sorusu</div>
              </div>
              {entry?.hasPracticeReadyQuestions ? (
                <button className="mt-4 flex w-full items-center justify-between rounded-xl bg-[#172033] px-4 py-3 text-sm font-black text-white">
                  Pratiğe başla
                  <ChevronRight className="h-4 w-4" />
                </button>
              ) : (
                <div className="mt-4 rounded-xl border border-[#e5e9f0] bg-[#f8fafc] p-3 text-sm leading-6 text-[#667085]">
                  {entry?.emptyState || "Bu alanda henüz yayına hazır pratik sorusu yok. İçerik eklendiğinde burada çözülebilir hale gelir."}
                </div>
              )}
            </section>
          </aside>
        </div>
      </div>
    </div>
  );
}
