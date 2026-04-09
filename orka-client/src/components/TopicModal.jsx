import React, { useState, useEffect } from 'react';
import { X, Sparkles } from 'lucide-react';

const CATEGORIES = [
  { value: 'Bilgisayar Bilimi', label: '💻  Bilgisayar Bilimi' },
  { value: 'Matematik',        label: '📐  Matematik' },
  { value: 'Yabancı Dil',     label: '🌍  Yabancı Dil' },
  { value: 'Tarih',           label: '🏛️  Tarih' },
  { value: 'Bilim',           label: '🔬  Bilim' },
  { value: 'Sanat',           label: '🎨  Sanat' },
  { value: 'İş & Finans',    label: '📊  İş & Finans' },
  { value: 'Psikoloji',       label: '🧠  Psikoloji' },
  { value: 'Felsefe',         label: '📜  Felsefe' },
  { value: 'Genel',           label: '📚  Genel' },
];

const PRESET_EMOJIS = [
  '📚','💻','🔬','📐','🌍','🎨',
  '🎸','🏛️','⚛️','🧬','📊','🚀',
  '🤖','🎮','🌱','🏋️','✈️','🧩',
  '🦁','🎯','🔧','🎬','🧪','💡',
];

export default function TopicModal({ isOpen, onClose, onSubmit, isLoading }) {
  const [formData, setFormData] = useState({ title: '', category: 'Genel', emoji: '📚' });

  useEffect(() => {
    if (isOpen) setFormData({ title: '', category: 'Genel', emoji: '📚' });
  }, [isOpen]);

  if (!isOpen) return null;

  const handleSubmit = (e) => {
    e.preventDefault();
    if (!formData.title.trim()) return;
    onSubmit(formData);
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-card" onClick={e => e.stopPropagation()}>
        <div className="modal-top">
          <div className="modal-top-row">
            <div>
              <div className="modal-title">Yeni Konu Ekle</div>
              <div className="modal-subtitle">
                Ne öğrenmek istiyorsunuz? AI sizin için kişiselleştirilmiş bir bilgi haritası oluşturacak.
              </div>
            </div>
            <button className="modal-close-btn" onClick={onClose}>
              <X size={17} strokeWidth={2} />
            </button>
          </div>
        </div>

        <form onSubmit={handleSubmit}>
          <div className="modal-body">
            <div className="form-group">
              <label className="form-label">Konu Adı</label>
              <input
                type="text"
                className="form-input"
                placeholder="Örn: Python ile Makine Öğrenmesi, Osmanlı Tarihi"
                value={formData.title}
                onChange={e => setFormData({ ...formData, title: e.target.value })}
                required
                autoFocus
              />
            </div>

            <div className="form-group">
              <label className="form-label">Alan</label>
              <select
                className="form-select"
                value={formData.category}
                onChange={e => setFormData({ ...formData, category: e.target.value })}
              >
                {CATEGORIES.map(cat => (
                  <option key={cat.value} value={cat.value}>{cat.label}</option>
                ))}
              </select>
            </div>

            <div className="form-group">
              <label className="form-label">Emoji Seç</label>
              <div className="emoji-grid">
                {PRESET_EMOJIS.map(emoji => (
                  <div
                    key={emoji}
                    className={`emoji-option ${formData.emoji === emoji ? 'selected' : ''}`}
                    onClick={() => setFormData({ ...formData, emoji })}
                    role="button"
                    tabIndex={0}
                    onKeyDown={e => e.key === 'Enter' && setFormData({ ...formData, emoji })}
                  >
                    {emoji}
                  </div>
                ))}
              </div>
            </div>
          </div>

          <div className="modal-footer">
            <button
              type="button"
              className="btn btn-secondary"
              onClick={onClose}
              disabled={isLoading}
            >
              İptal
            </button>
            <button
              type="submit"
              className="btn btn-primary"
              disabled={isLoading || !formData.title.trim()}
            >
              <Sparkles size={13} strokeWidth={2.5} />
              {isLoading ? 'Oluşturuluyor...' : 'Oluştur'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
