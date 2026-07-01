import { useEffect, useState, useRef, type ReactNode } from "react";
import { Link } from "wouter";
import { motion, useInView } from "framer-motion";
import {
  ArrowRight,
  ArrowUp,
  BookOpen,
  Network,
  CheckCircle2,
  ChevronDown,
  Brain,
  Zap,
  FileText,
  MessageSquare,
} from "lucide-react";
import OrcaLogo from "@/components/OrcaLogo";

/* ─── Reveal ──────────────────────────────────────────────────────────────── */
function Reveal({ children, className = "", delay = 0 }: { children: ReactNode; className?: string; delay?: number }) {
  const ref = useRef<HTMLDivElement>(null);
  const isInView = useInView(ref, { once: true, margin: "-32px" });
  return (
    <motion.div
      ref={ref}
      initial={{ opacity: 0, y: 14 }}
      animate={isInView ? { opacity: 1, y: 0 } : {}}
      transition={{ duration: 0.55, delay, ease: [0.16, 1, 0.3, 1] }}
      className={className}
    >
      {children}
    </motion.div>
  );
}

/* ─── Navbar ─────────────────────────────────────────────────────────────── */
function Navbar() {
  const [scrolled, setScrolled] = useState(false);
  useEffect(() => {
    const fn = () => setScrolled(window.scrollY > 12);
    fn();
    window.addEventListener("scroll", fn, { passive: true });
    return () => window.removeEventListener("scroll", fn);
  }, []);

  return (
    <header
      className="fixed inset-x-0 top-0 z-50 transition-all duration-300"
      style={{
        background: scrolled ? "rgba(9,9,11,0.88)" : "transparent",
        backdropFilter: scrolled ? "blur(16px)" : "none",
        borderBottom: scrolled ? "1px solid rgba(255,255,255,0.06)" : "1px solid transparent",
      }}
    >
      <nav className="mx-auto flex h-13 max-w-6xl items-center justify-between px-6">
        <Link href="/" className="flex items-center gap-2">
          <span className="grid h-6 w-6 place-items-center rounded-md" style={{ background: "#6ed7ce" }}>
            <OrcaLogo className="h-3.5 w-3.5" style={{ color: "#041210" }} />
          </span>
          <span className="text-[14px] font-semibold" style={{ color: "#fafafa" }}>Orka</span>
        </Link>

        <div className="hidden items-center gap-6 md:flex">
          {[["Nasil Calisir", "#how"], ["Ozellikler", "#features"], ["SSS", "#faq"]].map(([label, href]) => (
            <a
              key={label}
              href={href}
              className="text-[13px] font-medium transition-colors"
              style={{ color: "#71717a" }}
              onMouseEnter={(e) => ((e.target as HTMLElement).style.color = "#fafafa")}
              onMouseLeave={(e) => ((e.target as HTMLElement).style.color = "#71717a")}
            >
              {label}
            </a>
          ))}
        </div>

        <div className="flex items-center gap-3">
          <Link
            href="/login"
            className="text-[13px] font-medium transition-colors"
            style={{ color: "#71717a" }}
          >
            Giris
          </Link>
          <Link
            href="/login"
            className="flex items-center gap-1.5 rounded-lg px-3.5 py-1.5 text-[13px] font-medium transition-opacity hover:opacity-85"
            style={{ background: "#6ed7ce", color: "#041210" }}
          >
            Baslat
            <ArrowRight className="h-3.5 w-3.5" />
          </Link>
        </div>
      </nav>
    </header>
  );
}

