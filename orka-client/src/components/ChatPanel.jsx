import React, { useRef, useEffect, useState } from 'react';
import { ArrowUp, Paperclip, Mic, HelpCircle, Users, BookOpen, BarChart2 } from 'lucide-react';
import ReactMarkdown from 'react-markdown';

const PHASE_LABELS = {
  0: 'Keşif', Discovery: 'Keşif',
  1: 'Değerlendirme', Assessment: 'Değerlendirme',
  2: 'Planlama', Planning: 'Planlama',
  3: 'Aktif Öğrenme', ActiveStudy: 'Aktif Öğrenme',
  4: 'Tamamlandı', Completed: 'Tamamlandı',
};

const PHASE_COLORS = {
  0: 'var(--amber)', Discovery: 'var(--amber)',
  1: 'var(--blue)', Assessment: 'var(--blue)',
  2: 'var(--violet)', Planning: 'var(--violet)',
  3: 'var(--green)', ActiveStudy: 'var(--green)',
  4: 'var(--muted)', Completed: 'var(--muted)',
};

const QUICK_ACTIONS = [
  { label: '/quiz — Test Et', command: '/quiz', icon: HelpCircle },
  { label: '/interview — Mülakat Pratiği', command: '/interview', icon: Users },
  { label: '/summary — Özet Al', command: '/summary', icon: BookOpen },
  { label: '/plan — Plan Oluştur', command: '/plan', icon: BarChart2 },
];

// ── Quiz block parser ─────────────────────────────────────────────────────────
function parseQuizBlock(content) {
  const marker = '```quiz';
  const idx = content.indexOf(marker);
  if (idx === -1) return null;
  const after = content.slice(idx + marker.length);
  const end = after.indexOf('```');
  if (end === -1) return null;
  try {
    return { quiz: JSON.parse(after.slice(0, end).trim()), beforeIdx: idx };
  } catch {
    return null;
  }
}

// ── Interactive Quiz Card ─────────────────────────────────────────────────────
function QuizCard({ quiz, onAnswer, disabled }) {
  const [answered, setAnswered] = useState(false);
  const [selected, setSelected] = useState(null);

  const handleSelect = (option, idx) => {
    if (answered || disabled) return;
    setAnswered(true);
    setSelected(idx);
    onAnswer(option);
  };

  return (
    <div style={{
      background: 'var(--surface2)',
      border: '1px solid var(--border)',
      borderRadius: '10px',
      padding: '14px 16px',
      marginTop: '10px',
    }}>
      <div style={{
        fontSize: '13.5px',
        fontWeight: '500',
        color: 'var(--text)',
        marginBottom: '12px',
        lineHeight: 1.5,
      }}>
        {quiz.question}
      </div>
      <div style={{ display: 'flex', flexDirection: 'column', gap: '7px' }}>
        {quiz.options.map((opt, idx) => {
          const isSelected = answered && selected === idx;
          const isCorrect  = answered && idx === quiz.correctIndex;
          const isWrong    = answered && selected === idx && idx !== quiz.correctIndex;

          let borderColor = 'var(--border)';
          let bg = 'var(--surface)';
          let color = 'var(--text2)';

          if (isCorrect && answered) {
            borderColor = 'var(--green)';
            bg = 'rgba(34,197,94,0.1)';
            color = 'var(--green)';
          } else if (isWrong) {
            borderColor = 'var(--red)';
            bg = 'rgba(239,68,68,0.08)';
            color = 'var(--red)';
          } else if (!answered) {
            // hover state via style tag is limited; we use opacity change on active
          }

          return (
            <button
              key={idx}
              onClick={() => handleSelect(opt, idx)}
              disabled={answered || disabled}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '9px',
                padding: '8px 12px',
                borderRadius: '7px',
                border: `1px solid ${borderColor}`,
                background: bg,
                color: color,
                fontSize: '13px',
                textAlign: 'left',
                cursor: answered || disabled ? 'default' : 'pointer',
                transition: 'all 0.15s',
                opacity: answered && !isSelected && !isCorrect ? 0.5 : 1,
              }}
            >
              <span style={{
                width: '22px',
                height: '22px',
                borderRadius: '50%',
                background: isCorrect ? 'var(--green)' : isWrong ? 'var(--red)' : 'var(--surface3)',
                color: isCorrect || isWrong ? '#fff' : 'var(--text2)',
                fontSize: '11px',
                fontWeight: '600',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                flexShrink: 0,
              }}>
                {isCorrect ? '✓' : isWrong ? '✗' : String.fromCharCode(65 + idx)}
              </span>
              {opt.replace(/^[A-D]\)\s*/, '')}
            </button>
          );
        })}
      </div>
    </div>
  );
}

// ── AI bubble content renderer ────────────────────────────────────────────────
function AiBubbleContent({ content, onSend, sending }) {
  const parsed = parseQuizBlock(content);

  if (!parsed) {
    return <ReactMarkdown>{content}</ReactMarkdown>;
  }

  const { quiz, beforeIdx } = parsed;
  const textBefore = content.slice(0, beforeIdx).trim();

  return (
    <>
      {textBefore && <ReactMarkdown>{textBefore}</ReactMarkdown>}
      <QuizCard quiz={quiz} onAnswer={onSend} disabled={sending} />
    </>
  );
}

