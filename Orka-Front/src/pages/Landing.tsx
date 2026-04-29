import { Link } from "wouter";
import { motion } from "framer-motion";
import {
  ArrowRight,
  BookOpen,
  BrainCircuit,
  CheckCircle2,
  Code2,
  FileText,
  GraduationCap,
  Layers3,
  Mic2,
  Network,
  ShieldCheck,
  Sparkles,
  Target,
  RefreshCw
} from "lucide-react";
import OrcaLogo from "@/components/OrcaLogo";

const fadeUp = {
  initial: { opacity: 0, y: 28 },
  whileInView: { opacity: 1, y: 0 },
  viewport: { once: true, margin: "-80px" },
  transition: { duration: 0.65, ease: "easeOut" as const },
};

const slide = (x: number, delay = 0) => ({
  initial: { opacity: 0, x, y: 18 },
  whileInView: { opacity: 1, x: 0, y: 0 },
  viewport: { once: true, margin: "-70px" },
  transition: { duration: 0.62, delay, ease: "easeOut" as const },
});

const modules = ["Adaptif Plan", "Otonom Wiki", "Dinamik Quiz", "NotebookLM/RAG", "Korteks", "Sesli Sınıf", "Sandbox IDE"];

const featureCards = [
  {
    icon: BrainCircuit,
    title: "Derin Davranış Analizi",
    text: "Doğru/yanlış metriklerini aşar. Cevap verme sürenizden takıldığınız alt becerilere kadar mikrodavranışlarınızı işleyerek zihinsel modelinizin tam haritasını çıkarır.",
    meta: "Mikro-Teşhis: Analitik Zayıflık Tespit Edildi",
    className: "lg:col-span-2 orka-panel",
  },
  {
    icon: FileText,
    title: "Otonom RAG Entegrasyonu",
    text: "Yüklediğiniz PDF'ler, bağlantılar ve kişisel notlarınız vektörel olarak indekslenir. Her cevapta ilgili referansa nokta atışı yapılır.",
    meta: "Multi-Agent: RAG Destekli Veri Sentezi",
    className: "orka-surface",
  },
  {
    icon: Target,
    title: "Algoritmik Beceri Sınaması",
    text: "Ezbere dayalı soru havuzlarını unutun. Yapay zeka, doğrudan zayıf olduğunuz alt beceriyi zorlamak için her seferinde eşsiz bir senaryo üretir.",
    meta: "Dinamik Üretim: LLM Tabanlı Senaryolar",
    className: "orka-muted-panel",
  },
  {
    icon: Network,
    title: "Sürekli Gelişen Nöral Hafıza",
    text: "Öğrendiğiniz, unuttuğunuz veya zorlandığınız her an, kişisel Wiki ağınıza yapısal bir bağlantı olarak kalıcı biçimde kodlanır.",
    meta: "Knowledge Graph: Kişisel Bilgi Ağı",
    className: "lg:col-span-2 orka-surface",
  },
  {
    icon: Mic2,
    title: "Çoklu-Ajan Senkronizasyonu",
    text: "Hoca, Asistan ve Konuk rollerine sahip otonom ajanlar eş zamanlı çalışır. Sorduğunuz anlık bir soru, sınıfın tüm bağlamına entegre olur.",
    meta: "Swarm Intelligence: Senkronize Temsilciler",
    className: "orka-panel",
  },
  {
    icon: Code2,
    title: "Bağlamsal Kod Sandbox'ı",
    text: "İzole bir kod penceresi değil; doğrudan chat ve müfredat ile senkronize çalışan güvenli bir ortam. Hata çıktılarınız anında AI asistanınıza akar.",
    meta: "Real-Time: Çıktı ve Teşhis Akışı",
    className: "orka-muted-panel",
  },
];

const pipeline = [
  "Davranış Analizi",
  "Eksiklerin Tespiti",
  "Müfredat Uyarlama",
  "Kişisel Anlatım",
  "Adaptif Sınama",
  "Wiki Kaydı",
];

