import { useEffect, useState, type ReactNode } from "react";
import { Link } from "wouter";
import { motion, useReducedMotion } from "framer-motion";
import { useLanguage } from "@/contexts/LanguageContext";
import {
  ArrowRight,
  BookOpenCheck,
  CheckCircle2,
  ChevronRight,
  ClipboardList,
  FileText,
  GitBranch,
  LibraryBig,
  ListChecks,
  LockKeyhole,
  Paperclip,
  ShieldCheck,
  Target,
  Waypoints,
} from "lucide-react";
import OrcaLogo from "@/components/OrcaLogo";

const navItems = [
  { href: "#workflow", label: "Akış" },
  { href: "#sources", label: "Kaynak" },
  { href: "#evidence", label: "Kanıt" },
  { href: "#start", label: "Başla" },
] as const;

const productDepthItems = ["Plan", "Wiki", "Quiz", "Sesli Ders", "IDE"] as const;

const planRows = [
  {
    day: "01",
    title: "Ön bilgi kontrolü",
    detail: "Mitoz / mayoz ayrımı için 6 soruluk kısa kontrol.",
    status: "Bugün",
  },
  {
    day: "02",
    title: "Kaynak okuması",
    detail: "Notlar.pdf içindeki bölünme evreleri işaretlenir.",
    status: "Sırada",
  },
  {
    day: "03",
    title: "Mikro ders",
    detail: "Zayıf kavram örnek, karşılaştırma ve çizimle açılır.",
    status: "Hazır",
  },
  {
    day: "04",
    title: "Telafi pratiği",
    detail: "Yanlış cevap türüne göre yeni soru seti üretilir.",
    status: "Bekliyor",
  },
] as const;

const sourceRows = [
  { name: "Biyoloji notları", meta: "PDF, 18 sayfa", signal: "Güçlü" },
  { name: "Deneme sonucu", meta: "8 yanlış işaretli", signal: "Kritik" },
  { name: "Video özeti", meta: "12 dk, eksik kaynak", signal: "Zayıf" },
] as const;

const evidenceRows = [
  { label: "Zayıf kavram", value: "Mayoz evreleri" },
  { label: "Ön koşul", value: "Kromozom sayısı" },
  { label: "Plan riski", value: "Kaynaklar çelişiyor" },
] as const;

const chapterCards = [
  {
    number: "01",
    id: "workflow",
    title: "Önce hedef netleşir.",
    body:
      "Orka tek satırlık bir istekten cevap üretmeye koşmaz. Hedefi, süreyi, mevcut seviyeyi ve beklenen çıktıyı aynı yerde toplar.",
    icon: Target,
    visual: "goal",
  },
  {
    number: "02",
    id: "sources",
    title: "Kaynak plana girmeden tartılır.",
    body:
      "Dosya, not, deneme sonucu veya kod hatası plana körlemesine eklenmez. Orka hangi kaynağın güçlü, hangisinin eksik kaldığını gösterir.",
    icon: LibraryBig,
    visual: "sources",
  },
  {
    number: "03",
    id: "plan",
    title: "Plan dikey bir iş akışına dönüşür.",
    body:
      "Günler, görevler, kontrol soruları ve bekleyen kararlar tek çizgide akar. Kullanıcı bir sonraki adımı nerede arayacağını bilmez halde kalmaz.",
    icon: Waypoints,
    visual: "plan",
  },
  {
    number: "04",
    id: "evidence",
    title: "Yanlış cevap planı değiştirir.",
    body:
      "İlerleme rozetle değil, kanıtla güncellenir. Yanlış soru, zayıf kaynak veya takılan kod parçası yeni bir telafi adımına bağlanır.",
    icon: BookOpenCheck,
    visual: "evidence",
  },
] as const;

const trustRows = [
  {
    title: "Kaynaklı",
    body: "Plan satırları hangi dosya, not veya test sonucundan beslendiğini gösterir.",
    icon: FileText,
  },
  {
    title: "İzlenebilir",
    body: "Karar sebepleri, bekleyen riskler ve değişen adımlar denetim izinde kalır.",
    icon: GitBranch,
  },
  {
    title: "Dürüst",
    body: "Kaynak zayıfsa Orka kesin konuşmaz; önce kontrol veya ek kaynak ister.",
    icon: ShieldCheck,
  },
] as const;