// ── Main ChatPanel ────────────────────────────────────────────────────────────
export default function ChatPanel({ topic, messages, onSend, sending, user }) {
  const [inputValue, setInputValue] = useState('');
  const messagesEndRef = useRef(null);
  const textareaRef = useRef(null);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages, sending]);

  useEffect(() => {
    const ta = textareaRef.current;
    if (!ta) return;
    ta.style.height = 'auto';
    ta.style.height = Math.min(ta.scrollHeight, 120) + 'px';
  }, [inputValue]);

  const handleSubmit = (e) => {
    e?.preventDefault();
    if (!inputValue.trim() || sending) return;
    onSend(inputValue.trim());
    setInputValue('');
    if (textareaRef.current) textareaRef.current.style.height = 'auto';
  };

  const handleKeyDown = (e) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSubmit();
    }
  };

  const getInitials = () => {
    const f = user?.firstName?.charAt(0) || '';
    const l = user?.lastName?.charAt(0) || '';
    return (f + l).toUpperCase() || 'U';
  };

  const phaseKey = topic?.currentPhase ?? 0;
  const phaseLabel = PHASE_LABELS[phaseKey] || 'Keşif';
  const phaseColor = PHASE_COLORS[phaseKey] || 'var(--amber)';

  return (
    <div className="chat-panel">
      {topic && (
        <div className="chat-head">
          <span className="chat-head-emoji">{topic.emoji || '📚'}</span>
          <div className="chat-head-title">{topic.title}</div>
          <div className="phase-indicator">
            <div className="phase-dot" style={{ background: phaseColor }} />
            <span>{phaseLabel}</span>
          </div>
          {topic.category && (
            <span className="cat-tag">{topic.category}</span>
          )}
        </div>
      )}

      <div className="messages-list">
        {messages.length === 0 && (
          <div className="welcome-card">
            <div className="welcome-card-emoji">{topic?.emoji || '🧠'}</div>
            <div className="welcome-card-title">{topic?.title || 'Yeni Konu'}</div>
            <div className="welcome-card-text">
              {topic?.lastStudySnapshot
                ? topic.lastStudySnapshot
                : 'Öğrenme asistanınız hazır. Ne öğrenmek istediğinizi yazarak doğrudan başlayın!'}
            </div>
            <div className="quick-actions">
              {QUICK_ACTIONS.map(action => (
                <button
                  key={action.command}
                  className="quick-action-chip"
                  onClick={() => onSend(action.command)}
                  disabled={sending}
                >
                  <action.icon size={11} strokeWidth={2.5} />
                  {action.label}
                </button>
              ))}
            </div>
          </div>
        )}

        {messages.map((msg, idx) => {
          const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';
          const safeKey = msg.id && msg.id !== EMPTY_GUID ? msg.id : `msg-${idx}-${msg.role}`;
          return (
            <div key={safeKey} className={`msg-row ${msg.role === 'user' ? 'user-msg' : ''}`}>
              <div className={`msg-avatar ${msg.role === 'user' ? 'user-av' : 'ai-av'}`}>
                {msg.role === 'user' ? getInitials() : '◎'}
              </div>
              <div className="msg-content">
                <div className="msg-meta">
                  {msg.role !== 'user' && (
                    <span className="model-chip">{msg.modelUsed || 'Orka AI'}</span>
                  )}
                  <span>{msg.role === 'user' ? 'Sen' : (msg.messageType || 'Yanıt')}</span>
                </div>
                <div className={`bubble ${msg.role === 'user' ? 'user-bubble' : 'ai-bubble'}`}>
                  {msg.role === 'user'
                    ? msg.content
                    : <AiBubbleContent content={msg.content} onSend={onSend} sending={sending} />
                  }
                </div>
              </div>
            </div>
          );
        })}

        {sending && (
          <div className="thinking-row">
            <div className="msg-avatar ai-av">◎</div>
            <div className="thinking-bubble">
              <div className="thinking-dot" />
              <div className="thinking-dot" />
              <div className="thinking-dot" />
            </div>
          </div>
        )}

        <div ref={messagesEndRef} />
      </div>

      <div className="input-zone">
        <div className="input-wrap">
          <textarea
            ref={textareaRef}
            value={inputValue}
            onChange={e => setInputValue(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder={
              topic
                ? 'Soru sor, konu değiştir, /quiz veya /plan yaz...'
                : 'Öğrenmek istediğiniz yeni bir konu yazarak başlayın...'
            }
            rows={1}
            disabled={sending}
          />
          <div className="input-tools">
            <button type="button" className="input-tool-btn" tabIndex={-1}>
              <Paperclip size={12} strokeWidth={2.5} />
            </button>
            <button type="button" className="input-tool-btn" tabIndex={-1}>
              <Mic size={12} strokeWidth={2.5} />
            </button>
            <button
              className="send-btn"
              onClick={handleSubmit}
              disabled={!inputValue.trim() || sending}
            >
              <ArrowUp size={15} strokeWidth={2.5} />
            </button>
          </div>
        </div>
        <div className="input-footer">
          <div className="input-status">
            <div className="status-group">
              <div className="status-dot" />
              <span>Gemini Flash</span>
            </div>
            <span>·</span>
            <span>
              {user?.dailyMessageCount ?? 0} / {user?.dailyLimit ?? 50} mesaj
            </span>
          </div>
          <span className="input-hint">Enter gönder · Shift+Enter satır</span>
        </div>
      </div>
    </div>
  );
}
