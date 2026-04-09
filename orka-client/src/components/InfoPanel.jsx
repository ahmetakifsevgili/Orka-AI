import React from 'react';
import { HelpCircle, Users, BookOpen, BarChart2 } from 'lucide-react';

const AI_MODELS = [
  { name: 'Gemini Flash', role: 'Açıklama · Plan · Özet', color: 'var(--amber)' },
  { name: 'Groq LLaMA', role: 'Yönlendirme · Değerlendirme', color: 'var(--blue)' },
  { name: 'OpenRouter', role: 'Mülakat · Quiz · Araştırma', color: 'var(--violet)' },
  { name: 'Mistral', role: 'Wiki küratörü', color: 'var(--green)' },
];

const QUICK_ACTIONS = [
  { label: '/quiz  — Test et', command: '/quiz', icon: HelpCircle, color: 'var(--blue)' },
  { label: '/interview  — Mülakat', command: '/interview', icon: Users, color: 'var(--violet)' },
  { label: '/summary  — Özet al', command: '/summary', icon: BookOpen, color: 'var(--amber)' },
  { label: '/plan  — Plan oluştur', command: '/plan', icon: BarChart2, color: 'var(--green)' },
];

export default function InfoPanel({ topic, onSendCommand }) {
  const hasSections = topic?.totalSections > 0;
  const progress = hasSections
    ? Math.round((topic.completedSections / topic.totalSections) * 100)
    : 0;

  return (
    <div className="info-panel">
      <div className="info-panel-head">Bu Konuda</div>
      <div className="info-panel-scroll">

        {hasSections && (
          <>
            <div className="info-section-label">İlerleme</div>
            <div className="progress-card">
              <div className="progress-fraction">
                {topic.completedSections}
                <span>/{topic.totalSections}</span>
              </div>
              <div className="progress-label">bölüm tamamlandı</div>
              <div className="progress-bar-full">
                <div className="progress-bar-fill" style={{ width: `${progress}%` }} />
              </div>
            </div>
          </>
        )}

        <div className="info-section-label">Hızlı Komutlar</div>
        {QUICK_ACTIONS.map(action => (
          <button
            key={action.command}
            className="quick-btn"
            onClick={() => onSendCommand(action.command)}
            disabled={!topic}
          >
            <action.icon size={11} style={{ color: action.color, flexShrink: 0 }} strokeWidth={2.5} />
            {action.label}
          </button>
        ))}

        <div className="info-section-label" style={{ marginTop: '14px' }}>Aktif AI Modeller</div>
        {AI_MODELS.map(model => (
          <div key={model.name} className="ai-model-card">
            <div className="ai-model-dot" style={{ background: model.color }} />
            <div className="ai-model-info">
              <div className="ai-model-name">{model.name}</div>
              <div className="ai-model-role">{model.role}</div>
            </div>
          </div>
        ))}

      </div>
    </div>
  );
}