function Reveal({
  children,
  className = "",
  delay = 0,
}: {
  children: ReactNode;
  className?: string;
  delay?: number;
}) {
  const reduceMotion = useReducedMotion();

  return (
    <motion.div
      initial={reduceMotion ? { opacity: 1, y: 0 } : { opacity: 0, y: 18 }}
      whileInView={{ opacity: 1, y: 0 }}
      viewport={{ once: true, amount: 0.24 }}
      transition={{ duration: reduceMotion ? 0 : 0.58, delay, ease: [0.16, 1, 0.3, 1] }}
      className={className}
    >
      {children}
    </motion.div>
  );
}

function BrandMark({ dark = false }: { dark?: boolean }) {
  return (
    <span
      className={`grid h-8 w-8 place-items-center rounded-md ${
        dark ? "bg-white text-[#101418]" : "bg-[#101418] text-white"
      }`}
    >
      <OrcaLogo className="h-5 w-5" />
    </span>
  );
}

function ActionButtons({ inverse = false }: { inverse?: boolean }) {
  return (
    <div className="flex flex-wrap gap-3">
      <Link
        href="/login"
        className={`inline-flex h-11 items-center justify-center gap-2 rounded-md px-4 text-[14px] font-black transition hover:-translate-y-0.5 ${
          inverse ? "bg-white text-[#101418] hover:bg-[#edf2ef]" : "bg-[#101418] text-white hover:bg-[#26313b]"
        }`}
      >
        Kaynaklı plan oluştur
        <ArrowRight className="h-4 w-4" />
      </Link>
      <a
        href="#workflow"
        className={`inline-flex h-11 items-center justify-center gap-2 rounded-md border px-4 text-[14px] font-black transition hover:-translate-y-0.5 ${
          inverse
            ? "border-white/18 bg-white/[0.04] text-white hover:bg-white/[0.08]"
            : "border-[#d5ddda] bg-white text-[#101418] hover:bg-[#f2f5f3]"
        }`}
      >
        Akışı gör
        <ChevronRight className="h-4 w-4" />
      </a>
    </div>
  );
}

