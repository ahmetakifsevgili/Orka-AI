// BookmarksPanel — kullanıcının kaydettiği mesajların listesi.
// Mesaj snippet, topic context ve isteğe bağlı not/etiket gösterir.
// Tıklanınca topic'e fokus, çöp ikonu ile siler.

import { useEffect, useState, useCallback } from "react";
import { motion } from "framer-motion";
import { Bookmark, Trash2, Tag, MessageSquare, RefreshCw } from "lucide-react";
import toast from "react-hot-toast";
import { BookmarksAPI, type BookmarkItem } from "@/services/api";
import type { ApiTopic } from "@/lib/types";

interface BookmarksPanelProps {
  topics: ApiTopic[];
  onFocusTopic?: (topic: ApiTopic) => void;
  onViewChange: (view: string) => void;
}

function formatDate(iso: string): string {
  try {
    return new Date(iso).toLocaleDateString("tr-TR", {
      day: "numeric",
      month: "short",
      hour: "2-digit",
      minute: "2-digit",
    });
  } catch {
    return iso;
  }
}

export default function BookmarksPanel({ topics, onFocusTopic, onViewChange }: BookmarksPanelProps) {
  const [items, setItems] = useState<BookmarkItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await BookmarksAPI.list();
      setItems(data);
    } catch (err: unknown) {
      console.error("[BookmarksPanel] list failed:", err);
      setError("Kayıtlı mesajlar yüklenemedi.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const handleDelete = useCallback(async (id: string) => {
    try {
      await BookmarksAPI.remove(id);
      setItems((prev) => prev.filter((b) => b.id !== id));
      toast.success("Kayıt silindi.");
    } catch (err: unknown) {
      console.error("[BookmarksPanel] delete failed:", err);
      toast.error("Silme başarısız.");
    }
  }, []);

  const handleOpenTopic = useCallback((bookmark: BookmarkItem) => {
    if (!bookmark.topicId) {
      onViewChange("chat");
      return;
    }
    const topic = topics.find((t) => t.id === bookmark.topicId);
    if (topic && onFocusTopic) {
      onFocusTopic(topic);
    } else {
      onViewChange("chat");
    }
  }, [topics, onFocusTopic, onViewChange]);

  return (
    <div className="flex h-full flex-col bg-transparent">
      <div className="flex-shrink-0 px-4 pt-4 sm:px-6 lg:px-8">
        <div className="flex items-center justify-between gap-3">
          <div>
            <h1 className="text-2xl font-black tracking-tight text-[#172033]">Kayıtlı Mesajlarım</h1>
            <p className="mt-1 text-sm text-[#667085]">
              Sohbet sırasında işaretlediğin yanıtlar, not ve etiketlerle birlikte burada toplanır.
            </p>
          </div>
          <button
            onClick={() => void load()}
            disabled={loading}
            className="flex items-center gap-1.5 rounded-xl border border-[#526d82]/15 bg-[#f7f9fa]/72 px-3 py-2 text-xs font-bold text-[#344054] transition hover:bg-[#eef1f3]"
            title="Yenile"
          >
            <RefreshCw className={`h-3.5 w-3.5 ${loading ? "animate-spin" : ""}`} />
            Yenile
          </button>
        </div>
      </div>

      <div className="mt-6 flex-1 overflow-y-auto px-4 pb-12 sm:px-6 lg:px-8">
        {error && (
          <div className="rounded-2xl border border-[#c77b6b]/22 bg-[#f4e1dc]/72 p-4 text-sm font-semibold text-[#9a4e3e]">
            {error}
          </div>
        )}

        {loading && items.length === 0 && (
          <div className="space-y-3">
            {[0, 1, 2].map((i) => (
              <div key={`bm-skel-${i}`} className="h-24 animate-pulse rounded-2xl bg-[#eef1f3]/60" />
            ))}
          </div>
        )}

        {!loading && items.length === 0 && !error && (
          <div className="rounded-[2rem] border border-dashed border-[#526d82]/22 bg-[#f7f9fa]/55 p-10 text-center">
            <Bookmark className="mx-auto h-8 w-8 text-[#8a97a0]" />
            <p className="mt-4 text-sm font-bold text-[#344054]">Henüz kayıtlı mesajın yok</p>
            <p className="mt-1 text-xs text-[#667085]">
              Bir AI yanıtının yanındaki "Kaydet" butonuna basarak buraya ekleyebilirsin.
            </p>
            <button
              onClick={() => onViewChange("chat")}
              className="mt-5 inline-flex items-center gap-2 rounded-xl bg-[#172033] px-4 py-2 text-xs font-extrabold text-white transition hover:bg-[#24314b]"
            >
              <MessageSquare className="h-3.5 w-3.5" />
              Sohbete dön
            </button>
          </div>
        )}

        {items.length > 0 && (
          <div className="space-y-3">
            {items.map((b) => (
              <motion.div
                key={b.id}
                initial={{ opacity: 0, y: 4 }}
                animate={{ opacity: 1, y: 0 }}
                className="rounded-2xl border border-[#526d82]/12 bg-[#f7f9fa]/72 p-4 shadow-[0_8px_22px_rgba(66,91,112,0.05)]"
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2">
                      {b.topicTitle && (
                        <span className="rounded-full bg-[#dcecf3]/68 px-2 py-0.5 text-[10px] font-extrabold uppercase tracking-wider text-[#2d5870]">
                          {b.topicTitle}
                        </span>
                      )}
                      {b.tag && (
                        <span className="inline-flex items-center gap-1 rounded-full bg-[#fff8ee] px-2 py-0.5 text-[10px] font-extrabold text-[#8a641f]">
                          <Tag className="h-2.5 w-2.5" /> {b.tag}
                        </span>
                      )}
                      <span className="text-[10px] text-[#8a97a0]">{formatDate(b.createdAt)}</span>
                    </div>
                    <p className="mt-2 text-sm leading-relaxed text-[#344054]">{b.messageSnippet}</p>
                    {b.note && (
                      <p className="mt-2 rounded-lg border-l-2 border-[#8fb7a2] bg-[#eef1f3]/55 px-3 py-1.5 text-xs italic text-[#667085]">
                        {b.note}
                      </p>
                    )}
                    <div className="mt-3 flex gap-2">
                      <button
                        onClick={() => handleOpenTopic(b)}
                        className="rounded-lg bg-[#172033] px-3 py-1 text-[11px] font-bold text-white transition hover:bg-[#24314b]"
                      >
                        Konuya git
                      </button>
                    </div>
                  </div>
                  <button
                    onClick={() => void handleDelete(b.id)}
                    title="Sil"
                    className="rounded-lg p-1.5 text-[#98a2b3] transition hover:bg-red-500/10 hover:text-red-500"
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </button>
                </div>
              </motion.div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
