import { useEffect, useState, useRef, type ReactNode } from "react";
import { Link } from "wouter";
import { motion, useInView } from "framer-motion";
import {
  ArrowRight,
  ArrowUp,
  MessageSquare,
  BookOpen,
  Network,
  CheckCircle2,
  ChevronDown,
  FileText,
  AlertTriangle,
  Lightbulb
} from "lucide-react";
import OrcaLogo from "@/components/OrcaLogo";

/* ─── Reveal helper ────────────────────────────────────────────────────────── */
function Reveal({
  children,
  className = "",
  delay = 0,
}: {
  children: ReactNode;
  className?: string;
  delay?: number;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const isInView = useInView(ref, { once: true, margin: "-40px" });
  return (
    <motion.div
      ref={ref}
      initial={{ opacity: 0, y: 16 }}
      animate={isInView ? { opacity: 1, y: 0 } : {}}
      transition={{ duration: 0.5, delay, ease: [0.16, 1, 0.3, 1] }}
      className={className}
    >
      {children}
    </motion.div>
  );
}

/* ─── Navbar ────────────────────────────────────────────────────────────────── */
function Navbar() {
  const [scrolled, setScrolled] = useState(false);
  useEffect(() => {
    const fn = () => setScrolled(window.scrollY > 10);
    fn();
    window.addEventListener("scroll", fn, { passive: true });
    return () => window.removeEventListener("scroll", fn);
  }, []);

  return (
    <header className="fixed inset-x-0 top-0 z-50 bg-white/80 backdrop-blur-md border-b transition-colors duration-300" style={{ borderColor: scrolled ? "rgba(0,0,0,0.08)" : "transparent" }}>
      <nav className="mx-auto flex h-14 max-w-7xl items-center justify-between px-6">
        <Link href="/" className="flex items-center gap-2.5">
          <span className="grid h-7 w-7 place-items-center rounded bg-black">
            <OrcaLogo className="h-4 w-4 text-white" />
          </span>
          <span className="text-[15px] font-semibold text-[#1a1a1a]">Orka</span>
        </Link>

        <div className="hidden items-center gap-6 md:flex">
          {["Nasıl Çalışır", "Özellikler", "SSS"].map((item) => (
            <a
              key={item}
              href={`#${item.toLowerCase().replace(" ", "-")}`}
              className="text-[13px] font-medium text-[#666666] hover:text-[#1a1a1a] transition-colors"
            >
              {item}
            </a>
          ))}
        </div>

        <div className="flex items-center gap-4">
          <Link
            href="/login"
            className="text-[13px] font-medium text-[#666666] hover:text-[#1a1a1a] transition-colors"
          >
            Giriş
          </Link>
          <Link
            href="/login"
            className="rounded-full bg-[#1a1a1a] px-4 py-1.5 text-[13px] font-medium text-white transition hover:bg-black"
          >
            Başla
          </Link>
        </div>
      </nav>
    </header>
  );
}

/* ─── Realistic UI Mockups ────────────────────────────────────────────────── */
function MockupSidebar() {
  return (
    <div className="w-[240px] shrink-0 border-r border-[#eaecf0] bg-[#fdfdfc] flex flex-col hidden md:flex">
      <div className="px-4 py-4 border-b border-[#eaecf0]">
        <div className="h-8 w-8 rounded-full bg-[#f2f2f1] flex items-center justify-center border border-[#eaecf0]">
           <BookOpen className="w-4 h-4 text-[#666666]" />
        </div>
      </div>
      <div className="p-4 space-y-4 flex-1">
        <div>
          <p className="text-[11px] font-bold uppercase text-[#888888] tracking-wider mb-2">Çalışma Alanları</p>
          <div className="space-y-1">
            <div className="flex items-center gap-2 px-2 py-1.5 rounded-md bg-[#eaecf0]/50 text-[#1a1a1a]">
              <FileText className="w-3.5 h-3.5" />
              <span className="text-[13px] font-medium">Biyoloji 101</span>
            </div>
            <div className="flex items-center gap-2 px-2 py-1.5 rounded-md text-[#666666] hover:bg-[#eaecf0]/50">
              <FileText className="w-3.5 h-3.5" />
              <span className="text-[13px] font-medium">Tarih Notları</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function MockupChat() {
  return (
    <div className="flex-1 flex flex-col bg-white">
      {/* Header */}
      <div className="h-14 border-b border-[#eaecf0] flex items-center px-6 shrink-0">
        <h3 className="text-[14px] font-semibold text-[#1a1a1a]">Mitoz vs Mayoz Bölünme</h3>
      </div>
      {/* Messages */}
      <div className="flex-1 p-6 space-y-6 overflow-hidden">
        {/* User Message */}
        <div className="flex justify-end">
          <div className="max-w-[80%] rounded-2xl rounded-tr-sm bg-[#f2f2f1] px-4 py-3 border border-[#eaecf0]">
            <p className="text-[14px] text-[#1a1a1a]">Mitoz ve mayoz bölünme arasındaki temel farklar nelerdir? Kısa bir özet geçebilir misin?</p>
          </div>
        </div>
        {/* Assistant Message */}
        <div className="flex gap-3">
          <div className="w-7 h-7 rounded bg-black flex items-center justify-center shrink-0 mt-1">
            <OrcaLogo className="w-3.5 h-3.5 text-white" />
          </div>
          <div className="max-w-[85%] rounded-2xl rounded-tl-sm bg-white border border-[#eaecf0] px-4 py-3 shadow-sm">
            <p className="text-[14px] leading-relaxed text-[#333333]">
              Elbette. İşte temel farklar:
              <br/><br/>
              • <strong>Mitoz:</strong> Vücut hücrelerinde görülür. Sonucunda genetik olarak birbirinin aynısı 2 diploit (2n) hücre oluşur. Büyüme ve onarımı sağlar.
              <br/>
              • <strong>Mayoz:</strong> Üreme ana hücrelerinde görülür. Sonucunda genetik olarak farklı 4 haploit (n) hücre oluşur. Üremeyi (gamet oluşumunu) sağlar.
            </p>
          </div>
        </div>
      </div>
      {/* Input */}
      <div className="p-4 shrink-0">
        <div className="flex items-center gap-2 rounded-2xl border border-[#eaecf0] bg-white px-3 py-2 shadow-sm">
          <input type="text" disabled placeholder="Soru sor veya belge yükle..." className="flex-1 bg-transparent text-[14px] text-[#888888] outline-none px-2" />
          <div className="w-8 h-8 rounded-full bg-[#1a1a1a] flex items-center justify-center">
             <ArrowUp className="w-4 h-4 text-white" />
          </div>
        </div>
      </div>
    </div>
  );
}

function MockupWiki() {
  return (
    <div className="w-[380px] shrink-0 border-l border-[#eaecf0] bg-[#faf9f6] flex flex-col hidden lg:flex">
      {/* Header */}
      <div className="px-6 py-4 border-b border-[#eaecf0] shrink-0">
        <div className="text-[11px] font-medium text-[#888888] mb-1">Biyoloji 101 / Hücre Bölünmesi</div>
        <h2 className="text-[16px] font-bold text-[#1a1a1a] flex items-center gap-2">
          <BookOpen className="w-4 h-4 text-[#666666]" />
          Mitoz Bölünme Özeti
        </h2>
      </div>
      {/* Content */}
      <div className="flex-1 p-6 space-y-5 overflow-hidden">
        <p className="text-[13px] leading-relaxed text-[#444444]">
          Mitoz, ökaryotik hücrelerin bölünerek iki özdeş yavru hücre oluşturduğu hücresel süreçtir. 
        </p>
        <div className="bg-amber-50 border border-amber-200/60 rounded-xl p-4">
           <div className="flex items-center gap-2 mb-2">
              <Lightbulb className="w-4 h-4 text-amber-600" />
              <span className="text-[12px] font-bold text-amber-800">Önemli Not</span>
           </div>
           <p className="text-[12px] text-amber-900 leading-relaxed">
             Mitoz sonucu oluşan hücrelerin kromozom sayısı ata hücre ile aynıdır. Çeşitlilik sağlamaz.
           </p>
        </div>
        <ul className="space-y-2">
          <li className="flex items-start gap-2">
            <CheckCircle2 className="w-4 h-4 text-[#888888] shrink-0 mt-0.5" />
            <span className="text-[13px] text-[#444444]">İnterfaz: Hazırlık evresi</span>
          </li>
          <li className="flex items-start gap-2">
            <CheckCircle2 className="w-4 h-4 text-[#888888] shrink-0 mt-0.5" />
            <span className="text-[13px] text-[#444444]">Profaz: Kromatin iplikler kromozoma dönüşür.</span>
          </li>
        </ul>
      </div>
      {/* Tutor Panel Bottom */}
      <div className="h-[140px] border-t border-[#eaecf0] bg-white p-4 flex flex-col shrink-0">
        <div className="flex items-center gap-2 mb-3">
          <MessageSquare className="w-4 h-4 text-[#666666]" />
          <span className="text-[12px] font-semibold text-[#1a1a1a]">Belge Ajanı</span>
        </div>
        <p className="text-[12px] text-[#666666] mb-3">Bu belge hakkında aklınıza takılan bir şey var mı?</p>
        <div className="flex items-center gap-2 rounded-xl border border-[#eaecf0] bg-[#faf9f6] px-3 py-1.5 mt-auto">
          <span className="flex-1 text-[12px] text-[#888888]">Belgeye soru sor...</span>
          <div className="w-6 h-6 rounded-full bg-black flex items-center justify-center">
             <ArrowUp className="w-3 h-3 text-white" />
          </div>
        </div>
      </div>
    </div>
  );
}

function MainAppMockup() {
  return (
    <div className="mx-auto max-w-6xl overflow-hidden rounded-2xl border border-[#eaecf0] bg-white shadow-[0_8px_30px_rgba(0,0,0,0.04)] flex h-[650px] text-left">
      <MockupSidebar />
      <MockupChat />
      <MockupWiki />
    </div>
  );
}

/* ─── Hero Section ──────────────────────────────────────────────────────────── */
function Hero() {
  return (
    <section className="pt-32 pb-16 px-6 text-center max-w-5xl mx-auto">
      <Reveal>
        <h1 className="text-[44px] md:text-[64px] font-bold leading-[1.1] tracking-tight text-[#0d0d0d]">
          Sıradan bir sohbet botu değil,<br/>
          kalıcı bir öğrenme belleği.
        </h1>
      </Reveal>
      <Reveal delay={0.1}>
        <p className="mx-auto mt-6 max-w-[600px] text-[17px] leading-[1.6] text-[#555555]">
          Orka, yüklediğiniz belgelerden ve yaptığınız sohbetlerden otomatik bir Wiki çıkarır, anladığınız konuları haritalandırır ve eksiklerinizi tespit eder.
        </p>
      </Reveal>
      <Reveal delay={0.2}>
        <div className="mt-8 flex flex-wrap items-center justify-center gap-3">
          <Link
            href="/login"
            className="inline-flex items-center gap-2 rounded-full bg-[#0d0d0d] px-6 py-3 text-[14px] font-semibold text-white transition hover:bg-black shadow-sm"
          >
            Ücretsiz Başla
          </Link>
          <a
            href="#nasıl-çalışır"
            className="inline-flex items-center gap-2 rounded-full border border-[#eaecf0] bg-white px-6 py-3 text-[14px] font-medium text-[#333333] transition hover:bg-[#faf9f6]"
          >
            Nasıl Çalışır?
          </a>
        </div>
      </Reveal>
      
      <div className="mt-20">
        <Reveal delay={0.3}>
          <MainAppMockup />
        </Reveal>
      </div>
    </section>
  );
}

/* ─── Workflow Section (Academy Style) ──────────────────────────────────────── */
function WorkflowSection() {
  const steps = [
    {
      title: "1. Her Şey Doğal Bir Sohbetle Başlar",
      desc: "İster PDF yükleyin, ister doğrudan bir konuyu sorun. Klasik bir sohbet arayüzü gibi başlarsınız, karmaşık menüler veya panellerle uğraşmazsınız.",
    },
    {
      title: "2. Wiki Otomatik Oluşturulur",
      desc: "Siz sohbet ettikçe, arka planda Orka bilgileri derler. Önemli tanımları, kuralları ve kod bloklarını sağ taraftaki kalıcı Wiki sayfanıza aktarır.",
    },
    {
      title: "3. Anında Pratik ve Kontrol",
      desc: "Ajan sadece size cevap vermekle kalmaz, öğrendiklerinizi pekiştirmeniz için size araya sıkıştırılmış ufak quizler sunar.",
    }
  ];

  return (
    <section id="nasıl-çalışır" className="py-24 px-6 bg-[#faf9f6] border-y border-[#eaecf0]">
      <div className="max-w-5xl mx-auto">
        <Reveal>
          <h2 className="text-[32px] font-bold tracking-tight text-[#0d0d0d] mb-16">
            Öğrenme sürecinizi nasıl belgeliyoruz?
          </h2>
        </Reveal>

        <div className="grid md:grid-cols-2 gap-16 items-start">
          <div className="space-y-12">
            {steps.map((step, i) => (
              <Reveal key={i} delay={i * 0.1}>
                <div>
                  <h3 className="text-[18px] font-bold text-[#1a1a1a] mb-2">{step.title}</h3>
                  <p className="text-[15px] leading-relaxed text-[#555555]">{step.desc}</p>
                </div>
              </Reveal>
            ))}
          </div>
          
          <Reveal delay={0.3} className="sticky top-24 rounded-2xl border border-[#eaecf0] bg-white p-6 shadow-sm hidden md:block">
             <div className="space-y-4">
                <div className="h-6 w-1/3 bg-[#f2f2f1] rounded-md mb-6"></div>
                <div className="h-4 w-full bg-[#f2f2f1] rounded-sm"></div>
                <div className="h-4 w-5/6 bg-[#f2f2f1] rounded-sm"></div>
                <div className="h-4 w-4/6 bg-[#f2f2f1] rounded-sm"></div>
                <div className="h-24 w-full border border-amber-200/50 bg-amber-50/50 rounded-xl mt-6"></div>
             </div>
          </Reveal>
        </div>
      </div>
    </section>
  );
}

/* ─── Features Grid ─────────────────────────────────────────────────────────── */
function FeaturesSection() {
  const features = [
    {
      icon: Network,
      title: "Bilgi Grafiği",
      desc: "Öğrendiğiniz tüm konular ağaç yapısı olarak birbiriyle bağlanır. Neyi bildiğinizi net görürsünüz."
    },
    {
      icon: MessageSquare,
      title: "Belge Ajanı",
      desc: "Wiki içindeki dokümanlara özel ajan. Metnin spesifik bir paragrafı hakkında anında soru sorun."
    },
    {
      icon: CheckCircle2,
      title: "Sınav Modu",
      desc: "Hazır olduğunuzda, sadece o ana kadar işlediğiniz konuları kapsayan rastgele mini denemeler oluşturun."
    }
  ];

  return (
    <section id="özellikler" className="py-24 px-6">
      <div className="max-w-5xl mx-auto">
        <Reveal>
          <div className="grid md:grid-cols-3 gap-8">
            {features.map((feat, i) => {
              const Icon = feat.icon;
              return (
                <div key={i} className="pt-6">
                  <div className="w-10 h-10 rounded bg-[#f2f2f1] flex items-center justify-center mb-5 border border-[#eaecf0]">
                    <Icon className="w-5 h-5 text-[#333333]" />
                  </div>
                  <h3 className="text-[16px] font-bold text-[#1a1a1a] mb-2">{feat.title}</h3>
                  <p className="text-[14px] leading-[1.6] text-[#666666]">{feat.desc}</p>
                </div>
              );
            })}
          </div>
        </Reveal>
      </div>
    </section>
  );
}

/* ─── FAQ Section ───────────────────────────────────────────────────────────── */
function FAQ() {
  const faqs = [
    {
      q: "Orka ne tür belgeleri destekler?",
      a: "Şu an için PDF, TXT dosyalarını ve doğrudan yapıştırdığınız Markdown veya düz metinleri destekliyoruz."
    },
    {
      q: "Wiki notları dışa aktarılabilir mi?",
      a: "Evet, oluşturulan tüm Wiki sayfalarını standart Markdown formatında bilgisayarınıza indirebilirsiniz."
    },
    {
      q: "Ücretli mi?",
      a: "Kayıt olmak ve temel kullanım tamamen ücretsizdir. Kredi kartı gerekmez."
    }
  ];

  const [open, setOpen] = useState<number | null>(null);

  return (
    <section id="sss" className="py-24 px-6 bg-[#faf9f6] border-t border-[#eaecf0]">
      <div className="max-w-3xl mx-auto">
        <Reveal>
          <h2 className="text-[24px] font-bold text-[#0d0d0d] mb-8">Sıkça Sorulan Sorular</h2>
          <div className="space-y-4">
            {faqs.map((faq, i) => (
              <div key={i} className="border border-[#eaecf0] bg-white rounded-xl overflow-hidden transition-all duration-200">
                <button
                  onClick={() => setOpen(open === i ? null : i)}
                  className="w-full text-left px-5 py-4 flex items-center justify-between text-[15px] font-medium text-[#1a1a1a]"
                >
                  {faq.q}
                  <ChevronDown className={`w-4 h-4 text-[#888888] transition-transform ${open === i ? "rotate-180" : ""}`} />
                </button>
                {open === i && (
                  <div className="px-5 pb-4 text-[14px] text-[#555555] leading-relaxed">
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

/* ─── Footer ────────────────────────────────────────────────────────────────── */
function Footer() {
  return (
    <footer className="border-t border-[#eaecf0] bg-white px-6 py-12">
      <div className="mx-auto max-w-5xl flex flex-col md:flex-row items-center justify-between gap-6">
        <div className="flex items-center gap-2">
          <span className="grid h-6 w-6 place-items-center rounded bg-[#1a1a1a]">
            <OrcaLogo className="h-3 w-3 text-white" />
          </span>
          <span className="text-[14px] font-bold text-[#1a1a1a]">Orka</span>
        </div>
        <p className="text-[13px] text-[#888888]">
          © 2026 Orka Inc. Tüm hakları saklıdır.
        </p>
        <div className="flex items-center gap-6">
          <a href="#" className="text-[13px] font-medium text-[#666666] hover:text-[#1a1a1a]">X (Twitter)</a>
          <a href="#" className="text-[13px] font-medium text-[#666666] hover:text-[#1a1a1a]">GitHub</a>
          <a href="#" className="text-[13px] font-medium text-[#666666] hover:text-[#1a1a1a]">Destek</a>
        </div>
      </div>
    </footer>
  );
}

/* ─── Page ──────────────────────────────────────────────────────────────────── */
export default function Landing() {
  return (
    <div className="min-h-screen bg-white text-[#1a1a1a] font-sans selection:bg-amber-100 selection:text-amber-900">
      <Navbar />
      <Hero />
      <WorkflowSection />
      <FeaturesSection />
      <FAQ />
      <Footer />
    </div>
  );
}