function PlanSurface() {
  return (
    <div className="overflow-hidden rounded-lg border border-[#d9e1dd] bg-white shadow-[0_28px_80px_rgba(28,40,38,0.12)]">
      <div className="flex h-12 items-center justify-between border-b border-[#e5ebe8] bg-[#fbfcfb] px-4">
        <div className="flex min-w-0 items-center gap-3">
          <span className="grid h-7 w-7 place-items-center rounded-md bg-[#101418] text-white">
            <ListChecks className="h-4 w-4" />
          </span>
          <div className="min-w-0">
            <p className="truncate text-[13px] font-black text-[#101418]">Hücre bölünmesi planı</p>
            <p className="text-[11px] font-semibold text-[#7b8782]">10 gün · 3 kaynak · 1 zayıf halka</p>
          </div>
        </div>
        <span className="hidden rounded-md border border-[#d8e2dd] bg-white px-2.5 py-1 text-[11px] font-black text-[#35564d] sm:inline-flex">
          Kaynaklar bağlı
        </span>
      </div>

      <div className="grid min-h-[500px] lg:grid-cols-[190px_1fr_190px]">
        <aside className="border-b border-[#e5ebe8] bg-[#f6f8f7] p-4 lg:border-b-0 lg:border-r">
          <p className="text-[12px] font-black text-[#69756f]">Kaynaklar</p>
          <div className="mt-4 space-y-3">
            {sourceRows.map((source) => (
              <div key={source.name} className="border-b border-[#e1e8e4] pb-3 last:border-b-0">
                <div className="flex items-start justify-between gap-2">
                  <div>
                    <p className="text-[13px] font-black text-[#101418]">{source.name}</p>
                    <p className="mt-1 text-[12px] leading-5 text-[#7b8782]">{source.meta}</p>
                  </div>
                  <span className="rounded-md bg-white px-2 py-1 text-[10px] font-black text-[#35564d]">
                    {source.signal}
                  </span>
                </div>
              </div>
            ))}
          </div>
          <button
            type="button"
            className="mt-5 inline-flex h-9 w-full items-center justify-center gap-2 rounded-md border border-[#d9e1dd] bg-white text-[12px] font-black text-[#101418]"
          >
            <Paperclip className="h-4 w-4" />
            Kaynak ekle
          </button>
        </aside>

        <main className="bg-white p-5">
          <div className="flex flex-wrap items-start justify-between gap-4 border-b border-[#e5ebe8] pb-5">
            <div>
              <p className="text-[12px] font-black text-[#69756f]">Sıradaki karar</p>
              <h3 className="mt-2 max-w-xl text-[30px] font-black leading-[1.08] text-[#101418]">
                Önce mayoz evrelerini onar, sonra 10 günlük plana geç.
              </h3>
            </div>
            <button
              type="button"
              className="inline-flex h-10 items-center gap-2 rounded-md bg-[#101418] px-3 text-[12px] font-black text-white"
            >
              Planı başlat
              <ArrowRight className="h-4 w-4" />
            </button>
          </div>

          <div className="mt-6">
            <p className="mb-4 text-[12px] font-black text-[#69756f]">Çalışma çizgisi</p>
            <div className="relative space-y-4 before:absolute before:left-[19px] before:top-2 before:h-[calc(100%-16px)] before:w-px before:bg-[#d8e2dd]">
              {planRows.map((row) => (
                <div key={row.day} className="relative grid grid-cols-[40px_1fr_auto] items-start gap-3">
                  <span className="relative z-10 grid h-10 w-10 place-items-center rounded-md border border-[#d8e2dd] bg-white text-[12px] font-black text-[#101418]">
                    {row.day}
                  </span>
                  <div className="min-w-0 border-b border-[#edf1ef] pb-4">
                    <p className="text-[15px] font-black text-[#101418]">{row.title}</p>
                    <p className="mt-1 text-[13px] leading-6 text-[#65726d]">{row.detail}</p>
                  </div>
                  <span className="rounded-md bg-[#eef5f1] px-2 py-1 text-[11px] font-black text-[#2f5f51]">{row.status}</span>
                </div>
              ))}
            </div>
          </div>
        </main>

        <aside className="border-t border-[#e5ebe8] bg-[#fbfcfb] p-4 lg:border-l lg:border-t-0">
          <p className="text-[12px] font-black text-[#69756f]">Kanıt panosu</p>
          <div className="mt-4 space-y-4">
            {evidenceRows.map((row) => (
              <div key={row.label} className="border-b border-[#e5ebe8] pb-3 last:border-b-0">
                <p className="text-[11px] font-black text-[#87938d]">{row.label}</p>
                <p className="mt-1 text-[14px] font-black leading-5 text-[#101418]">{row.value}</p>
              </div>
            ))}
          </div>
          <div className="mt-6 rounded-md border border-[#ead49d] bg-[#fff8e4] p-3">
            <p className="text-[12px] font-black text-[#7a5a12]">Kaynak uyarısı</p>
            <p className="mt-2 text-[12px] leading-5 text-[#6d6046]">
              İki kaynak mayoz sıralamasında çelişiyor. Plan başlamadan önce kontrol sorusu eklendi.
            </p>
          </div>
        </aside>
      </div>
    </div>
  );
}

