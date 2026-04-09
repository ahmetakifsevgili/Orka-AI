import React, { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { motion } from 'framer-motion';
import { AlertCircle, Zap, BookOpen, BarChart2 } from 'lucide-react';
import { AuthAPI } from '../services/api';

const BRAND_FEATURES = [
  {
    icon: Zap,
    iconBg: 'rgba(240,165,0,0.12)',
    iconColor: 'var(--amber)',
    text: 'Günde 50 mesaj ücretsiz. Kredi kartı gerekmez.',
  },
  {
    icon: BookOpen,
    iconBg: 'rgba(59,130,246,0.12)',
    iconColor: 'var(--blue)',
    text: 'Sınırsız konu ekleyin ve kişisel wiki\'nizi oluşturun.',
  },
  {
    icon: BarChart2,
    iconBg: 'rgba(34,197,94,0.12)',
    iconColor: 'var(--green)',
    text: 'İlerlemenizi takip edin, seviyenize göre içerik alın.',
  },
];

export default function Register() {
  const navigate = useNavigate();
  const [formData, setFormData] = useState({
    firstName: '', lastName: '', email: '', password: '',
  });
  const [error, setError] = useState(null);
  const [loading, setLoading] = useState(false);

  const handleSubmit = async (e) => {
    e.preventDefault();
    setError(null);

    if (!formData.firstName.trim() || !formData.lastName.trim()) {
      setError('İsim ve soyisim zorunludur.');
      return;
    }
    const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    if (!emailRegex.test(formData.email)) {
      setError('Geçerli bir e-posta adresi girin.');
      return;
    }
    if (formData.password.length < 8) {
      setError('Şifre en az 8 karakter olmalıdır.');
      return;
    }

    setLoading(true);
    try {
      const res = await AuthAPI.register(formData);
      const data = res.data;
      localStorage.setItem('orka_token', data.token);
      localStorage.setItem('orka_refresh', data.refreshToken);
      localStorage.setItem('orka_user', JSON.stringify(data.user));
      navigate('/app');
    } catch (err) {
      setError(err.response?.data?.message || 'Hesap oluşturulamadı. Lütfen tekrar deneyin.');
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
          Derin öğrenme<br />yolculuğuna<br /><em>başla.</em>
        </h2>
        <p className="auth-brand-desc">
          Ücretsiz hesap oluştur, AI destekli öğrenme asistanına anında erişim kazan.
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
          <div className="auth-form-title">Hesap Oluştur</div>
          <div className="auth-form-subtitle">Birkaç saniyede ücretsiz başlayın.</div>

          {error && (
            <div className="auth-error" style={{ marginBottom: 14 }}>
              <AlertCircle size={14} strokeWidth={2.5} />
              {error}
            </div>
          )}

          <form className="auth-form" onSubmit={handleSubmit}>
            <div style={{ display: 'flex', gap: 12 }}>
              <div className="form-group" style={{ flex: 1 }}>
                <label className="form-label">İsim</label>
                <input
                  type="text"
                  className="form-input"
                  placeholder="Ali"
                  value={formData.firstName}
                  onChange={set('firstName')}
                  required
                  autoComplete="given-name"
                />
              </div>
              <div className="form-group" style={{ flex: 1 }}>
                <label className="form-label">Soyisim</label>
                <input
                  type="text"
                  className="form-input"
                  placeholder="Yılmaz"
                  value={formData.lastName}
                  onChange={set('lastName')}
                  required
                  autoComplete="family-name"
                />
              </div>
            </div>

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
                placeholder="En az 8 karakter"
                value={formData.password}
                onChange={set('password')}
                required
                minLength={8}
                autoComplete="new-password"
              />
            </div>

            <button
              type="submit"
              className="btn btn-primary"
              disabled={loading}
              style={{ width: '100%', marginTop: 4, justifyContent: 'center', padding: '12px' }}
            >
              {loading ? 'Hesap oluşturuluyor...' : 'Ücretsiz Başla'}
            </button>

            <p style={{ fontSize: 11, color: 'var(--muted)', textAlign: 'center', lineHeight: 1.5 }}>
              Devam ederek{' '}
              <a href="#" style={{ color: 'var(--text2)' }}>Kullanım Şartlarını</a>
              {' '}ve{' '}
              <a href="#" style={{ color: 'var(--text2)' }}>Gizlilik Politikasını</a>
              {' '}kabul etmiş olursunuz.
            </p>
          </form>

          <div className="auth-footer">
            Zaten hesabın var mı?{' '}
            <Link to="/login" style={{ color: 'var(--amber)', fontWeight: 600 }}>
              Giriş Yap
            </Link>
          </div>
        </motion.div>
      </div>
    </div>
  );
}
