import { useCallback, useRef, useState } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { BookOpen, CheckCircle, FileText, Globe, Loader2, Upload, X } from "lucide-react";
import toast from "react-hot-toast";
import { KorteksAPI } from "@/services/api";

interface ResearchUploadModalProps {
  open: boolean;
  onClose: () => void;
  onJobStarted: (jobId: string) => void;
  topicId?: string;
}

const ACCEPTED = ".pdf,.txt,.md,.markdown";
const MAX_MB = 25;

export default function ResearchUploadModal({ open, onClose, onJobStarted, topicId }: ResearchUploadModalProps) {
  const [file, setFile] = useState<File | null>(null);
  const [query, setQuery] = useState("");
  const [requireWebSearch, setRequireWebSearch] = useState(false);
  const [loading, setLoading] = useState(false);
  const [dragOver, setDragOver] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const reset = () => {
    setFile(null);
    setQuery("");
    setRequireWebSearch(false);
    setLoading(false);
  };

  const handleClose = () => {
    reset();
    onClose();
  };

  const validateFile = (candidate: File): boolean => {
    if (candidate.size > MAX_MB * 1024 * 1024) {
      toast.error(`Dosya ${MAX_MB} MB'den büyük olamaz.`);
      return false;
    }
    const ext = candidate.name.split(".").pop()?.toLowerCase();
    const extOk = ["pdf", "txt", "md", "markdown"].includes(ext ?? "");
    const mimeOk = ["application/pdf", "text/plain", "text/markdown", "text/x-markdown"].includes(candidate.type);
    if (!mimeOk && !extOk) {
      toast.error("Yalnızca PDF, TXT veya Markdown dosyaları desteklenir.");
      return false;
    }
    return true;
  };

  const handleFileSelect = useCallback((candidate: File) => {
    if (validateFile(candidate)) setFile(candidate);
  }, []);

  const handleDrop = (event: React.DragEvent) => {
    event.preventDefault();
    setDragOver(false);
    const dropped = event.dataTransfer.files[0];
    if (dropped) handleFileSelect(dropped);
  };

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!file) {
      toast.error("Lütfen bir dosya seçin.");
      return;
    }
    if (!query.trim()) {
      toast.error("Araştırma sorusu boş olamaz.");
      return;
    }

    setLoading(true);
    try {
      const result = await KorteksAPI.startFileResearch({
        query: query.trim(),
        file,
        requireWebSearch,
        topicId,
      });
      toast.success(result.mode === "hybrid" ? "Belge + web araştırması başladı." : "Belge araştırması başladı.");
      onJobStarted(result.jobId);
      handleClose();
    } catch {
      toast.error("Araştırma başlatılamadı. Lütfen tekrar dene.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <AnimatePresence>
      {open && (
        <>
          <motion.div
            key="backdrop"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 z-40 bg-foreground/20"
            onClick={handleClose}
          />

          <motion.div
            key="modal"
            initial={{ opacity: 0, scale: 0.98, y: 10 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            exit={{ opacity: 0, scale: 0.98, y: 8 }}
            transition={{ duration: 0.18, ease: "easeOut" }}
            className="fixed inset-0 z-50 flex items-center justify-center p-4"
          >
            <form
              onSubmit={handleSubmit}
              onClick={(event) => event.stopPropagation()}
              className="w-full max-w-lg overflow-hidden rounded-xl border soft-border soft-surface soft-shadow"
            >
              <div className="flex items-center justify-between border-b soft-border px-6 py-4">
                <div>
                  <h2 className="text-sm font-semibold text-foreground">Belge ile araştırma başlat</h2>
                  <p className="mt-0.5 text-[11px] soft-text-muted">PDF, TXT veya Markdown. Maksimum {MAX_MB} MB.</p>
                </div>
                <button type="button" onClick={handleClose} className="rounded-lg p-2 soft-text-muted hover:bg-surface-muted hover:text-foreground">
                  <X className="h-4 w-4" />
                </button>
              </div>

              <div className="space-y-5 p-6">
                <div
                  onDragOver={(event) => {
                    event.preventDefault();
                    setDragOver(true);
                  }}
                  onDragLeave={() => setDragOver(false)}
                  onDrop={handleDrop}
                  onClick={() => inputRef.current?.click()}
                  className={`flex cursor-pointer flex-col items-center justify-center gap-3 rounded-xl border-2 border-dashed p-6 text-center transition-colors ${
                    dragOver
                      ? "border-emerald-500/60 bg-emerald-500/10"
                      : file
                        ? "border-emerald-500/40 bg-emerald-500/10"
                        : "soft-border soft-muted hover:bg-surface"
                  }`}
                >
                  <input
                    ref={inputRef}
                    type="file"
                    className="hidden"
                    accept={ACCEPTED}
                    onChange={(event) => {
                      const selected = event.target.files?.[0];
                      if (selected) handleFileSelect(selected);
                    }}
                  />

                  {file ? (
                    <>
                      <CheckCircle className="h-8 w-8 text-emerald-600" />
                      <div>
                        <p className="text-sm font-medium text-foreground">{file.name}</p>
                        <p className="mt-0.5 text-[11px] soft-text-muted">
                          {(file.size / 1024 / 1024).toFixed(2)} MB. Değiştirmek için tıkla.
                        </p>
                      </div>
                    </>
                  ) : (
                    <>
                      <Upload className="h-7 w-7 soft-text-muted" />
                      <div>
                        <p className="text-sm text-foreground">Dosyayı buraya sürükle veya tıkla</p>
                        <p className="mt-0.5 text-[11px] soft-text-muted">PDF, TXT, Markdown</p>
                      </div>
                    </>
                  )}
                </div>

                <div>
                  <label className="mb-1.5 block text-[12px] font-medium soft-text-muted">Araştırma sorusu</label>
                  <textarea
                    value={query}
                    onChange={(event) => setQuery(event.target.value)}
                    placeholder="Örn: Bu makalenin ana argümanlarını özetle ve eksik noktaları bul."
                    rows={3}
                    className="w-full resize-none rounded-lg border soft-border bg-transparent px-3 py-2 text-[13px] text-foreground outline-none placeholder:text-muted-foreground focus:border-emerald-500/50"
                  />
                </div>

                <div className="grid grid-cols-2 gap-3">
                  <button
                    type="button"
                    onClick={() => setRequireWebSearch(false)}
                    className={`rounded-xl border px-4 py-3 transition-colors ${
                      !requireWebSearch ? "border-emerald-500/30 bg-emerald-500/10" : "soft-border hover:bg-surface-muted"
                    }`}
                  >
                    <BookOpen className="mx-auto mb-2 h-4 w-4 soft-text-muted" />
                    <p className="text-[12px] font-semibold text-foreground">Sadece belge</p>
                    <p className="text-[10px] soft-text-muted">İnternetsiz</p>
                  </button>
                  <button
                    type="button"
                    onClick={() => setRequireWebSearch(true)}
                    className={`rounded-xl border px-4 py-3 transition-colors ${
                      requireWebSearch ? "border-emerald-500/30 bg-emerald-500/10" : "soft-border hover:bg-surface-muted"
                    }`}
                  >
                    <Globe className="mx-auto mb-2 h-4 w-4 soft-text-muted" />
                    <p className="text-[12px] font-semibold text-foreground">Belge + web</p>
                    <p className="text-[10px] soft-text-muted">Teyitli</p>
                  </button>
                </div>
              </div>

              <div className="flex gap-3 px-6 pb-5">
                <button
                  type="button"
                  onClick={handleClose}
                  className="flex-1 rounded-lg border soft-border py-2.5 text-[13px] font-medium soft-text-muted transition-colors hover:bg-surface-muted hover:text-foreground"
                >
                  İptal
                </button>
                <button
                  type="submit"
                  disabled={loading || !file || !query.trim()}
                  className="flex flex-1 items-center justify-center gap-2 rounded-lg bg-foreground py-2.5 text-[13px] font-semibold text-background transition-opacity hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-40"
                >
                  {loading ? (
                    <>
                      <Loader2 className="h-4 w-4 animate-spin" />
                      Başlatılıyor...
                    </>
                  ) : (
                    <>
                      <FileText className="h-4 w-4" />
                      Araştır
                    </>
                  )}
                </button>
              </div>
            </form>
          </motion.div>
        </>
      )}
    </AnimatePresence>
  );
}