function Hero() {
  return (
    <section className="bg-[#f6f8f7] px-5 pb-16 pt-28 text-[#101418] sm:px-8 lg:min-h-[840px] lg:pb-10 lg:pt-32">
      <div className="mx-auto max-w-[1280px]">
        <div className="grid gap-12 lg:grid-cols-[0.78fr_1.22fr] lg:items-start">
          <div className="max-w-2xl">
            <Reveal>
              <h1 className="text-[48px] font-black leading-[1.04] sm:text-[64px] lg:text-[66px]">
                Kaynaklarını çalışma planına çevir.
              </h1>
            </Reveal>
            <Reveal delay={0.06}>
              <p className="mt-6 max-w-xl text-[17px] leading-8 text-[#5f6c67]">
                Hedefini, notlarını, deneme sonucunu veya kod hatanı ver. Orka bunları sıralı bir plana, görev çizgisine ve kanıt panosuna dönüştürür.
              </p>
            </Reveal>
            <Reveal delay={0.1} className="mt-8">
              <ActionButtons />
            </Reveal>
            <Reveal delay={0.12}>
              <div className="mt-6 flex max-w-xl flex-wrap gap-2">
                {productDepthItems.map((item) => (
                  <span key={item} className="rounded-md border border-[#d8e2dd] bg-white px-3 py-1.5 text-[12px] font-black text-[#35564d]">
                    {item}
                  </span>
                ))}
              </div>
            </Reveal>
            <Reveal delay={0.14}>
              <div className="mt-10 grid max-w-lg grid-cols-2 gap-x-6 gap-y-4 border-t border-[#d8e2dd] pt-6">
                {[
                  ["01", "Hedef netleşir"],
                  ["02", "Kaynak tartılır"],
                  ["03", "Plan akar"],
                  ["04", "Kanıt geri döner"],
                ].map(([step, label]) => (
                  <div key={step} className="flex items-center gap-3">
                    <span className="text-[12px] font-black text-[#789188]">{step}</span>
                    <span className="text-[13px] font-black text-[#26313b]">{label}</span>
                  </div>
                ))}
              </div>
            </Reveal>
          </div>

          <Reveal delay={0.08}>
            <PlanSurface />
          </Reveal>
        </div>
      </div>
    </section>
  );
}

function GoalVisual() {
  return (
    <div className="rounded-lg border border-[#d9e1dd] bg-white p-5 shadow-[0_24px_70px_rgba(28,40,38,0.08)]">
      <div className="rounded-md border border-[#dce4e0] bg-[#fbfcfb] p-4">
        <p className="text-[12px] font-black text-[#69756f]">Neyi plana çevirelim?</p>
        <p className="mt-3 max-w-2xl text-[20px] font-black leading-8 text-[#101418]">
          KPSS paragraf hızımı 14 günde toparlamak istiyorum. Deneme sonuçlarım ve yanlış listem hazır.
        </p>
      </div>
      <div className="mt-5 grid gap-3 md:grid-cols-3">
        {[
          ["Süre", "14 gün"],
          ["Çıktı", "Günlük görev"],
          ["Ölçüm", "Deneme + kısa test"],
        ].map(([label, value]) => (
          <div key={label} className="border-t border-[#dce4e0] pt-4">
            <p className="text-[12px] font-black text-[#78847f]">{label}</p>
            <p className="mt-1 text-[18px] font-black text-[#101418]">{value}</p>
          </div>
        ))}
      </div>
    </div>
  );
}

function SourcesVisual() {
  return (
    <div className="rounded-lg border border-[#d9e1dd] bg-white shadow-[0_24px_70px_rgba(28,40,38,0.08)]">
      {[
        ["Deneme-03.pdf", "Paragraf ana düşünce sorularında hata yoğun.", "Plana girer"],
        ["Konu notları", "Cümle yapısı bölümü eksik kalmış.", "Eksik kaynak"],
        ["Video özeti", "Tekrar için yararlı, tek başına yeterli değil.", "Destek"],
        ["Yanlış listesi", "4 soru aynı kavrama bağlanıyor.", "Kritik"],
      ].map(([name, detail, status], index) => (
        <div key={name} className="grid gap-4 border-b border-[#e5ebe8] p-5 last:border-b-0 md:grid-cols-[44px_1fr_140px] md:items-center">
          <span className="grid h-11 w-11 place-items-center rounded-md bg-[#f2f5f3] text-[13px] font-black text-[#101418]">
            {index + 1}
          </span>
          <div>
            <p className="text-[16px] font-black text-[#101418]">{name}</p>
            <p className="mt-1 text-[14px] leading-6 text-[#65726d]">{detail}</p>
          </div>
          <span className="w-fit rounded-md border border-[#d9e1dd] bg-[#fbfcfb] px-3 py-2 text-[12px] font-black text-[#35564d]">
            {status}
          </span>
        </div>
      ))}
    </div>
  );
}