/* ─── Product Mockup ─────────────────────────────────────────────────────── */
function ProductMockup() {
  const messages = [
    { role: "user", text: "Mitoz ve mayoz arasindaki fark nedir?" },
    {
      role: "ai",
      text: "Mitoz, vucüt hücrelerinde 2 özdes yavru hücre olusturur. Mayoz ise üreme hücrelerinde 4 farkli haploit hücre olusturur ve genetik cesitlilik saglar.",
    },
  ];

  return (
    <div
      className="overflow-hidden rounded-2xl"
      style={{
        background: "#111113",
        border: "1px solid rgba(255,255,255,0.08)",
        boxShadow: "0 32px 80px rgba(0,0,0,0.6), 0 0 0 1px rgba(255,255,255,0.04)",
      }}
    >
      {/* Window chrome */}
      <div
        className="flex items-center gap-1.5 px-4 py-3"
        style={{ borderBottom: "1px solid rgba(255,255,255,0.06)", background: "#0d0d0f" }}
      >
        <span className="h-2.5 w-2.5 rounded-full" style={{ background: "#f87171", opacity: 0.7 }} />
        <span className="h-2.5 w-2.5 rounded-full" style={{ background: "#fbbf24", opacity: 0.7 }} />
        <span className="h-2.5 w-2.5 rounded-full" style={{ background: "#4ade80", opacity: 0.7 }} />
        <span className="ml-3 text-[11px] font-medium" style={{ color: "#3f3f46" }}>Orka — Biyoloji 101</span>
      </div>

      <div className="flex" style={{ height: "380px" }}>
        {/* Sidebar strip */}
        <div
          className="flex w-[180px] shrink-0 flex-col gap-1 p-3"
          style={{ borderRight: "1px solid rgba(255,255,255,0.05)", background: "#0c0c0f" }}
        >
          <div className="mb-3 flex items-center gap-2 px-2">
            <span className="grid h-5 w-5 place-items-center rounded" style={{ background: "#6ed7ce" }}>
              <OrcaLogo className="h-3 w-3" style={{ color: "#041210" }} />
            </span>
            <span className="text-[12px] font-semibold" style={{ color: "#fafafa" }}>Orka</span>
          </div>
          {["Tutor", "Wiki", "Planlama", "Pratik"].map((item, i) => (
            <div
              key={item}
              className="flex items-center gap-2 rounded-md px-2 py-1.5"
              style={{
                background: i === 0 ? "rgba(255,255,255,0.06)" : "transparent",
                color: i === 0 ? "#fafafa" : "#52525b",
                fontSize: "12px",
                fontWeight: 500,
              }}
            >
              <span className="h-1.5 w-1.5 rounded-full flex-none" style={{ background: i === 0 ? "#6ed7ce" : "transparent" }} />
              {item}
            </div>
          ))}
          <div className="mt-3 px-2">
            <div className="text-[10px] font-semibold uppercase tracking-wider mb-1.5" style={{ color: "#3f3f46" }}>Gecmis</div>
            {["Biyoloji 101", "Lineer Cebir"].map((t) => (
              <div key={t} className="truncate rounded px-2 py-1 text-[11px]" style={{ color: "#52525b" }}>{t}</div>
            ))}
          </div>
        </div>

        {/* Chat area */}
        <div className="flex flex-1 flex-col">
          <div className="flex-1 space-y-5 overflow-hidden px-5 py-5">
            {messages.map((msg, i) => (
              <div key={i} className={`flex gap-2.5 ${msg.role === "user" ? "justify-end" : ""}`}>
                {msg.role === "ai" && (
                  <div
                    className="mt-0.5 grid h-6 w-6 flex-none place-items-center rounded-md"
                    style={{ background: "rgba(110,215,206,0.10)", border: "1px solid rgba(110,215,206,0.2)" }}
                  >
                    <OrcaLogo className="h-3.5 w-3.5" style={{ color: "#6ed7ce" }} />
                  </div>
                )}
                <div
                  className="max-w-[85%] rounded-xl px-3.5 py-2.5 text-[12.5px] leading-relaxed"
                  style={{
                    background: msg.role === "user" ? "rgba(255,255,255,0.06)" : "rgba(255,255,255,0.03)",
                    border: `1px solid ${msg.role === "user" ? "rgba(255,255,255,0.08)" : "rgba(255,255,255,0.05)"}`,
                    color: msg.role === "user" ? "#e4e4e7" : "#a1a1aa",
                  }}
                >
                  {msg.text}
                </div>
              </div>
            ))}
            {/* Thinking indicator */}
            <div className="flex items-center gap-2.5">
              <div
                className="grid h-6 w-6 flex-none place-items-center rounded-md"
                style={{ background: "rgba(110,215,206,0.10)", border: "1px solid rgba(110,215,206,0.2)" }}
              >
                <OrcaLogo className="h-3.5 w-3.5" style={{ color: "#6ed7ce" }} animated />
              </div>
              <div className="flex items-center gap-1">
                {[0, 1, 2].map((i) => (
                  <span
                    key={i}
                    className="h-1.5 w-1.5 rounded-full animate-pulse"
                    style={{ background: "#3f3f46", animationDelay: `${i * 0.2}s` }}
                  />
                ))}
              </div>
            </div>
          </div>
          {/* Input */}
          <div className="shrink-0 px-4 pb-4">
            <div
              className="flex items-center gap-2.5 rounded-xl px-3 py-2.5"
              style={{
                background: "rgba(255,255,255,0.04)",
                border: "1px solid rgba(255,255,255,0.08)",
              }}
            >
              <span className="flex-1 text-[12px]" style={{ color: "#3f3f46" }}>Soru sor veya konu baslat...</span>
              <div
                className="grid h-6 w-6 place-items-center rounded-md"
                style={{ background: "#6ed7ce" }}
              >
                <ArrowUp className="h-3 w-3" style={{ color: "#041210" }} />
              </div>
            </div>
          </div>
        </div>

        {/* Right wiki panel */}
        <div
          className="hidden w-[220px] shrink-0 flex-col gap-4 overflow-hidden p-5 lg:flex"
          style={{ borderLeft: "1px solid rgba(255,255,255,0.05)", background: "#0d0d0f" }}
        >
          <div>
            <div className="text-[10px] font-semibold uppercase tracking-wider mb-2" style={{ color: "#3f3f46" }}>Wiki Ozeti</div>
            <div className="text-[13px] font-medium mb-2" style={{ color: "#fafafa" }}>Hucre Bolunmesi</div>
            <div className="space-y-2">
              {["Mitoz: 2 diploit hucre", "Mayoz: 4 haploit hucre", "Genetik cesitlilik"].map((item) => (
                <div key={item} className="flex items-start gap-1.5">
                  <CheckCircle2 className="mt-0.5 h-3 w-3 flex-none" style={{ color: "#6ed7ce", opacity: 0.7 }} />
                  <span className="text-[11.5px] leading-4" style={{ color: "#71717a" }}>{item}</span>
                </div>
              ))}
            </div>
          </div>
          <div
            className="rounded-lg p-3"
            style={{ background: "rgba(110,215,206,0.05)", border: "1px solid rgba(110,215,206,0.12)" }}
          >
            <div className="text-[10px] font-medium mb-1" style={{ color: "#6ed7ce" }}>Pratik Soru</div>
            <div className="text-[11px] leading-4" style={{ color: "#71717a" }}>Mayoz hangi evrede genetik rekombinasyon gerceklesir?</div>
          </div>
        </div>
      </div>
    </div>
  );
}

