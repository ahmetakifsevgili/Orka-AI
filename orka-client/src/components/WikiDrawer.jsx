import React from 'react';
import { X, Map } from 'lucide-react';
import WikiPanel from './WikiPanel';

/**
 * WikiDrawer — Sağ taraf bilgi haritası paneli.
 * Global chat'i kapatmadan topic wiki içeriğini gösterir.
 */
export default function WikiDrawer({ topic, onClose, refreshKey }) {
  return (
    <div className="wiki-drawer">
      <div className="wiki-drawer-head">
        <div className="wiki-drawer-title">
          <Map size={13} strokeWidth={2} style={{ color: 'var(--amber)', flexShrink: 0 }} />
          <span>{topic?.emoji || '📚'} {topic?.title || 'Bilgi Haritası'}</span>
        </div>
        <button className="wiki-drawer-close" onClick={onClose} title="Kapat">
          <X size={14} strokeWidth={2.5} />
        </button>
      </div>
      <div style={{ flex: 1, overflow: 'hidden', display: 'flex' }}>
        <WikiPanel topicId={topic?.id} refreshKey={refreshKey} />
      </div>
    </div>
  );
}
