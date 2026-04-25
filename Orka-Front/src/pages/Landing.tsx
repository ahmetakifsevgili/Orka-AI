/*
 * Design: "Sessiz Lüks" — Premium landing page.
 * Inspired by Linear.app hero, Vercel depth, Stripe premium feel.
 * Monochrome zinc palette. Typography-driven hierarchy.
 * Sections: Hero, Features, How It Works, CTA.
 */

import { Link } from "wouter";
import { motion } from "framer-motion";
import { ArrowRight, BookOpen, Brain, Target, Layers, Zap, Shield } from "lucide-react";
import OrcaLogo from "@/components/OrcaLogo";

const HERO_BG = "https://d2xsxph8kpxj0f.cloudfront.net/310519663534340404/Tdyfs3EUSoXDGihDvec6L4/hero-abstract-CJpjj4mLRnNXJor3rRcL7z.webp";
const FEATURE_1 = "https://d2xsxph8kpxj0f.cloudfront.net/310519663534340404/Tdyfs3EUSoXDGihDvec6L4/landing-feature-1-Sf6RHeHu3cSRac9MjeHesk.webp";
const FEATURE_2 = "https://d2xsxph8kpxj0f.cloudfront.net/310519663534340404/Tdyfs3EUSoXDGihDvec6L4/landing-feature-2-3qxTjA6Xr9goC7xUTxTr4Z.webp";
const FEATURE_3 = "https://d2xsxph8kpxj0f.cloudfront.net/310519663534340404/Tdyfs3EUSoXDGihDvec6L4/landing-feature-3-KXEMA63jg54Bu92y5UBssW.webp";

const fadeUp = {
  initial: { opacity: 0, y: 20 },
  whileInView: { opacity: 1, y: 0 },
  viewport: { once: true, margin: "-50px" },
  transition: { duration: 0.5, ease: "easeOut" as const },
};

const stagger = {
  initial: { opacity: 0, y: 20 },
  whileInView: { opacity: 1, y: 0 },
  viewport: { once: true },
} as const;