/* ─── Hero ───────────────────────────────────────────────────────────────── */
function Hero() {
  return (
    <section className="relative min-h-screen pt-28 pb-24 px-6 overflow-hidden flex flex-col items-center">
      {/* Ambient glow */}
      <div
        className="pointer-events-none absolute left-1/2 top-0 -translate-x-1/2 h-[600px] w-[600px] rounded-full opacity-10"
        style={{ background: "radial-gradient(ellipse at center, #6ed7ce 0%, transparent 70%)", filter: "blur(40px)" }}
      />

      <div className="relative z-10 mx-auto max-w-4xl text-center">
        <Reveal>
          <div
            className="mb-6 inline-flex items-center gap-2 rounded-full px-3.5 py-1.5 text-[12px] font-medium"
            style={{
              background: "rgba(110,215,206,0.07)",
              border: "1px solid rgba(110,215,206,0.16)",
              color: "#6ed7ce",
            }}
          >
            <span className="h-1.5 w-1.5 rounded-full bg-current animate-pulse" />
            Yapay Zeka Ogrenme Ortami
          </div>
        </Reveal>

        <Reveal delay={0.05}>
          <h1
            className="text-[48px] md:text-[72px] font-bold leading-[1.04] tracking-tight text-balance"
            style={{ color: "#fafafa" }}
          >
            Ogrenmen icin{" "}
            <span style={{ color: "#6ed7ce" }}>kisisel bir AI</span>{" "}
            ajani.
          </h1>
        </Reveal>

        <Reveal delay={0.12}>
          <p className="mx-auto mt-6 max-w-[560px] text-[17px] leading-relaxed" style={{ color: "#71717a" }}>
            Orka, belgelerinden ve sohbetlerinden otomatik Wiki olusturur, eksiklerini teshis eder ve kisisel bir calisma plani hazirlar.
          </p>
        </Reveal>

        <Reveal delay={0.18}>
          <div className="mt-8 flex flex-wrap items-center justify-center gap-3">
            <Link
              href="/login"
              className="inline-flex items-center gap-2 rounded-xl px-6 py-3 text-[14px] font-semibold transition-opacity hover:opacity-85"
              style={{ background: "#6ed7ce", color: "#041210" }}
            >
              Ucretsiz Baslat
              <ArrowRight className="h-4 w-4" />
            </Link>
            <a
              href="#how"
              className="inline-flex items-center gap-2 rounded-xl px-6 py-3 text-[14px] font-medium transition-colors"
              style={{
                border: "1px solid rgba(255,255,255,0.08)",
                color: "#a1a1aa",
              }}
              onMouseEnter={(e) => ((e.currentTarget as HTMLElement).style.color = "#fafafa")}
              onMouseLeave={(e) => ((e.currentTarget as HTMLElement).style.color = "#a1a1aa")}
            >
              Nasil Calisir?
            </a>
          </div>
        </Reveal>

        <div className="mt-16">
          <Reveal delay={0.26}>
            <ProductMockup />
          </Reveal>
        </div>
      </div>
    </section>
  );
}

