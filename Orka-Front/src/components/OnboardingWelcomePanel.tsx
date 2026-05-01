import { useEffect } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { ArrowRight, BookOpen, Code2, LayoutDashboard, MessageSquare, Sparkles, X } from "lucide-react";

interface OnboardingWelcomePanelProps {
  open: boolean;
  onFirstGoal: () => void;
  onStartTour: () => void;
  onSkip: () => void;
}

const featureCards = [
  {
    icon: MessageSquare,
    title: "Hedefini yaz",
    body: "Orka plan, sohbet ve quiz akışını aynı bağlamda başlatır.",
  },
  {
    icon: LayoutDashboard,
    title: "Kokpiti izle",
    body: "Zayıf beceriler ve sıradaki en iyi adım tek yerde görünür.",
  },
  {
    icon: BookOpen,
    title: "Wiki büyüsün",
    body: "Kaynaklar, notlar ve pekiştirme önerileri kişiselleşir.",
  },
  {
    icon: Code2,
    title: "IDE ile dene",
    body: "Kod çıktısı hocaya ve öğrenme sinyallerine bağlanır.",
  },
];

export default function OnboardingWelcomePanel({ open, onFirstGoal, onStartTour, onSkip }: OnboardingWelcomePanelProps) {
  useEffect(() => {
    if (!open) return;

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") onSkip();
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [onSkip, open]);

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          className="fixed inset-0 z-[80] flex items-end justify-center bg-[#172033]/18 px-3 pb-3 backdrop-blur-[2px] sm:items-center sm:p-6"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          role="dialog"
          aria-modal="true"
          aria-label="ORKA başlangıç rehberi"
        >
          <motion.div
            initial={{ opacity: 0, y: 24, scale: 0.98 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 18, scale: 0.98 }}
            transition={{ duration: 0.22, ease: "easeOut" }}
            className="relative w-full max-w-3xl overflow-hidden rounded-t-[2rem] border border-[#526d82]/14 bg-[#f7f4ec]/96 shadow-[0_28px_80px_rgba(23,32,51,0.18)] backdrop-blur-2xl sm:rounded-[2rem]"
          >
            <div className="pointer-events-none absolute -right-16 -top-20 h-56 w-56 rounded-full bg-[#dcecf3]/70 blur-3xl" />
            <div className="pointer-events-none absolute -bottom-20 left-10 h-48 w-48 rounded-full bg-[#ddebe3]/70 blur-3xl" />

            <button
              onClick={onSkip}
              className="absolute right-4 top-4 z-10 grid h-9 w-9 place-items-center rounded-xl border border-[#526d82]/12 bg-[#eef1f3]/78 text-[#667085] transition hover:bg-[#f4e1dc] hover:text-[#9a4e3e]"
              aria-label="Onboarding panelini kapat"
              title="Kapat"
            >
              <X className="h-4 w-4" />
            </button>

            <div className="relative grid gap-0 lg:grid-cols-[1fr_0.92fr]">
              <section className="p-6 sm:p-8">
                <div className="mb-5 inline-flex items-center gap-2 rounded-full bg-[#dcecf3]/78 px-3 py-1 text-[11px] font-black uppercase tracking-[0.16em] text-[#2d5870]">
                  <Sparkles className="h-3.5 w-3.5" />
                  İlk ORKA adımı
                </div>
                <h2 className="max-w-xl text-2xl font-black tracking-tight text-[#172033] sm:text-3xl">
                  ORKA’ya hoş geldin. Burada hedefin, kaynakların ve hataların tek öğrenme akışına dönüşür.
                </h2>
                <p className="mt-4 max-w-xl text-sm leading-7 text-[#667085]">
                  İlk hedefini yaz; Orka planı kurar, Wiki hafızanı oluşturur, quizlerle zayıf becerileri yakalar ve gerektiğinde IDE veya sınıf akışıyla destek olur.
                </p>

                <div className="mt-6 flex flex-col gap-3 sm:flex-row sm:flex-wrap">
                  <button
                    onClick={onFirstGoal}
                    className="inline-flex items-center justify-center gap-2 rounded-2xl bg-[#172033] px-5 py-3 text-sm font-extrabold text-white shadow-sm transition hover:-translate-y-0.5 hover:bg-[#24314b]"
                  >
                    İlk hedefimi yaz
                    <ArrowRight className="h-4 w-4" />
                  </button>
                  <button
                    onClick={onStartTour}
                    className="inline-flex items-center justify-center gap-2 rounded-2xl border border-[#526d82]/14 bg-[#eef1f3]/82 px-5 py-3 text-sm font-extrabold text-[#344054] transition hover:-translate-y-0.5 hover:bg-[#e4eaec]"
                  >
                    Kısa turu başlat
                  </button>
                  <button
                    onClick={onSkip}
                    className="inline-flex items-center justify-center rounded-2xl px-4 py-3 text-sm font-bold text-[#667085] transition hover:text-[#172033]"
                  >
                    Şimdilik geç
                  </button>
                </div>
              </section>

              <section className="border-t border-[#526d82]/10 bg-[#eef1f3]/58 p-5 sm:p-6 lg:border-l lg:border-t-0">
                <div className="grid gap-3">
                  {featureCards.map((item) => {
                    const Icon = item.icon;
                    return (
                      <div key={item.title} className="rounded-[1.25rem] border border-[#526d82]/10 bg-[#f7f9fa]/72 p-4 shadow-sm">
                        <div className="flex gap-3">
                          <div className="grid h-10 w-10 flex-shrink-0 place-items-center rounded-2xl bg-[#dcecf3]/74 text-[#2d5870]">
                            <Icon className="h-4 w-4" />
                          </div>
                          <div>
                            <p className="text-sm font-extrabold text-[#172033]">{item.title}</p>
                            <p className="mt-1 text-xs leading-5 text-[#667085]">{item.body}</p>
                          </div>
                        </div>
                      </div>
                    );
                  })}
                </div>
              </section>
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
