import React, { useEffect, useState } from 'react';
import {
  Circle, Clock, CheckCircle2,
  Lightbulb, HelpCircle, StickyNote,
  Link2, Trash2, Plus, Send, FileText,
} from 'lucide-react';
import { WikiAPI } from '../services/api';
import toast from 'react-hot-toast';

const STATUS_ICONS = {
  pending:   <Circle size={12} color="var(--muted)" strokeWidth={2} />,
  learning:  <Clock size={12} color="var(--amber)" strokeWidth={2} />,
  completed: <CheckCircle2 size={12} color="var(--green)" strokeWidth={2} />,
};
const STATUS_LABELS = {
  pending: 'Beklemede', learning: 'Öğreniliyor', completed: 'Tamamlandı',
};
const BLOCK_ICONS = {
  Concept: Lightbulb, Quiz: HelpCircle, UserNote: StickyNote,
};
const BLOCK_ICON_CLASS = {
  Concept: 'block-icon-concept', Quiz: 'block-icon-quiz', UserNote: 'block-icon-note',
};
const BLOCK_LABELS = {
  Concept: 'Kavram', Quiz: 'Quiz', UserNote: 'Notum',
};

export default function WikiPanel({ topicId, refreshKey }) {
  const [pages, setPages] = useState([]);
  const [selectedPageId, setSelectedPageId] = useState(null);
  const [pageContent, setPageContent] = useState(null);
  const [loadingPages, setLoadingPages] = useState(false);
  const [loadingPage, setLoadingPage] = useState(false);
  const [noteText, setNoteText] = useState('');
  const [addingNote, setAddingNote] = useState(false);
  const [deletingId, setDeletingId] = useState(null);

  useEffect(() => {
    if (topicId) {
      loadPages(topicId);
    } else {
      setPages([]);
      setSelectedPageId(null);
      setPageContent(null);
    }
  }, [topicId, refreshKey]);

  const loadPages = async (tid) => {
    try {
      setLoadingPages(true);
      const res = await WikiAPI.getTopicPages(tid);
      const list = res.data || [];
      setPages(list);
      if (list.length > 0) {
        await loadPage(list[0].pageId ?? list[0].id);
      } else {
        setSelectedPageId(null);
        setPageContent(null);
      }
    } catch (err) {
      console.error('Wiki sayfaları yüklenemedi', err);
    } finally {
      setLoadingPages(false);
    }
  };

  const loadPage = async (pageId) => {
    try {
      setLoadingPage(true);
      setSelectedPageId(pageId);
      const res = await WikiAPI.getPage(pageId);
      setPageContent(res.data);
    } catch {
      toast.error('Sayfa yüklenemedi.');
    } finally {
      setLoadingPage(false);
    }
  };

  const handleAddNote = async (e) => {
    e?.preventDefault();
    if (!noteText.trim() || !selectedPageId) return;
    try {
      setAddingNote(true);
      await WikiAPI.addNote(selectedPageId, { content: noteText.trim() });
      setNoteText('');
      await loadPage(selectedPageId);
      toast.success('Not eklendi!');
    } catch {
      toast.error('Not eklenemedi.');
    } finally {
      setAddingNote(false);
    }
  };

  const handleDeleteBlock = async (blockId) => {
    try {
      setDeletingId(blockId);
      await WikiAPI.deleteBlock(blockId);
      await loadPage(selectedPageId);
      toast.success('Not silindi.');
    } catch {
      toast.error('Silinemedi.');
    } finally {
      setDeletingId(null);
    }
  };

  if (!topicId) {
    return (
      <div className="wiki-layout">
        <div className="empty-state" style={{ flex: 1 }}>
          <div className="empty-icon"><FileText size={22} strokeWidth={1.5} /></div>
          <div className="empty-title">Konu seçilmedi</div>
          <div className="empty-subtitle">Bilgi haritasını görüntülemek için sol panelden bir konu seçin.</div>
        </div>
      </div>
    );
  }

  return (
    <div className="wiki-layout">
      {/* Page tree */}
      <div className="wiki-tree">
        <div className="wiki-tree-head">Bilgi Haritası</div>
        <div className="wiki-pages-list">
          {loadingPages && (
            <>
              {[...Array(5)].map((_, i) => (
                <div key={i} className="skeleton" style={{ height: 34, margin: '2px 4px' }} />
              ))}
            </>
          )}

          {!loadingPages && pages.length === 0 && (
            <div style={{
              padding: '16px 10px',
              color: 'var(--muted)',
              fontSize: '12px',
              lineHeight: 1.65,
              textAlign: 'center',
            }}>
              Henüz sayfa yok. Sohbet ederek içerik oluşturun.
            </div>
          )}

          {pages.map((page, idx) => {
            const pid = page.pageId ?? page.id;
            const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';
            const safeKey = pid && pid !== EMPTY_GUID ? pid : `page-${idx}`;
            return (
              <div
                key={safeKey}
                className={`wiki-page-item ${selectedPageId === pid ? 'active' : ''}`}
                onClick={() => loadPage(pid)}
              >
                <span className="wiki-page-status-icon">
                  {STATUS_ICONS[page.status] || STATUS_ICONS.pending}
                </span>
                <span className="wiki-page-title">{page.title}</span>
                {page.blockCount > 0 && (
                  <span className="wiki-page-blocks">{page.blockCount}</span>
                )}
              </div>
            );
          })}
        </div>
      </div>

      {/* Content */}
      <div className="wiki-content">
        {!selectedPageId && !loadingPage && (
          <div className="empty-state">
            <div className="empty-icon"><FileText size={22} strokeWidth={1.5} /></div>
            <div className="empty-title">Sayfa seçin</div>
            <div className="empty-subtitle">Sol listeden bir sayfa seçerek içeriğini görüntüleyin.</div>
          </div>
        )}

        {loadingPage && (
          <div style={{ padding: '20px', display: 'flex', flexDirection: 'column', gap: '10px' }}>
            <div className="skeleton" style={{ height: 46 }} />
            {[...Array(3)].map((_, i) => (
              <div key={i} className="skeleton" style={{ height: 110 }} />
            ))}
          </div>
        )}

        {!loadingPage && pageContent && (
          <>
            <div className="wiki-content-head">
              <div className="wiki-content-title">{pageContent.page?.title}</div>
              <span className={`wiki-status-badge wiki-status-${pageContent.page?.status || 'pending'}`}>
                {STATUS_LABELS[pageContent.page?.status] || 'Beklemede'}
              </span>
            </div>

            <div className="wiki-blocks-list">
              {(!pageContent.blocks || pageContent.blocks.length === 0) && (
                <div style={{
                  color: 'var(--muted)',
                  fontSize: '13px',
                  textAlign: 'center',
                  padding: '32px 20px',
                  lineHeight: 1.65,
                }}>
                  Bu sayfada henüz içerik yok.<br />
                  Sohbet ederken otomatik olarak doldurulacak.
                </div>
              )}

              {pageContent.blocks?.map((block, bIdx) => {
                const BlockIcon = BLOCK_ICONS[block.blockType] || Lightbulb;
                const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';
                const blockKey = block.id && block.id !== EMPTY_GUID ? block.id : `block-${bIdx}`;
                return (
                  <div key={blockKey} className="block-card">
                    <div className="block-header">
                      <div className={`block-type-icon ${BLOCK_ICON_CLASS[block.blockType] || 'block-icon-concept'}`}>
                        <BlockIcon size={12} strokeWidth={2.5} />
                      </div>
                      <div className="block-title">
                        {block.title || BLOCK_LABELS[block.blockType] || 'İçerik'}
                      </div>
                      <span className="block-source">{block.source || 'AI'}</span>
                      <div className="block-actions">
                        {block.blockType === 'UserNote' && (
                          <button
                            className="block-action-btn danger"
                            onClick={() => handleDeleteBlock(block.id)}
                            disabled={deletingId === block.id}
                            title="Notu sil"
                          >
                            <Trash2 size={11} strokeWidth={2.5} />
                          </button>
                        )}
                      </div>
                    </div>
                    <div className="block-body">{block.content}</div>
                  </div>
                );
              })}
            </div>

            {pageContent.sources?.length > 0 && (
              <div className="sources-section">
                <div className="info-section-label" style={{ paddingBottom: '8px' }}>Kaynaklar</div>
                {pageContent.sources.map((src, sIdx) => {
                  const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';
                  const srcKey = src.id && src.id !== EMPTY_GUID ? src.id : `src-${sIdx}`;
                  return (
                  <a
                    key={srcKey}
                    href={src.url}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="source-card"
                  >
                    <div className="source-icon">
                      <Link2 size={13} strokeWidth={2} />
                    </div>
                    <div className="source-info">
                      <div className="source-title">{src.title}</div>
                      <div className="source-meta">
                        {src.type}{src.durationMinutes ? ` · ${src.durationMinutes} dk` : ''}
                      </div>
                    </div>
                  </a>
                  );
                })}
              </div>
            )}

            <div className="add-note-area">
              <div className="add-note-wrap">
                <textarea
                  className="add-note-input"
                  placeholder="Not ekle... (Enter ile kaydet)"
                  value={noteText}
                  onChange={e => setNoteText(e.target.value)}
                  rows={1}
                  onKeyDown={e => {
                    if (e.key === 'Enter' && !e.shiftKey) {
                      e.preventDefault();
                      handleAddNote();
                    }
                  }}
                />
                <div className="add-note-footer">
                  <span className="add-note-label">
                    <StickyNote size={11} strokeWidth={2} style={{ display: 'inline', marginRight: 4, verticalAlign: 'middle' }} />
                    Kendi notlarınızı ekleyin
                  </span>
                  <button
                    className="btn btn-primary btn-sm"
                    onClick={handleAddNote}
                    disabled={!noteText.trim() || addingNote}
                  >
                    <Send size={11} strokeWidth={2.5} />
                    {addingNote ? 'Ekleniyor...' : 'Ekle'}
                  </button>
                </div>
              </div>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