/* ─── How It Works ───────────────────────────────────────────────────────── */
function HowItWorks() {
  const steps = [
    {
      icon: MessageSquare,
      title: "Sohbetle basla",
      desc: "PDF yukle ya da konuyu yaz. Karmasik kurulum yok — sadece konusarak baslarsın.",
    },
    {
      icon: Brain,
      title: "Wiki otomatik olusur",
      desc: "Arka planda ajanlarin bilgileri derler. Onemli tanimlar ve kodlar kalici Wiki'ne aktarilir.",
    },
    {
      icon: Zap,
      title: "Pratik ve kontrol",
      desc: "Ajan sadece cevap vermez; aradaki mini quizlerle ogrendiklerini pekistirir.",
    },
  ];

  return (
    <section id="how" className="py-28 px-6" style={{ borderTop: "1px solid rgba(255,255,255,0.05)" }}>
      <div className="mx-auto max-w-5xl">
        <Reveal>
          <div className="mb-4 text-[11px] font-semibold uppercase tracking-widest" style={{ color: "#52525b" }}>Nasil Calisir</div>
          <h2 className="text-[32px] font-bold tracking-tight mb-16" style={{ color: "#fafafa" }}>
            Ogrenme surecini nasil belgeliyoruz?
          </h2>
        </Reveal>

        <div className="grid gap-8 md:grid-cols-3">
          {steps.map((step, i) => {
            const Icon = step.icon;
            return (
              <Reveal key={i} delay={i * 0.07}>
                <div
                  className="rounded-xl p-5 h-full"
                  style={{
                    background: "rgba(255,255,255,0.02)",
                    border: "1px solid rgba(255,255,255,0.06)",
                  }}
                >
                  <div
                    className="mb-4 grid h-9 w-9 place-items-center rounded-lg"
                    style={{ background: "rgba(110,215,206,0.08)", border: "1px solid rgba(110,215,206,0.15)" }}
                  >
                    <Icon className="h-4.5 w-4.5" style={{ color: "#6ed7ce" }} />
                  </div>
                  <div className="text-[11px] font-semibold uppercase tracking-widest mb-2" style={{ color: "#52525b" }}>
                    Adim {i + 1}
                  </div>
                  <h3 className="text-[16px] font-semibold mb-2" style={{ color: "#fafafa" }}>{step.title}</h3>
                  <p className="text-[13.5px] leading-relaxed" style={{ color: "#71717a" }}>{step.desc}</p>
                </div>
              </Reveal>
            );
          })}
        </div>
      </div>
    </section>
  );
}

