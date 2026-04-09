import React, { useState, useEffect } from 'react';
import { Plus, ChevronRight, ChevronDown, BookOpen } from 'lucide-react';

const PHASE_LABELS = {
  0: 'Keşif', Discovery: 'Keşif',
  1: 'Değerlendirme', Assessment: 'Değerlendirme',
  2: 'Planlama', Planning: 'Planlama',
  3: 'Öğreniyor', ActiveStudy: 'Öğreniyor',
  4: 'Tamamlandı', Completed: 'Tamamlandı',
};

const PHASE_CLASSES = {
  0: 'phase-discovery', Discovery: 'phase-discovery',
  1: 'phase-assessment', Assessment: 'phase-assessment',
  2: 'phase-planning', Planning: 'phase-planning',
  3: 'phase-activestudy', ActiveStudy: 'phase-activestudy',
  4: 'phase-completed', Completed: 'phase-completed',
};

export default function TopicSidebar({ topics, selectedTopic, onSelect, onNew }) {
  const [expanded, setExpanded] = useState(new Set());

  // Seçili konu değiştiğinde ilgili parent'ı otomatik aç
  useEffect(() => {
    if (!selectedTopic) return;
    if (selectedTopic.parentTopicId) {
      setExpanded(prev => {
        const next = new Set(prev);
        next.add(selectedTopic.parentTopicId);
        return next;
      });
    }
  }, [selectedTopic?.id]);

  // Topicları parent / child olarak ayır
  const parentTopics = topics
    .filter(t => !t.parentTopicId)
    .sort((a, b) => new Date(b.lastAccessedAt) - new Date(a.lastAccessedAt));

  const childrenOf = (parentId) =>
    topics
      .filter(t => t.parentTopicId === parentId)
      .sort((a, b) => (a.order ?? 0) - (b.order ?? 0));

  const toggleExpand = (id, e) => {
    e.stopPropagation();
    setExpanded(prev => {
      const next = new Set(prev);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });
  };

  const isActive = (topic) => selectedTopic?.id === topic.id;

  const phaseKey = (t) => t.currentPhase ?? 0;

  return (
    <div className="sidebar-left">
      <div className="sidebar-header">
        <button className="new-topic-btn" onClick={onNew}>
          <Plus size={13} strokeWidth={2.5} />
          Yeni Konu
        </button>
      </div>

      <div className="sidebar-section-label">Konularım</div>

      <div className="sidebar-topics">
        {topics.length === 0 && (
          <div style={{
            padding: '14px 12px',
            textAlign: 'center',
            color: 'var(--muted)',
            fontSize: '12px',
            lineHeight: 1.65,
          }}>
            Henüz konu yok.<br />Yeni bir konu ekleyin.
          </div>
        )}

        {parentTopics.map((topic, idx) => {
          const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';
          const safeKey = topic.id && topic.id !== EMPTY_GUID ? topic.id : `topic-${idx}`;
          const children = childrenOf(topic.id);
          const hasChildren = children.length > 0;
          const isOpen = expanded.has(topic.id);
          const active = isActive(topic);

          return (
            <div key={safeKey} className="topic-tree-node">
              {/* ── Parent Topic Row ── */}
              <div
                className={`topic-item ${active ? 'active' : ''}`}
                onClick={() => {
                  onSelect(topic);
                  if (hasChildren) {
                    setExpanded(prev => {
                      const next = new Set(prev);
                      next.has(topic.id) ? next.delete(topic.id) : next.add(topic.id);
                      return next;
                    });
                  }
                }}
              >
                <span className="topic-emoji">{topic.emoji || '📚'}</span>
                <div className="topic-info">
                  <div className="topic-name">{topic.title}</div>
                  <div className="topic-meta">
                    <span className={`topic-phase-badge ${PHASE_CLASSES[phaseKey(topic)] || 'phase-discovery'}`}>
                      {PHASE_LABELS[phaseKey(topic)] || 'Keşif'}
                    </span>
                    {hasChildren && (
                      <span style={{ fontSize: '9.5px', color: 'var(--muted)', marginLeft: '2px' }}>
                        {children.length} ders
                      </span>
                    )}
                  </div>
                </div>
                {hasChildren && (
                  <button
                    className="topic-expand-btn"
                    onClick={(e) => toggleExpand(topic.id, e)}
                    style={{
                      background: 'none',
                      border: 'none',
                      padding: '2px',
                      cursor: 'pointer',
                      color: 'var(--muted)',
                      display: 'flex',
                      alignItems: 'center',
                      flexShrink: 0,
                      transition: 'color 0.15s',
                    }}
                  >
                    {isOpen
                      ? <ChevronDown size={13} strokeWidth={2} />
                      : <ChevronRight size={13} strokeWidth={2} />}
                  </button>
                )}
              </div>

              {/* ── Child Topics (accordion) ── */}
              {hasChildren && isOpen && (
                <div
                  className="topic-children"
                  style={{
                    overflow: 'hidden',
                    animation: 'slideDown 0.18s ease-out',
                  }}
                >
                  {children.map((child, ci) => {
                    const childKey = child.id && child.id !== EMPTY_GUID ? child.id : `child-${ci}`;
                    const childActive = isActive(child);

                    return (
                      <div
                        key={childKey}
                        className={`topic-item topic-child-item ${childActive ? 'active' : ''}`}
                        onClick={() => onSelect(child)}
                        style={{
                          marginLeft: '14px',
                          paddingLeft: '8px',
                          borderLeft: childActive
                            ? '2px solid var(--amber)'
                            : '2px solid var(--border)',
                          borderRadius: '0 6px 6px 0',
                          marginBottom: '1px',
                        }}
                      >
                        <BookOpen size={12} strokeWidth={2} style={{ flexShrink: 0, color: childActive ? 'var(--amber)' : 'var(--muted)' }} />
                        <div className="topic-info">
                          <div className="topic-name" style={{ fontSize: '12px' }}>{child.title}</div>
                          <div className="topic-meta">
                            <span className={`topic-phase-badge ${PHASE_CLASSES[phaseKey(child)] || 'phase-discovery'}`}>
                              {PHASE_LABELS[phaseKey(child)] || 'Keşif'}
                            </span>
                          </div>
                        </div>
                      </div>
                    );
                  })}
                </div>
              )}
            </div>
          );
        })}

        {/* Eğer hiç parent yoksa ama child'lar varsa (eski veri) düz liste göster */}
        {parentTopics.length === 0 && topics.length > 0 && topics.map((topic, idx) => {
          const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';
          const safeKey = topic.id && topic.id !== EMPTY_GUID ? topic.id : `flat-${idx}`;
          return (
            <div
              key={safeKey}
              className={`topic-item ${isActive(topic) ? 'active' : ''}`}
              onClick={() => onSelect(topic)}
            >
              <span className="topic-emoji">{topic.emoji || '📚'}</span>
              <div className="topic-info">
                <div className="topic-name">{topic.title}</div>
              </div>
            </div>
          );
        })}
      </div>

      <div className="sidebar-footer" />
    </div>
  );
}