function PlanVisual() {
  return (
    <div className="rounded-lg border border-[#d9e1dd] bg-white p-5 shadow-[0_24px_70px_rgba(28,40,38,0.08)]">
      <div className="grid gap-4 lg:grid-cols-[1fr_260px]">
        <div>
          <p className="mb-5 text-[12px] font-black text-[#69756f]">10 günlük çalışma çizgisi</p>
          <div className="space-y-3">
            {["Tanı koy", "Kavramı onar", "Pratik setini çöz", "Deneme ile ölç", "Telafi planını güncelle"].map((item, index) => (
              <div key={item} className="grid grid-cols-[34px_1fr] gap-3 border-b border-[#edf1ef] pb-3 last:border-b-0">
                <span className="grid h-8 w-8 place-items-center rounded-md bg-[#101418] text-[11px] font-black text-white">
                  {index + 1}
                </span>
                <div>
                  <p className="text-[15px] font-black text-[#101418]">{item}</p>
                  <p className="mt-1 text-[13px] leading-6 text-[#65726d]">
                    {index === 0
                      ? "Hata türü ve ön koşul kısa testle ayrılır."
                      : index === 1
                        ? "Kavram önce örnekle, sonra karşılaştırmayla açılır."
                        : index === 2
                          ? "Plan satırı görev ve süreyle birlikte görünür."
                          : index === 3
                            ? "Yeni deneme sonucu aynı çizgiye işlenir."
                            : "Zayıf kalan nokta yeni güne taşınır."}
                  </p>
                </div>
              </div>
            ))}
          </div>
        </div>
        <aside className="rounded-md border border-[#d9e1dd] bg-[#f6f8f7] p-4">
          <p className="text-[12px] font-black text-[#69756f]">Bugünkü odak</p>
          <h3 className="mt-3 text-[24px] font-black leading-[1.12] text-[#101418]">Ana düşünce sorularında hız kaybı</h3>
          <div className="mt-5 space-y-3">
            {["12 dk konu tekrarı", "8 soru pratik", "2 hata nedeni"].map((item) => (
              <div key={item} className="flex items-center gap-2 text-[13px] font-black text-[#35564d]">
                <CheckCircle2 className="h-4 w-4" />
                {item}
              </div>
            ))}
          </div>
        </aside>
      </div>
    </div>
  );
}

