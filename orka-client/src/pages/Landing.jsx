import React from 'react';
import { Link } from 'react-router-dom';
import { motion } from 'framer-motion';
import {
  Brain, BookOpen, Target, BarChart2,
  Zap, ArrowRight, Check, Layers,
  MessageSquare, Map,
} from 'lucide-react';

const fadeUp = {
  initial: { opacity: 0, y: 20 },
  whileInView: { opacity: 1, y: 0 },
  viewport: { once: true },
  transition: { duration: 0.5, ease: 'easeOut' },
};

const FEATURES = [
  {
    icon: Brain,
    iconBg: 'rgba(168,85,247,0.12)',
    iconColor: 'var(--violet)',
    title: 'Çok Ajanlı AI Yönlendirme',
    desc: 'Her isteğiniz anın içeriğine göre en uygun modele (Gemini, Groq, Mistral) iletilir. Tek modelin kısıtlarına takılmazsınız.',
  },
  {
    icon: Map,
    iconBg: 'rgba(240,165,0,0.12)',
    iconColor: 'var(--amber)',
    title: 'Otomatik Bilgi Haritası',
    desc: 'Her sohbetten kavram açıklamaları, quiz soruları ve kaynak notlar otomatik olarak kişisel wiki\'nize eklenir.',
  },
  {
    icon: Target,
    iconBg: 'rgba(34,197,94,0.12)',
    iconColor: 'var(--green)',
    title: 'Aşamalı Öğrenme',
    desc: 'Keşif\'ten Tamamlanma\'ya kadar 5 aşamalı bir yol. Kendi seviyenizden başlarsınız, AI sizi bir sonraki adıma taşır.',
  },
  {
    icon: MessageSquare,
    iconBg: 'rgba(59,130,246,0.12)',
    iconColor: 'var(--blue)',
    title: 'Doğal Dil Komutları',
    desc: '"/quiz", "/interview", "/plan" gibi anlık komutlarla anı yönlendirin. AI yönlendirmesine gerek yok.',
  },
  {
    icon: BarChart2,
    iconBg: 'rgba(240,165,0,0.12)',
    iconColor: 'var(--amber)',
    title: 'İlerleme Takibi',
    desc: 'Hangi konunun hangi bölümünde olduğunuzu, kaç quiz sorduğunuzu ve ne kadar kaldığını anlık görürsünüz.',
  },
  {
    icon: Layers,
    iconBg: 'rgba(168,85,247,0.12)',
    iconColor: 'var(--violet)',
    title: 'Bağlam Farkındalığı',
    desc: 'AI önceki konuşmaları, öğrenme seviyenizi ve hangi aşamada olduğunuzu bilerek yanıt verir.',
  },
];

const PHASES = [
  {
    num: '1',
    name: 'Keşif',
    desc: 'Konunuzu sisteme tanıtırsınız. AI hedeflerinizi anlar ve tam program mı, serbest sohbet mi sorar.',
    numBg: 'var(--amber-dim)', numColor: 'var(--amber)',
  },
  {
    num: '2',
    name: 'Değerlendirme',
    desc: 'Groq modeli kısa bir değerlendirmeyle seviyenizi (Başlangıç/Orta/İleri) belirler.',
    numBg: 'var(--blue-dim)', numColor: 'var(--blue)',
  },
  {
    num: '3',
    name: 'Planlama',
    desc: 'Seviyenize özel bir müfredat oluşturulur ve bilgi haritanızın iskeleti hazırlanır.',
    numBg: 'var(--violet-dim)', numColor: 'var(--violet)',
  },
  {
    num: '4',
    name: 'Aktif Öğrenme',
    desc: 'Soru sorar, quiz çözer, mülakat pratiği yaparsınız. Her yanıt wiki\'nize eklenir.',
    numBg: 'var(--green-dim)', numColor: 'var(--green)',
  },
  {
    num: '5',
    name: 'Tamamlandı',
    desc: 'Konu tamamlandı olarak işaretlenir. Bilgi haritanız gelecekte referans olarak durmaya devam eder.',
    numBg: 'rgba(100,116,139,0.12)', numColor: '#94a3b8',
  },
];