export default function Landing() {
  return (
    <div className="min-h-screen bg-zinc-950 text-zinc-100 overflow-x-hidden">
      {/* Navigation */}
      <nav className="fixed top-0 left-0 right-0 z-50 soft-surface border-b soft-border">
        <div className="max-w-6xl mx-auto px-6 h-14 flex items-center justify-between">
          <div className="flex items-center gap-2.5">
            <OrcaLogo className="w-5 h-5 text-zinc-100" />
            <span className="font-semibold text-sm text-zinc-100">Orka AI</span>
          </div>
          <div className="flex items-center gap-6">
            <a href="#features" className="text-xs text-zinc-500 hover:text-zinc-300 transition-colors duration-150">
              Özellikler
            </a>
            <a href="#how-it-works" className="text-xs text-zinc-500 hover:text-zinc-300 transition-colors duration-150">
              Nasıl Çalışır?
            </a>
            <Link
              href="/login"
              className="text-xs font-medium text-zinc-950 bg-zinc-100 hover:bg-zinc-200 px-4 py-2 rounded-lg transition-colors duration-150"
            >
              Giriş Yap
            </Link>
          </div>
        </div>
      </nav>

      {/* Hero Section */}
      <section className="relative pt-32 pb-20 px-6">
        {/* Background Image */}
        <div className="absolute inset-0 overflow-hidden">
          <img
            src={HERO_BG}
            alt=""
            className="w-full h-full object-cover opacity-30"
          />
          <div className="absolute inset-0 bg-background/80" />
        </div>

        <div className="relative max-w-4xl mx-auto text-center">
          <motion.div
            initial={{ opacity: 0, y: 16 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.6, ease: "easeOut" }}
          >
            {/* Badge */}
            <div className="inline-flex items-center gap-2 px-3 py-1.5 rounded-full border border-zinc-800 bg-zinc-900/50 mb-8">
              <div className="w-1.5 h-1.5 rounded-full bg-zinc-400 animate-pulse" />
              <span className="text-[11px] text-zinc-400">Yapay Zeka Destekli Öğrenme Ekosistemi</span>
            </div>

            <h1 className="text-5xl sm:text-6xl font-bold tracking-tight text-zinc-50 leading-[1.1] mb-6">
              Her konuda uzmanlaşın
              <br />
              <span className="text-zinc-500">akıllı rehberlik ile</span>
            </h1>

            <p className="text-lg text-zinc-500 max-w-2xl mx-auto mb-10 leading-relaxed">
              Orka AI, siz öğrendikçe yaşayan bir bilgi haritası oluşturur. Kişiselleştirilmiş müfredat, 
              etkileşimli quizler ve otonom araştırma agentları ile öğrenme sürecinizi hızlandırın.
            </p>

            <div className="flex items-center justify-center gap-4">
              <Link
                href="/login"
                className="inline-flex items-center gap-2 px-6 py-3 bg-zinc-100 text-zinc-950 rounded-lg font-medium text-sm hover:bg-zinc-200 transition-colors duration-150"
              >
                Öğrenmeye Başla
                <ArrowRight className="w-4 h-4" />
              </Link>
              <a
                href="#video"
                className="inline-flex items-center gap-2 px-6 py-3 border border-zinc-800 text-zinc-300 rounded-lg text-sm hover:bg-zinc-900 hover:border-zinc-700 transition-colors duration-150"
              >
                Nasıl Çalışır?
              </a>
            </div>
          </motion.div>

          {/* Video Section - Moved Here for Initial Visibility */}
          <motion.div
            initial={{ opacity: 0, y: 40 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.8, delay: 0.3, ease: "easeOut" }}
            className="mt-16 relative"
            id="video"
          >
            <div className="rounded-xl border soft-border overflow-hidden soft-surface soft-shadow">
              <video
                className="w-full aspect-video bg-zinc-950"
                controls
                autoPlay
                muted
                loop
                playsInline
                poster="https://d2xsxph8kpxj0f.cloudfront.net/310519663534340404/Tdyfs3EUSoXDGihDvec6L4/video-keyframe-1-LvjTUMtbNfbZK9DZfshLfh.webp"
                preload="auto"
                src="https://d2xsxph8kpxj0f.cloudfront.net/310519663534340404/Tdyfs3EUSoXDGihDvec6L4/orka-promo_30c32572.mp4"
              >
                <source src="https://d2xsxph8kpxj0f.cloudfront.net/310519663534340404/Tdyfs3EUSoXDGihDvec6L4/orka-promo_30c32572.mp4" type="video/mp4" />
                Tarayıcınız video etiketini desteklemiyor.
              </video>
            </div>
            {/* Subtle glow under the video */}
            <div className="absolute -bottom-12 left-1/2 -translate-x-1/2 w-3/4 h-24 bg-zinc-100/10 blur-[100px] rounded-full" />
          </motion.div>
        </div>
      </section>

      {/* Stats Bar */}
      <section className="border-y border-zinc-800/50 py-12 px-6">
        <div className="max-w-4xl mx-auto grid grid-cols-3 gap-8">
          {[
            { value: "10K+", label: "Aktif Öğrenci" },
            { value: "500+", label: "Mevcut Konu" },
            { value: "95%", label: "Başarı Oranı" },
          ].map((stat) => (
            <motion.div key={stat.label} {...fadeUp} className="text-center">
              <p className="text-3xl font-bold text-zinc-100">{stat.value}</p>
              <p className="text-xs text-zinc-500 mt-1">{stat.label}</p>
            </motion.div>
          ))}
        </div>
      </section>

      {/* Features Section */}
      <section id="features" className="py-24 px-6">
        <div className="max-w-5xl mx-auto">
          <motion.div {...fadeUp} className="text-center mb-16">
            <p className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider mb-3">
              Özellikler
            </p>
            <h2 className="text-3xl sm:text-4xl font-bold text-zinc-100 mb-4">
              Etkili öğrenme için ihtiyacınız olan her şey
            </h2>
            <p className="text-sm text-zinc-500 max-w-lg mx-auto">
              Yapay zeka desteği, yapılandırılmış müfredat ve bilgi yönetimini birleştiren tam kapsamlı bir öğrenme komuta merkezi.
            </p>
          </motion.div>

          {/* Feature Grid */}
          <div className="grid grid-cols-3 gap-4 mb-16">
            {[
              {
                icon: Brain,
                title: "AI Destekli Mentorluk",
                description: "Konseptleri derinlemesine açıklayan, kod örnekleri ve tablolarla zenginleştirilmiş akıllı diyaloglar.",
              },
              {
                icon: Layers,
                title: "Yapılandırılmış Müfredat",
                description: "/plan komutu ile saniyeler içinde otonom öğrenme yolları oluşturun. AI sizin için hiyerarşik dersler hazırlar.",
              },
              {
                icon: Target,
                title: "İnteraktif Quizler",
                description: "Akademik tarzda çoktan seçmeli quizler öğrenme sürecinde doğal olarak karşınıza çıkar. Başarınızı anlık takip edin.",
              },
              {
                icon: BookOpen,
                title: "Canlı Bilgi Kütüphanesi",
                description: "Her ders için otomatik oluşturulan wiki sayfaları; anahtar noktaları, kod bloklarını ve özetleri içerir.",
              },
              {
                icon: Zap,
                title: "Kalıcı Bilgi Haritası",
                description: "Öğrenme ilerlemeniz yan menüdeki ağaç yapısında saklanır. Neleri başardığınızı ve sıradaki adımı görün.",
              },
              {
                icon: Shield,
                title: "Kişisel Notlar",
                description: "AI içeriğinin yanına kendi notlarınızı ekleyin. Kişisel ve yapay zeka destekli dev bir bilgi tabanı oluşturun.",
              },
            ].map((feature, i) => (
              <motion.div
                key={feature.title}
                {...stagger}
                transition={{ duration: 0.4, delay: i * 0.08 }}
                className="p-5 rounded-lg border border-zinc-800 bg-zinc-900/30 hover:bg-zinc-900/50 transition-colors duration-200"
              >
                <feature.icon className="w-5 h-5 text-zinc-500 mb-3" />
                <h3 className="text-sm font-medium text-zinc-200 mb-2">{feature.title}</h3>
                <p className="text-xs text-zinc-500 leading-relaxed">{feature.description}</p>
              </motion.div>
            ))}
          </div>

          {/* Feature Showcase 1 */}
          <motion.div {...fadeUp} className="grid grid-cols-2 gap-12 items-center mb-24">
            <div>
              <p className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider mb-3">
                Akıllı Quizler
              </p>
              <h3 className="text-2xl font-bold text-zinc-100 mb-4">
                Öğrenirken bilginizi test edin
              </h3>
              <p className="text-sm text-zinc-500 leading-relaxed mb-6">
                Quizler, öğrenme seanslarınız sırasında doğal bir akışla karşınıza çıkar. AI, zorluk seviyesini ilerlemenize göre ayarlar ve güçlendirilmesi gereken alanları tespit eder.
              </p>
              <ul className="space-y-3">
                {["Akademik çoktan seçmeli format", "Açıklamalı anlık geri bildirimler", "İlerleme takibi ve başarı geçmişi"].map((item) => (
                  <li key={item} className="flex items-center gap-2 text-xs text-zinc-400">
                    <div className="w-1 h-1 rounded-full bg-zinc-600" />
                    {item}
                  </li>
                ))}
              </ul>
            </div>
            <div className="rounded-xl border soft-border overflow-hidden soft-shadow">
              <img src={FEATURE_2} alt="Quiz Arayüzü" className="w-full" />
            </div>
          </motion.div>

          {/* Feature Showcase 2 */}
          <motion.div {...fadeUp} className="grid grid-cols-2 gap-12 items-center">
            <div className="rounded-xl border soft-border overflow-hidden order-1 soft-shadow">
              <img src={FEATURE_3} alt="Wiki Arayüzü" className="w-full" />
            </div>
            <div className="order-2">
              <p className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider mb-3">
                Bilgi Kütüphanesi (Wiki)
              </p>
              <h3 className="text-2xl font-bold text-zinc-100 mb-4">
                Size özel dijital kütüphane
              </h3>
              <p className="text-sm text-zinc-500 leading-relaxed mb-6">
                Öğrendiğiniz her konu; yapılandırılmış içerikler, kod örnekleri, karşılaştırma tabloları ve anahtar çıkarımlarla kendi wiki sayfasına dönüşür. Kendi notlarınızı ekleyerek kişisel referans dünyanızı kurun.
              </p>
              <ul className="space-y-3">
                {["Kod blokları içeren zengin markdown içeriği", "Hızlı tekrar için anahtar nokta özetleri", "Her ders için özel kullanıcı notları"].map((item) => (
                  <li key={item} className="flex items-center gap-2 text-xs text-zinc-400">
                    <div className="w-1 h-1 rounded-full bg-zinc-600" />
                    {item}
                  </li>
                ))}
              </ul>
            </div>
          </motion.div>
        </div>
      </section>

      {/* How It Works */}
      <section id="how-it-works" className="py-24 px-6 border-t border-zinc-800/50">
        <div className="max-w-4xl mx-auto">
          <motion.div {...fadeUp} className="text-center mb-16">
            <p className="text-[11px] font-medium text-zinc-500 uppercase tracking-wider mb-3">
              Nasıl Çalışır?
            </p>
            <h2 className="text-3xl sm:text-4xl font-bold text-zinc-100 mb-4">
              Uzmanlığa giden üç adım
            </h2>
          </motion.div>

          <div className="grid grid-cols-3 gap-6">
            {[
              {
                step: "01",
                title: "Konunu Seç",
                description: "Yapay zekaya dilediğini sor veya /plan kullanarak progressive derslerden oluşan bir yol oluştur.",
              },
              {
                step: "02",
                title: "Öğren ve Uygula",
                description: "AI açıklamalarıyla etkileşime gir, quizleri çöz ve ilerledikçe kendi bilgi kütüphaneni inşa et.",
              },
              {
                step: "03",
                title: "Takip Et ve Uzmanlaş",
                description: "İlerlemeni bilgi haritasından izle, quiz geçmişini incele ve konunun hakimi ol.",
              },
            ].map((item, i) => (
              <motion.div
                key={item.step}
                {...stagger}
                transition={{ duration: 0.4, delay: i * 0.1 }}
                className="relative"
              >
                <span className="text-5xl font-bold text-zinc-800/50 mb-4 block">{item.step}</span>
                <h3 className="text-sm font-medium text-zinc-200 mb-2">{item.title}</h3>
                <p className="text-xs text-zinc-500 leading-relaxed">{item.description}</p>
              </motion.div>
            ))}
          </div>
        </div>
      </section>


      {/* CTA Section */}
      <section className="py-24 px-6 border-t border-zinc-800/50">
        <div className="max-w-3xl mx-auto text-center">
          <motion.div {...fadeUp}>
            <h2 className="text-3xl sm:text-4xl font-bold text-zinc-100 mb-4">
              Öğrenmeye başlamaya hazır mısın?
            </h2>
            <p className="text-sm text-zinc-500 mb-8 max-w-md mx-auto">
              Teknik konularda daha hızlı ve etkili uzmanlaşmak için Orka AI kullanan binlerce öğrenciye katılın.
            </p>
            <Link
              href="/login"
              className="inline-flex items-center gap-2 px-8 py-3.5 bg-zinc-100 text-zinc-950 rounded-lg font-medium text-sm hover:bg-zinc-200 transition-colors duration-150"
            >
              Orka AI'yı Başlat
              <ArrowRight className="w-4 h-4" />
            </Link>
          </motion.div>
        </div>
      </section>

      {/* Footer */}
      <footer className="border-t border-zinc-800/50 py-8 px-6">
        <div className="max-w-6xl mx-auto flex items-center justify-between">
          <div className="flex items-center gap-2">
            <OrcaLogo className="w-4 h-4 text-zinc-600" />
            <span className="text-xs text-zinc-600">Orka AI</span>
          </div>
          <p className="text-[10px] text-zinc-700">
            Zeka ile inşa edildi. Uzmanlık için tasarlandı.
          </p>
        </div>
      </footer>
    </div>
  );
}