/* ─── Features ───────────────────────────────────────────────────────────── */
function Features() {
  const features = [
    {
      icon: Network,
      label: "Bilgi Grafigi",
      desc: "Tum konular ağac yapisiyla birbirine baglanir. Neyi bildigini net gorursun.",
    },
    {
      icon: BookOpen,
      label: "Belge Ajani",
      desc: "Wiki icindeki dokumanlariniza ozgu ajan. Bir paragrafin hakkinda aninda soru sor.",
    },
    {
      icon: CheckCircle2,
      label: "Sinav Modu",
      desc: "O ana kadar isledigin konulari kapsayan mini denemeler otomatik olusturulur.",
    },
    {
      icon: FileText,
      label: "Planlama Ajani",
      desc: "Hedefini soyle; ajan seni adim adim yapili bir musfredata kavusturur.",
    },
  ];

  return (
    <section id="features" className="py-28 px-6" style={{ borderTop: "1px solid rgba(255,255,255,0.05)" }}>
      <div className="mx-auto max-w-5xl">
        <Reveal>
          <div className="mb-4 text-[11px] font-semibold uppercase tracking-widest" style={{ color: "#52525b" }}>Ozellikler</div>
          <h2 className="text-[32px] font-bold tracking-tight mb-16" style={{ color: "#fafafa" }}>
            Tum ihtiyaciniz bir yerde.
          </h2>
        </Reveal>
        <div className="grid gap-4 sm:grid-cols-2">
          {features.map((f, i) => {
            const Icon = f.icon;
            return (
              <Reveal key={i} delay={i * 0.06}>
                <div
                  className="group rounded-xl p-5 transition-colors"
                  style={{
                    background: "rgba(255,255,255,0.02)",
                    border: "1px solid rgba(255,255,255,0.06)",
                  }}
                  onMouseEnter={(e) => ((e.currentTarget as HTMLElement).style.borderColor = "rgba(110,215,206,0.2)")}
                  onMouseLeave={(e) => ((e.currentTarget as HTMLElement).style.borderColor = "rgba(255,255,255,0.06)")}
                >
                  <div className="flex items-start gap-4">
                    <div
                      className="mt-0.5 grid h-8 w-8 flex-none place-items-center rounded-lg"
                      style={{ background: "rgba(255,255,255,0.04)", border: "1px solid rgba(255,255,255,0.08)" }}
                    >
                      <Icon className="h-4 w-4" style={{ color: "#a1a1aa" }} />
                    </div>
                    <div>
                      <h3 className="text-[15px] font-semibold mb-1.5" style={{ color: "#fafafa" }}>{f.label}</h3>
                      <p className="text-[13px] leading-relaxed" style={{ color: "#71717a" }}>{f.desc}</p>
                    </div>
                  </div>
                </div>
              </Reveal>
            );
          })}
        </div>
      </div>
    </section>
  );
}