export default function Landing() {
  return (
    <div className="landing">
      {/* Nav */}
      <nav className="landing-nav">
        <Link to="/" className="landing-logo">orka</Link>
        <div className="landing-nav-links">
          <a href="#features" className="landing-nav-link">Özellikler</a>
          <a href="#how" className="landing-nav-link">Nasıl Çalışır</a>
          <a href="#pricing" className="landing-nav-link">Fiyatlar</a>
        </div>
        <div className="landing-nav-cta">
          <Link
            to="/login"
            style={{
              fontSize: '13.5px',
              fontWeight: 500,
              color: 'var(--text2)',
              textDecoration: 'none',
              transition: 'color 0.15s',
            }}
          >
            Giriş Yap
          </Link>
          <Link to="/register" className="hero-btn-primary" style={{ padding: '8px 18px', fontSize: '13.5px' }}>
            Ücretsiz Başla
          </Link>
        </div>
      </nav>

      {/* Hero */}
      <motion.div
        className="hero-section"
        initial={{ opacity: 0, y: 28 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.65, ease: 'easeOut' }}
      >
        <div className="hero-eyebrow">
          <Zap size={11} strokeWidth={2.5} />
          Çok Ajanlı Öğrenme Orkestratörü
        </div>
        <h1 className="hero-title">
          Öğrenmenin<br />
          <em>En Derin</em> Hali
        </h1>
        <p className="hero-subtitle">
          Birden fazla AI modelinin gücünü tek platformda birleştirin.
          Konunuzu anlayın, planlayın ve kişisel bilgi haritanızı oluşturun.
        </p>
        <div className="hero-cta">
          <Link to="/register" className="hero-btn-primary">
            Hemen Başla
            <ArrowRight size={16} strokeWidth={2.5} />
          </Link>
          <a href="#how" className="hero-btn-secondary">
            Nasıl Çalışır?
          </a>
        </div>
      </motion.div>

      {/* Stats */}
      <motion.div className="stats-bar" {...fadeUp}>
        <div className="stat-item">
          <span className="stat-number">4+</span>
          <div className="stat-label">AI Modeli</div>
        </div>
        <div className="stat-item">
          <span className="stat-number">9</span>
          <div className="stat-label">Yanıt Türü</div>
        </div>
        <div className="stat-item">
          <span className="stat-number">5</span>
          <div className="stat-label">Öğrenme Aşaması</div>
        </div>
        <div className="stat-item">
          <span className="stat-number">∞</span>
          <div className="stat-label">Konu Desteği</div>
        </div>
      </motion.div>

      {/* Features */}
      <motion.section id="features" className="landing-section" {...fadeUp}>
        <div className="section-label">
          <Zap size={11} strokeWidth={2.5} />
          Özellikler
        </div>
        <h2 className="section-title">Her şey tek platformda</h2>
        <p className="section-subtitle">
          Standart chatbot'lardan farklı olarak Orka, öğrenme sürecinizi
          bütüncül olarak yönetir ve kalıcı bilgi birikimi sağlar.
        </p>
        <div className="features-grid">
          {FEATURES.map((f, i) => (
            <motion.div
              key={i}
              className="feature-card"
              initial={{ opacity: 0, y: 16 }}
              whileInView={{ opacity: 1, y: 0 }}
              viewport={{ once: true }}
              transition={{ duration: 0.4, delay: i * 0.07 }}
            >
              <div className="feature-icon" style={{ background: f.iconBg }}>
                <f.icon size={20} color={f.iconColor} strokeWidth={2} />
              </div>
              <div className="feature-title">{f.name || f.title}</div>
              <div className="feature-desc">{f.desc}</div>
            </motion.div>
          ))}
        </div>
      </motion.section>

      {/* How it works */}
      <motion.section
        id="how"
        className="landing-section"
        style={{ background: 'var(--surface)', borderTop: '1px solid var(--border)', borderBottom: '1px solid var(--border)', maxWidth: '100%', padding: '72px 0' }}
        {...fadeUp}
      >
        <div style={{ maxWidth: 1060, margin: '0 auto', padding: '0 32px' }}>
          <div className="section-label">
            <Target size={11} strokeWidth={2.5} />
            Nasıl Çalışır
          </div>
          <h2 className="section-title">5 Aşamalı Öğrenme Yolu</h2>
          <p className="section-subtitle">
            Her konu için kişiselleştirilmiş bir yolculuk. Sisteme yeni bir konu girdiğiniz
            an akıllı yönlendirme başlar.
          </p>
          <div className="phases-list">
            {PHASES.map((p, i) => (
              <motion.div
                key={i}
                className="phase-item"
                initial={{ opacity: 0, x: -12 }}
                whileInView={{ opacity: 1, x: 0 }}
                viewport={{ once: true }}
                transition={{ duration: 0.4, delay: i * 0.08 }}
              >
                <div
                  className="phase-num"
                  style={{ background: p.numBg, color: p.numColor }}
                >
                  {p.num}
                </div>
                <div className="phase-content">
                  <div className="phase-name">{p.name}</div>
                  <div className="phase-desc">{p.desc}</div>
                </div>
              </motion.div>
            ))}
          </div>
        </div>
      </motion.section>

      {/* Pricing */}
      <motion.section id="pricing" className="landing-section" {...fadeUp}>
        <div className="section-label">
          <Zap size={11} strokeWidth={2.5} />
          Fiyatlandırma
        </div>
        <h2 className="section-title">Sade ve şeffaf</h2>
        <p className="section-subtitle">
          Orka'ya ücretsiz başlayın, ihtiyacınız oldukça büyüyün.
        </p>
        <div className="pricing-grid">
          {/* Free plan */}
          <motion.div
            className="plan-card"
            initial={{ opacity: 0, y: 16 }}
            whileInView={{ opacity: 1, y: 0 }}
            viewport={{ once: true }}
            transition={{ duration: 0.4 }}
          >
            <div className="plan-name">Ücretsiz</div>
            <div className="plan-price">₺0 <span>/ ay</span></div>
            <div className="plan-desc">Başlamak için mükemmel</div>
            <ul className="plan-features">
              {['50 mesaj / gün', '3 GB depolama', 'Tüm AI modelleri', 'Sınırsız konu', 'Bilgi haritası'].map(f => (
                <li key={f} className="plan-feature">
                  <Check size={13} className="plan-feature-check" strokeWidth={2.5} />
                  {f}
                </li>
              ))}
            </ul>
            <Link to="/register" className="btn btn-secondary" style={{ width: '100%', justifyContent: 'center' }}>
              Ücretsiz Başla
            </Link>
          </motion.div>

          {/* Pro plan */}
          <motion.div
            className="plan-card featured"
            initial={{ opacity: 0, y: 16 }}
            whileInView={{ opacity: 1, y: 0 }}
            viewport={{ once: true }}
            transition={{ duration: 0.4, delay: 0.1 }}
          >
            <div className="plan-name" style={{ color: 'var(--amber)' }}>Pro ✦</div>
            <div className="plan-price" style={{ color: 'var(--amber)' }}>₺199 <span>/ ay</span></div>
            <div className="plan-desc">Yoğun öğrenenler için</div>
            <ul className="plan-features">
              {['500 mesaj / gün', '15 GB depolama', 'Öncelikli modeller', 'Sınırsız konu', 'Gelişmiş wiki', 'Export (PDF)'].map(f => (
                <li key={f} className="plan-feature">
                  <Check size={13} style={{ color: 'var(--amber)' }} strokeWidth={2.5} />
                  {f}
                </li>
              ))}
            </ul>
            <Link to="/register" className="btn btn-primary" style={{ width: '100%', justifyContent: 'center' }}>
              Pro'ya Geç
              <ArrowRight size={14} strokeWidth={2.5} />
            </Link>
          </motion.div>
        </div>
      </motion.section>

      {/* CTA */}
      <div className="landing-cta-section">
        <motion.div {...fadeUp}>
          <h2 className="section-title" style={{ textAlign: 'center', marginBottom: 10 }}>
            Öğrenmeye bugün başlayın
          </h2>
          <p style={{ fontSize: 16, color: 'var(--text2)', marginBottom: 28, textAlign: 'center' }}>
            Kayıt olmak ücretsiz, kredi kartı gerekmez.
          </p>
          <div style={{ display: 'flex', justifyContent: 'center' }}>
            <Link to="/register" className="hero-btn-primary">
              Ücretsiz Hesap Oluştur
              <ArrowRight size={16} strokeWidth={2.5} />
            </Link>
          </div>
        </motion.div>
      </div>

      {/* Footer */}
      <footer className="landing-footer">
        <div className="footer-logo">orka</div>
        <div className="footer-text">© 2025 Orka. Tüm hakları saklıdır.</div>
        <div style={{ display: 'flex', gap: 24 }}>
          <a href="#" style={{ fontSize: 12.5, color: 'var(--muted)', textDecoration: 'none' }}>Gizlilik</a>
          <a href="#" style={{ fontSize: 12.5, color: 'var(--muted)', textDecoration: 'none' }}>Şartlar</a>
        </div>
      </footer>
    </div>
  );
}
