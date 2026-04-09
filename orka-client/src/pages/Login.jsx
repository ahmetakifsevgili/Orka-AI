import React, { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { motion } from 'framer-motion';
import { AlertCircle, Brain, Map, Target } from 'lucide-react';
import { AuthAPI } from '../services/api';

const BRAND_FEATURES = [
  {
    icon: Brain,
    iconBg: 'rgba(168,85,247,0.12)',
    iconColor: 'var(--violet)',
    text: 'Gemini, Groq ve Mistral\'ı tek arayüzden kullanın',
  },
  {
    icon: Map,
    iconBg: 'rgba(240,165,0,0.12)',
    iconColor: 'var(--amber)',
    text: 'Her konuşma otomatik olarak bilgi haritanıza eklenir',
  },
  {
    icon: Target,
    iconBg: 'rgba(34,197,94,0.12)',
    iconColor: 'var(--green)',
    text: '5 aşamalı öğrenme yolu ile kalıcı öğrenme',
  },
];

export default function Login() {
  const navigate = useNavigate();
  const [formData, setFormData] = useState({ email: '', password: '' });
  const [error, setError] = useState(null);
  const [loading, setLoading] = useState(false);

  const handleSubmit = async (e) => {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      const res = await AuthAPI.login(formData);
      const data = res.data;
      localStorage.setItem('orka_token', data.token);
      localStorage.setItem('orka_refresh', data.refreshToken);
      localStorage.setItem('orka_user', JSON.stringify(data.user));
      navigate('/app');
    } catch (err) {
      setError(err.response?.data?.message || 'E-posta veya şifre hatalı.');
    } finally {
      setLoading(false);
    }
  };

  const set = (key) => (e) => setFormData(prev => ({ ...prev, [key]: e.target.value }));

  return (
    <div className="auth-page">
      {/* Brand panel */}
      <div className="auth-brand">
        <Link to="/" className="auth-brand-logo">orka</Link>
        <h2 className="auth-brand-title">
          Tekrar<br /><em>hoş geldin.</em>
        </h2>
        <p className="auth-brand-desc">
          Kaldığın yerden devam et. Bilgi haritaların ve öğrenme
          geçmişin seni bekliyor.
        </p>
        <ul className="auth-brand-features">
          {BRAND_FEATURES.map((f, i) => (
            <li key={i} className="auth-brand-feature">
              <div className="auth-brand-feature-icon" style={{ background: f.iconBg }}>
                <f.icon size={14} color={f.iconColor} strokeWidth={2} />
              </div>
              {f.text}
            </li>
          ))}
        </ul>
      </div>

      {/* Form side */}
      <div className="auth-form-side">
        <motion.div
          className="auth-form-card"
          initial={{ opacity: 0, y: 24 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.5, ease: 'easeOut' }}
        >
          <div className="auth-form-title">Giriş Yap</div>
          <div className="auth-form-subtitle">Hesabına erişmek için bilgilerini gir.</div>

          {error && (
            <div className="auth-error" style={{ marginBottom: 14 }}>
              <AlertCircle size={14} strokeWidth={2.5} />
              {error}
            </div>
          )}

          <form className="auth-form" onSubmit={handleSubmit}>
            <div className="form-group">
              <label className="form-label">E-posta</label>
              <input
                type="email"
                className="form-input"
                placeholder="ornek@email.com"
                value={formData.email}
                onChange={set('email')}
                required
                autoComplete="email"
              />
            </div>
            <div className="form-group">
              <label className="form-label">Şifre</label>
              <input
                type="password"
                className="form-input"
                placeholder="••••••••"
                value={formData.password}
                onChange={set('password')}
                required
                autoComplete="current-password"
              />
            </div>
            <button
              type="submit"
              className="btn btn-primary"
              disabled={loading}
              style={{ width: '100%', marginTop: 4, justifyContent: 'center', padding: '12px' }}
            >
              {loading ? 'Giriş yapılıyor...' : 'Giriş Yap'}
            </button>
          </form>

          <div className="auth-footer">
            Hesabın yok mu?{' '}
            <Link to="/register" style={{ color: 'var(--amber)', fontWeight: 600 }}>
              Ücretsiz Başla
            </Link>
          </div>
        </motion.div>
      </div>
    </div>
  );
}