/* ─── FAQ ────────────────────────────────────────────────────────────────── */
function FAQ() {
  const faqs = [
    { q: "Orka ne tur belgeleri destekler?", a: "Su an PDF, TXT ve yapistirilmis Markdown veya duz metin desteklenmektedir." },
    { q: "Wiki notlari dis aktarilabilir mi?", a: "Evet, tum Wiki sayfalari standart Markdown formatinda indirilebilir." },
    { q: "Ucretli mi?", a: "Temel kullanim tamamen ucretsizdir. Kredi karti gerekmez." },
  ];
  const [open, setOpen] = useState<number | null>(null);

  return (
    <section id="faq" className="py-28 px-6" style={{ borderTop: "1px solid rgba(255,255,255,0.05)" }}>
      <div className="mx-auto max-w-2xl">
        <Reveal>
          <div className="mb-4 text-[11px] font-semibold uppercase tracking-widest" style={{ color: "#52525b" }}>SSS</div>
          <h2 className="text-[28px] font-bold mb-10" style={{ color: "#fafafa" }}>Sikca sorulan sorular</h2>
          <div className="space-y-2">
            {faqs.map((faq, i) => (
              <div
                key={i}
                className="rounded-xl overflow-hidden"
                style={{ border: "1px solid rgba(255,255,255,0.07)" }}
              >
                <button
                  onClick={() => setOpen(open === i ? null : i)}
                  className="w-full flex items-center justify-between px-5 py-4 text-left text-[14px] font-medium"
                  style={{ color: "#e4e4e7", background: "rgba(255,255,255,0.02)" }}
                >
                  {faq.q}
                  <ChevronDown
                    className="h-4 w-4 flex-none transition-transform"
                    style={{ color: "#52525b", transform: open === i ? "rotate(180deg)" : "none" }}
                  />
                </button>
                {open === i && (
                  <div className="px-5 pb-4 text-[13px] leading-relaxed" style={{ color: "#71717a", background: "rgba(255,255,255,0.01)" }}>
                    {faq.a}
                  </div>
                )}
              </div>
            ))}
          </div>
        </Reveal>
      </div>
    </section>
  );
}

/* ─── CTA ────────────────────────────────────────────────────────────────── */
function CTA() {
  return (
    <section className="py-28 px-6" style={{ borderTop: "1px solid rgba(255,255,255,0.05)" }}>
      <div className="mx-auto max-w-3xl text-center">
        <Reveal>
          <h2 className="text-[40px] font-bold tracking-tight mb-4 text-balance" style={{ color: "#fafafa" }}>
            Ogrenmeye simdi basla.
          </h2>
          <p className="mb-8 text-[16px] leading-relaxed" style={{ color: "#71717a" }}>
            Ucretsiz kayit ol, ilk dersini aç.
          </p>
          <Link
            href="/login"
            className="inline-flex items-center gap-2 rounded-xl px-8 py-3.5 text-[15px] font-semibold transition-opacity hover:opacity-85"
            style={{ background: "#6ed7ce", color: "#041210" }}
          >
            Ucretsiz Baslat
            <ArrowRight className="h-4.5 w-4.5" />
          </Link>
        </Reveal>
      </div>
    </section>
  );
}

/* ─── Footer ─────────────────────────────────────────────────────────────── */
function Footer() {
  return (
    <footer className="px-6 py-10" style={{ borderTop: "1px solid rgba(255,255,255,0.05)" }}>
      <div className="mx-auto max-w-5xl flex flex-col items-center justify-between gap-5 sm:flex-row">
        <div className="flex items-center gap-2">
          <span className="grid h-5 w-5 place-items-center rounded" style={{ background: "#6ed7ce" }}>
            <OrcaLogo className="h-3 w-3" style={{ color: "#041210" }} />
          </span>
          <span className="text-[13px] font-semibold" style={{ color: "#fafafa" }}>Orka</span>
        </div>
        <p className="text-[12px]" style={{ color: "#3f3f46" }}>
          © 2026 Orka. Tum haklari saklidir.
        </p>
        <div className="flex items-center gap-5">
          {["Twitter", "GitHub", "Destek"].map((item) => (
            <a
              key={item}
              href="#"
              className="text-[12px] font-medium transition-colors"
              style={{ color: "#52525b" }}
              onMouseEnter={(e) => ((e.target as HTMLElement).style.color = "#a1a1aa")}
              onMouseLeave={(e) => ((e.target as HTMLElement).style.color = "#52525b")}
            >
              {item}
            </a>
          ))}
        </div>
      </div>
    </footer>
  );
}

/* ─── Page ───────────────────────────────────────────────────────────────── */
export default function Landing() {
  return (
    <div className="min-h-screen font-sans" style={{ background: "#09090b", color: "#fafafa" }}>
      <Navbar />
      <Hero />
      <HowItWorks />
      <Features />
      <FAQ />
      <CTA />
      <Footer />
    </div>
  );
}