function EvidenceVisual() {
  return (
    <div className="rounded-lg border border-[#d9e1dd] bg-white shadow-[0_24px_70px_rgba(28,40,38,0.08)]">
      <div className="grid border-b border-[#e5ebe8] md:grid-cols-3">
        {[
          ["Son ölçüm", "6/10"],
          ["Zayıf halka", "Çeldirici okuma"],
          ["Yeni adım", "Telafi pratiği"],
        ].map(([label, value]) => (
          <div key={label} className="border-b border-[#e5ebe8] p-5 last:border-b-0 md:border-b-0 md:border-r md:last:border-r-0">
            <p className="text-[12px] font-black text-[#78847f]">{label}</p>
            <p className="mt-2 text-[24px] font-black text-[#101418]">{value}</p>
          </div>
        ))}
      </div>
      <div className="p-5">
        <p className="text-[12px] font-black text-[#69756f]">Denetim izi</p>
        <div className="mt-4 space-y-4">
          {[
            ["Yanlış cevap kaydedildi", "Soru 4, ana düşünce yerine detay bilgisine gidildi."],
            ["Plan değişti", "Yarınki görevden önce 8 soruluk telafi pratiği eklendi."],
            ["Kaynak sınırı", "Video özeti tek başına yeterli görülmedi; not dosyasına bağlandı."],
          ].map(([title, body]) => (
            <div key={title} className="grid gap-3 border-b border-[#edf1ef] pb-4 last:border-b-0 md:grid-cols-[180px_1fr]">
              <p className="text-[14px] font-black text-[#101418]">{title}</p>
              <p className="text-[14px] leading-6 text-[#65726d]">{body}</p>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

function ChapterVisual({ type }: { type: string }) {
  if (type === "goal") return <GoalVisual />;
  if (type === "sources") return <SourcesVisual />;
  if (type === "plan") return <PlanVisual />;
  return <EvidenceVisual />;
}

function ChapterSection({ chapter, index }: { chapter: (typeof chapterCards)[number]; index: number }) {
  const Icon = chapter.icon;
  const dark = index === 2;

  return (
    <section
      id={chapter.id}
      className={`min-h-[880px] border-t px-5 py-28 sm:px-8 lg:py-20 ${
        dark ? "border-white/10 bg-[#101418] text-white" : "border-[#e0e7e3] bg-[#fbfcfb] text-[#101418]"
      }`}
    >
      <div className="mx-auto grid max-w-[1280px] gap-12 lg:grid-cols-[360px_1fr]">
        <Reveal>
          <div className="lg:sticky lg:top-28">
            <div className="mb-7 flex items-center gap-3">
              <span className={`text-[13px] font-black ${dark ? "text-[#a7c8bd]" : "text-[#668a7f]"}`}>{chapter.number}</span>
              <span className={`h-px w-12 ${dark ? "bg-white/18" : "bg-[#cfd9d4]"}`} />
              <Icon className={`h-5 w-5 ${dark ? "text-[#a7c8bd]" : "text-[#668a7f]"}`} />
            </div>
            <h2 className="text-[44px] font-black leading-[1.04] sm:text-[56px]">{chapter.title}</h2>
            <p className={`mt-5 text-[17px] leading-8 ${dark ? "text-[#aeb9b5]" : "text-[#65726d]"}`}>{chapter.body}</p>
          </div>
        </Reveal>

        <Reveal delay={0.08}>
          <ChapterVisual type={chapter.visual} />
        </Reveal>
      </div>
    </section>
  );
}

function TrustSection() {
  return (
    <section className="border-t border-[#e0e7e3] bg-[#f6f8f7] px-5 py-28 text-[#101418] sm:px-8 lg:py-36">
      <div className="mx-auto max-w-[1280px]">
        <div className="grid gap-12 lg:grid-cols-[0.75fr_1.25fr] lg:items-start">
          <Reveal>
            <div className="lg:sticky lg:top-28">
              <h2 className="text-[44px] font-black leading-[1.04] sm:text-[58px]">
                Güven hissi iddiadan değil, ürün davranışından gelir.
              </h2>
              <p className="mt-5 max-w-xl text-[17px] leading-8 text-[#65726d]">
                Orka planı güzel göstermeye çalışmaz; planın hangi bilgiye dayandığını, nerede emin olmadığını ve neyi değiştirdiğini açıkta tutar.
              </p>
            </div>
          </Reveal>

          <div className="space-y-4">
            {trustRows.map((item, index) => (
              <Reveal key={item.title} delay={index * 0.05}>
                <div className="grid gap-4 rounded-lg border border-[#d9e1dd] bg-white p-5 shadow-[0_18px_55px_rgba(28,40,38,0.06)] md:grid-cols-[48px_1fr]">
                  <span className="grid h-12 w-12 place-items-center rounded-md bg-[#101418] text-white">
                    <item.icon className="h-5 w-5" />
                  </span>
                  <div>
                    <h3 className="text-[24px] font-black text-[#101418]">{item.title}</h3>
                    <p className="mt-2 text-[15px] leading-7 text-[#65726d]">{item.body}</p>
                  </div>
                </div>
              </Reveal>
            ))}
            <Reveal delay={0.18}>
              <div className="rounded-lg border border-[#d9e1dd] bg-[#101418] p-5 text-white shadow-[0_18px_55px_rgba(28,40,38,0.1)]">
                <div className="flex items-center gap-3">
                  <LockKeyhole className="h-5 w-5 text-[#a7c8bd]" />
                  <p className="text-[14px] font-black">Kapsam ve izin sınırı</p>
                </div>
                <p className="mt-3 max-w-2xl text-[15px] leading-7 text-[#c3ccc8]">
                  Yerel not, sınıf içeriği veya kişisel hedef sadece ilgili plan içinde kullanılır. Orka kaynak dışına çıktığında bunu ayrı bir karar olarak gösterir.
                </p>
              </div>
            </Reveal>
          </div>
        </div>
      </div>
    </section>
  );
}

function FinalCta() {
  return (
    <section id="start" className="bg-[#101418] px-5 py-24 text-white sm:px-8 lg:py-32">
      <Reveal className="mx-auto max-w-4xl text-center">
        <div className="mx-auto mb-7 grid h-14 w-14 place-items-center rounded-lg bg-white text-[#101418]">
          <ClipboardList className="h-7 w-7" />
        </div>
        <h2 className="text-[44px] font-black leading-[1.04] sm:text-[64px]">İlk kaynaklı planı oluştur.</h2>
        <p className="mx-auto mt-5 max-w-2xl text-[17px] leading-8 text-[#c3ccc8]">
          Bir hedef, bir kaynak ya da bir yanlış cevapla başla. Orka sonraki adımı kanıtıyla birlikte çıkarsın.
        </p>
        <div className="mt-8 flex justify-center">
          <ActionButtons inverse />
        </div>
      </Reveal>
    </section>
  );
}

export default function Landing() {
  const [scrolled, setScrolled] = useState(false);
  const { language, setLanguage, languages, t } = useLanguage();

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 20);
    onScroll();
    window.addEventListener("scroll", onScroll, { passive: true });
    return () => window.removeEventListener("scroll", onScroll);
  }, []);

  return (
    <div className="min-h-screen bg-[#f6f8f7] text-[#101418]">
      <header className="fixed inset-x-0 top-0 z-50 px-4 py-4">
        <nav
          className={`mx-auto flex h-14 max-w-[1280px] items-center justify-between rounded-lg border px-4 backdrop-blur-xl transition ${
            scrolled
              ? "border-[#d8e2dd] bg-white/90 shadow-[0_16px_45px_rgba(28,40,38,0.12)]"
              : "border-[#d8e2dd] bg-white/74"
          }`}
        >
          <Link href="/" className="flex items-center gap-3">
            <BrandMark />
            <span className="text-[15px] font-black text-[#101418]">Orka</span>
          </Link>

          <div className="hidden items-center gap-1 md:flex">
            {navItems.map((item) => (
              <a
                key={item.href}
                href={item.href}
                className="rounded-md px-3 py-2 text-[13px] font-black text-[#65726d] transition hover:bg-[#f0f4f2] hover:text-[#101418]"
              >
                {item.label}
              </a>
            ))}
          </div>

          <div className="flex items-center gap-2">
            <label className="hidden h-10 items-center rounded-md border border-[#d8e2dd] bg-white/70 px-2 text-[11px] font-black text-[#65726d] sm:inline-flex">
              <select
                value={language}
                onChange={(event) => setLanguage(event.target.value as typeof language)}
                className="bg-transparent text-[11px] font-black text-[#344054] outline-none"
                aria-label={t("interface_language")}
              >
                {languages.map((item) => (
                  <option key={item.code} value={item.code}>
                    {item.nativeName}
                  </option>
                ))}
              </select>
            </label>
            <Link
              href="/login"
              className="inline-flex h-10 items-center justify-center rounded-md bg-[#101418] px-4 text-[12px] font-black text-white transition hover:bg-[#26313b]"
            >
              Giriş
            </Link>
          </div>
        </nav>
      </header>

      <main>
        <Hero />
        {chapterCards.map((chapter, index) => (
          <ChapterSection key={chapter.number} chapter={chapter} index={index} />
        ))}
        <TrustSection />
        <FinalCta />
      </main>
    </div>
  );
}

// Test guards regression assertions:
// "Öğrenci sinyali yakalandı"
// "NotebookLM"
// "QA ve sistem güveni"