const trustItems = [
  ["Proaktif Sistem Doğrulaması", "Tüm API endpoint'leri ve yetkilendirme katmanları, her dağıtım öncesi sentetik ajanlar tarafından otonom testlere tabi tutulur."],
  ["Yapısal Veri Koruması", "LLM'den dönen karmaşık JSON çıktıları, katı şema doğrulayıcılarla (schema validators) kontrol edilerek arayüze kusursuz yansıtılır."],
  ["Ajan Durum Senkronizasyonu", "Swarm içindeki rollerin (Hoca, Asistan) hafıza sızıntısı olmadan ve tam bağlamla sürece katılımı güvence altındadır."],
  ["Güvenli Yürütme Hattı", "Kod analizleri ve hata ayıklama süreçleri, izole bir kapsayıcıda işlenir ve ajanlara doğrudan yapısal veri olarak beslenir."],
];

export default function Landing() {
  return (
    <div className="min-h-screen orka-bg text-[#172033] overflow-x-hidden">
      <div className="pointer-events-none fixed inset-0 mist-grid opacity-35" />
      <nav className="fixed left-0 right-0 top-0 z-50 px-4 py-4">
        <div className="orka-glass mx-auto flex h-14 max-w-6xl items-center justify-between rounded-2xl px-4 sm:px-5">
          <Link href="/" className="flex items-center gap-3">
            <span className="grid h-9 w-9 place-items-center rounded-2xl bg-[#172033] text-white shadow-md shadow-slate-900/10">
              <OrcaLogo className="h-5 w-5" />
            </span>
            <span className="text-sm font-extrabold tracking-tight">Orka AI</span>
          </Link>
          <div className="hidden items-center gap-6 text-xs font-semibold text-[#667085] md:flex">
            <a href="#features" className="transition hover:text-[#172033]">Modüller</a>
            <a href="#flow" className="transition hover:text-[#172033]">Öğrenme Akışı</a>
            <a href="#trust" className="transition hover:text-[#172033]">Sistem Mimarisi</a>
          </div>
          <Link href="/login" className="orka-button rounded-full px-4 py-2 text-xs font-bold">
            Giriş Yap
          </Link>
        </div>
      </nav>

      <main className="relative z-10">
        <section className="mx-auto grid max-w-6xl gap-12 px-5 pb-20 pt-32 lg:grid-cols-[1.02fr_0.98fr] lg:items-center lg:pt-40">
          <motion.div
            initial={{ opacity: 0, y: 26 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.72, ease: "easeOut" }}
          >
            <div className="mb-6 inline-flex items-center gap-2 rounded-full border border-[#526d82]/14 bg-[#f7f4ec]/70 px-3 py-1.5 text-xs font-bold text-[#2d5870] shadow-sm backdrop-blur">
              <Sparkles className="h-3.5 w-3.5" />
              Orka AI v1.0 Yayında • Organization Knowledge Agent
            </div>
            <h1 className="font-display max-w-4xl text-5xl font-bold leading-[0.98] tracking-[-0.055em] text-[#172033] sm:text-6xl lg:text-7xl">
              Öğrenme potansiyelinizi
              <span className="block text-[#4f7485]">otonom bir zeka ağına dönüştürün.</span>
            </h1>
            <p className="mt-6 max-w-xl text-base leading-8 text-[#5f6f7b] sm:text-lg">
              Orka; sıradan bir asistan değil, zihinsel modelinizi öğrenen bir yapay zeka ekosistemidir. Plan, Wiki, Quiz, NotebookLM/RAG, Korteks, Sesli Sınıf ve IDE akışını tek bir nöral hafızada birleştirir. Sadece cevap vermez; düşünme yapınızı analiz edip mükemmelliğe ulaşana kadar sistemi yeniden kurgular.
            </p>
            <div className="mt-6 flex flex-wrap gap-2">
              {modules.map((module) => (
                <span key={module} className="rounded-full border border-[#526d82]/12 bg-[#eef1f3]/70 px-3 py-1.5 text-[11px] font-extrabold text-[#4b5f6b]">
                  {module}
                </span>
              ))}
            </div>
            <div className="mt-8 flex flex-col gap-3 sm:flex-row">
              <Link href="/login" className="orka-button inline-flex items-center justify-center gap-2 rounded-full px-6 py-3 text-sm font-extrabold">
                Öğrenmeye Başla <ArrowRight className="h-4 w-4" />
              </Link>
              <a href="#features" className="inline-flex items-center justify-center gap-2 rounded-full border border-[#526d82]/15 bg-[#f7f4ec]/72 px-6 py-3 text-sm font-bold text-[#344054] shadow-sm backdrop-blur transition hover:-translate-y-0.5 hover:bg-[#f7f4ec]">
                Sistemi Gör
              </a>
            </div>
            <div className="mt-8 grid max-w-xl grid-cols-3 gap-3">
              {[
                ["20", "tanı sorusu"],
                ["24s", "Wiki cache"],
                ["3", "sınıf rolü"],
              ].map(([value, label]) => (
                <div key={label} className="orka-card rounded-2xl px-4 py-3">
                  <p className="text-xl font-extrabold text-[#172033]">{value}</p>
                  <p className="mt-1 text-[11px] font-semibold uppercase tracking-[0.16em] text-[#667085]">{label}</p>
                </div>
              ))}
            </div>
          </motion.div>

          <motion.div
            initial={{ opacity: 0, x: 34, rotate: 1.5 }}
            animate={{ opacity: 1, x: 0, rotate: 0 }}
            transition={{ duration: 0.78, delay: 0.12, ease: "easeOut" }}
            className="relative"
          >
            <div className="absolute -left-10 top-14 h-36 w-36 rounded-full bg-[#b8d4df]/25 blur-2xl" />
            <div className="absolute -right-8 bottom-10 h-40 w-40 rounded-full bg-[#bfd8c8]/22 blur-2xl" />
            <div className="orka-glass relative overflow-hidden rounded-[2rem] p-4 sm:p-5">
              <div className="mb-4 flex items-center gap-2">
                <span className="h-3 w-3 rounded-full bg-[#d9a9a0]" />
                <span className="h-3 w-3 rounded-full bg-[#d9bd79]" />
                <span className="h-3 w-3 rounded-full bg-[#92b79c]" />
                <span className="ml-3 rounded-full bg-[#eef1f3]/76 px-3 py-1 text-[11px] font-bold text-[#667085]">orka.app/studio</span>
              </div>
              <div className="grid gap-4 lg:grid-cols-[0.86fr_1.14fr]">
                <div className="rounded-3xl bg-[#172033] p-4 text-white shadow-lg shadow-slate-900/10">
                  <div className="mb-4 flex items-center gap-2">
                    <OrcaLogo className="h-4 w-4" />
                    <span className="text-xs font-bold">Agent Studio</span>
                  </div>
                  {[
                    ["Plan", "Adaptif Bilişsel Harita"],
                    ["Tutor", "Derin Bağlam Analizi"],
                    ["Quiz", "Algoritmik Zayıflık Testi"],
                    ["Wiki", "Otonom Hafıza İndeksi"],
                    ["Sınıf", "Çoklu-Ajan Senkronizasyonu"],
                  ].map(([item, detail], index) => (
                    <motion.div
                      key={item}
                      initial={{ opacity: 0, x: -10 }}
                      animate={{ opacity: 1, x: 0 }}
                      transition={{ delay: 0.35 + index * 0.09 }}
                      className="mb-2 rounded-2xl bg-white/8 px-3 py-2 text-xs"
                    >
                      <div className="flex items-center justify-between">
                         <span className="font-bold">{item}</span>
                        <span className="h-2 w-2 rounded-full bg-[#a8d8ea]" />
                      </div>
                      <p className="mt-1 text-[11px] text-white/58">{detail}</p>
                    </motion.div>
                  ))}
                </div>
                <div className="space-y-3 rounded-3xl bg-[#eef1f3]/72 p-4">
                  <div className="flex items-start gap-3 rounded-2xl bg-[#f7f4ec]/75 p-3">
                    <BrainCircuit className="mt-1 h-5 w-5 text-[#52768a]" />
                    <div>
                      <p className="text-sm font-extrabold text-[#172033]">Öğrenci sinyali yakalandı</p>
                      <p className="mt-1 text-xs leading-5 text-[#667085]">Mikro-davranış analizi: "Optimizasyon" kavramında yapısal boşluk saptandı. Öğrenme düğümü zayıf olarak işaretlendi ve telafi döngüsü başlatıldı.</p>
                    </div>
                  </div>
                  <div className="grid grid-cols-2 gap-3">
                    <div className="rounded-2xl bg-[#f4ecdc]/82 p-3">
                      <BookOpen className="h-4 w-4 text-[#906c36]" />
                      <p className="mt-3 text-xs font-bold text-[#172033]">Dinamik Telafi Entegre</p>
                      <p className="mt-1 text-[11px] leading-4 text-[#667085]">Zihinsel modele uygun 3 yeni senaryo üretildi.</p>
                    </div>
                    <div className="rounded-2xl bg-[#d9e7de]/72 p-3">
                      <CheckCircle2 className="h-4 w-4 text-[#547c61]" />
                      <p className="mt-3 text-xs font-bold text-[#172033]">Nöral Hafıza Eklendi</p>
                      <p className="mt-1 text-[11px] leading-4 text-[#667085]">"Optimizasyon Açığı" kavramı kişisel Wiki'ye işlendi.</p>
                    </div>
                  </div>
                  <div className="rounded-2xl border border-[#526d82]/12 bg-[#f7f4ec]/64 p-3">
                    <p className="text-[11px] font-black uppercase tracking-[0.16em] text-[#52768a]">Çoklu-Ajan Sentezi</p>
                    <p className="mt-2 text-xs leading-5 text-[#344054]">Modelinizin zayıf noktasını hedef alarak web ve kişisel dokümanlarınızdan sentezlenmiş hibrit açıklama. <span className="font-bold text-[#2d5870]">[wiki:optimizasyon] [web:derin-öğrenme]</span></p>
                  </div>
                </div>
              </div>
            </div>
          </motion.div>
        </section>

        <section id="features" className="mx-auto max-w-6xl px-5 py-20">
          <motion.div {...fadeUp} className="mb-10 max-w-2xl">
            <p className="mb-3 text-xs font-extrabold uppercase tracking-[0.24em] text-[#52768a]">İçerik boş değil, sistem dolu</p>
            <h2 className="font-display text-4xl font-bold text-[#172033] sm:text-5xl">Orka modülleri birbirine öğrenme sinyali taşır.</h2>
            <p className="mt-4 text-sm leading-7 text-[#667085]">Her kart sadece özellik anlatmaz; sistem içinde hangi veriyi ürettiğini ve öğrencinin planına nasıl döndüğünü gösterir.</p>
          </motion.div>
          <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
            {featureCards.map((feature, index) => (
              <motion.div
                key={feature.title}
                {...slide(index % 2 === 0 ? -28 : 28, index * 0.06)}
                className={`group min-h-[230px] rounded-[1.75rem] p-6 transition duration-300 hover:-translate-y-1 hover:shadow-lg ${feature.className}`}
              >
                <div className="mb-8 grid h-12 w-12 place-items-center rounded-2xl bg-[#eef1f3]/80 text-[#52768a] shadow-sm">
                  <feature.icon className="h-5 w-5" />
                </div>
                <h3 className="text-lg font-extrabold tracking-tight text-[#172033]">{feature.title}</h3>
                <p className="mt-3 text-sm leading-7 text-[#667085]">{feature.text}</p>
                <p className="mt-5 rounded-2xl bg-[#eef1f3]/62 px-3 py-2 text-[11px] font-bold text-[#4f6570]">{feature.meta}</p>
              </motion.div>
            ))}
          </div>
        </section>

        <section id="flow" className="mx-auto max-w-6xl px-5 py-20">
          <motion.div {...fadeUp} className="orka-glass overflow-hidden rounded-[2rem] p-6 sm:p-8">
            <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
              <div>
                <p className="text-xs font-extrabold uppercase tracking-[0.24em] text-[#52768a]">Kişiselleştirilmiş Döngü</p>
                <h2 className="font-display mt-3 text-4xl font-bold text-[#172033]">Statik müfredatları unutun; sizi anlayan bir sistem var.</h2>
              </div>
              <p className="max-w-md text-sm leading-7 text-[#667085]">Sistemdeki her etkileşiminiz yapay zeka tarafından işlenerek kişisel bir besleme (feedback) döngüsü yaratır. Orka, sizi rastgele konularda yormak yerine doğrudan geliştirmeye açık olduğunuz becerileri hedefler.</p>
            </div>
            <div className="relative mt-10">
              {/* Arka Plan Bağlantı Çizgisi (Sadece Desktop) */}
              <div className="absolute left-[8%] right-[8%] top-6 hidden h-0.5 bg-gradient-to-r from-transparent via-[#8ba8b5]/40 to-transparent md:block" />

              <div className="flex flex-col md:flex-row md:items-start md:justify-between gap-6 md:gap-4 relative z-10">
                {pipeline.map((step, index) => (
                  <motion.div
                    key={step}
                    initial={{ opacity: 0, y: 18 }}
                    whileInView={{ opacity: 1, y: 0 }}
                    viewport={{ once: true }}
                    transition={{ delay: index * 0.07 }}
                    className="group flex flex-1 flex-col items-center text-center"
                  >
                    <div className="relative mb-4 grid h-12 w-12 place-items-center rounded-full border-[3px] border-white bg-[#d7e6ec] text-sm font-extrabold text-[#2d5870] shadow-sm transition-all duration-300 group-hover:scale-110 group-hover:bg-[#c2dce6]">
                      {index + 1}
                      {/* Döngü İkonu (Sadece son adımda) */}
                      {index === pipeline.length - 1 && (
                        <div className="absolute -right-6 top-1/2 -translate-y-1/2 hidden lg:block">
                          <RefreshCw className="h-4 w-4 text-[#8ba8b5]" />
                        </div>
                      )}
                    </div>

                    <div className="w-full rounded-2xl bg-[#eef1f3]/80 px-2 py-3 transition-colors duration-300 group-hover:bg-[#eaf1f4]">
                      <p className="text-[11px] font-black leading-tight text-[#172033] sm:text-[11.5px]">
                        {step}
                      </p>
                    </div>

                    {/* Mobildeki dikey ok (son adım hariç) */}
                    {index < pipeline.length - 1 && (
                      <div className="mt-4 block md:hidden">
                        <ArrowRight className="h-4 w-4 rotate-90 text-[#8ba8b5]/50" />
                      </div>
                    )}
                    {/* Mobildeki döngü (son adım) */}
                    {index === pipeline.length - 1 && (
                      <div className="mt-5 flex items-center justify-center gap-1.5 text-[10px] font-extrabold uppercase tracking-widest text-[#8ba8b5] md:hidden">
                        <RefreshCw className="h-3 w-3" /> Döngü Tamamlanır
                      </div>
                    )}
                  </motion.div>
                ))}
              </div>
            </div>
          </motion.div>
        </section>

        <section id="trust" className="mx-auto max-w-6xl px-5 py-20">
          <div className="grid gap-6 lg:grid-cols-[0.92fr_1.08fr] lg:items-start">
            <motion.div {...slide(-34)}>
              <p className="mb-3 text-xs font-extrabold uppercase tracking-[0.24em] text-[#52768a]">Güçlü Mimari ve QA</p>
              <h2 className="font-display text-4xl font-bold text-[#172033] sm:text-5xl">Görünmez asistanlarınız, görünür güvenilirlik.</h2>
              <p className="mt-5 text-sm leading-8 text-[#667085]">QA ve sistem güveni, estetik kadar kritik. Çoklu ajanların veri tutarlılığı, JSON yapılarının şema denetimleri ve endpoint kontrolleri, uçtan uca otomatik test süreçleriyle garanti altındadır.</p>
            </motion.div>
            <motion.div {...slide(34, 0.08)} className="orka-card rounded-[2rem] p-5">
              {trustItems.map(([title, text]) => (
                <div key={title} className="mb-3 flex items-start gap-3 rounded-3xl bg-[#eef1f3]/68 p-4 last:mb-0">
                  <span className="grid h-10 w-10 place-items-center rounded-2xl bg-[#172033] text-white">
                    <ShieldCheck className="h-4 w-4" />
                  </span>
                  <div>
                    <p className="text-sm font-extrabold text-[#172033]">{title}</p>
                    <p className="mt-1 text-sm leading-6 text-[#667085]">{text}</p>
                  </div>
                </div>
              ))}
            </motion.div>
          </div>
        </section>

        <section className="px-5 py-20">
          <motion.div {...fadeUp} className="orka-glass mx-auto max-w-4xl rounded-[2.25rem] p-8 text-center sm:p-12">
            <Layers3 className="mx-auto mb-5 h-9 w-9 text-[#52768a]" />
            <h2 className="font-display text-4xl font-bold text-[#172033] sm:text-5xl">Sizi tanıyan, sizinle gelişen bir zeka.</h2>
            <p className="mx-auto mt-4 max-w-2xl text-sm leading-8 text-[#667085]">Sistemdeki her etkileşiminiz, yapay zekayı sadece size özel bir eğitmene dönüştürmek için sürekli bir geri bildirim döngüsü yaratır. Sınırlarınızı yeniden keşfedin.</p>
            <Link href="/login" className="orka-button mt-8 inline-flex items-center gap-2 rounded-full px-7 py-3 text-sm font-extrabold">
              Orka'yı Başlat <GraduationCap className="h-4 w-4" />
            </Link>
          </motion.div>
        </section>
      </main>
    </div>
  );
}
