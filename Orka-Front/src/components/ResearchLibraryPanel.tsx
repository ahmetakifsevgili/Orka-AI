import { useCallback, useEffect, useMemo, useState } from "react";
import { AnimatePresence, motion } from "framer-motion";
import * as signalR from "@microsoft/signalr";
import { Clock, FileText, FlaskConical, Loader2, Plus, Search } from "lucide-react";
import { KorteksAPI, storage } from "@/services/api";
import MarkdownRender from "./MarkdownRender";
import ResearchToolbar from "./ResearchToolbar";
import ResearchUploadModal from "./ResearchUploadModal";

interface ResearchEntry {
  id: string;
  query: string;
  phase: string;
  completedAt: string;
  hasReport: boolean;
  topicId?: string;
}

export default function ResearchLibraryPanel() {
  const [entries, setEntries] = useState<ResearchEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [uploadOpen, setUploadOpen] = useState(false);
  const [report, setReport] = useState<{ query: string; report: string; completedAt: string } | null>(null);
  const [reportLoading, setReportLoading] = useState(false);
  const [searchQuery, setSearchQuery] = useState("");

  const loadLibrary = useCallback(() => {
    setLoading(true);
    KorteksAPI.getLibrary()
      .then((data) => setEntries(data))
      .catch(() => setEntries([]))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    loadLibrary();
  }, [loadLibrary]);

  useEffect(() => {
    const token = storage.getToken();
    if (!token) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/korteks", { accessTokenFactory: () => token })
      .withAutomaticReconnect()
      .build();

    connection.on("JobPhaseUpdated", (info: { jobId: string; phase: string }) => {
      setEntries((prev) => {
        const exists = prev.some((entry) => entry.id === info.jobId);
        if (!exists || info.phase === "Completed") {
          loadLibrary();
          return prev;
        }
        return prev.map((entry) => (entry.id === info.jobId ? { ...entry, phase: info.phase } : entry));
      });
    });

    connection.start().catch((err) => console.error("SignalR Connection Error: ", err));
    return () => {
      connection.stop();
    };
  }, [loadLibrary]);

  const handleSelect = useCallback(
    async (id: string) => {
      if (selectedId === id) {
        setSelectedId(null);
        setReport(null);
        return;
      }
      setSelectedId(id);
      setReportLoading(true);
      try {
        const data = await KorteksAPI.getReport(id);
        setReport(data);
      } catch {
        setReport(null);
      } finally {
        setReportLoading(false);
      }
    },
    [selectedId]
  );

  const filteredEntries = useMemo(
    () => entries.filter((entry) => entry.query.toLowerCase().includes(searchQuery.toLowerCase())),
    [entries, searchQuery]
  );

  const formatDate = (iso: string) => {
    if (!iso) return "-";
    return new Date(iso).toLocaleDateString("tr-TR", {
      day: "2-digit",
      month: "short",
      year: "numeric",
      hour: "2-digit",
      minute: "2-digit",
    });
  };

  return (
    <div className="flex h-full flex-col soft-page">
      <div className="flex flex-shrink-0 items-center gap-4 border-b soft-border soft-surface px-8 py-5">
        <div className="flex h-10 w-10 items-center justify-center rounded-xl soft-muted">
          <FlaskConical className="h-5 w-5 text-emerald-700 dark:text-emerald-300" />
        </div>
        <div>
          <h1 className="text-base font-semibold text-foreground">Korteks araştırmaları</h1>
          <p className="text-xs soft-text-muted">Tamamlanan raporlar ve araştırma notları.</p>
        </div>
        <div className="ml-auto flex items-center gap-4">
          <span className="text-xs font-medium soft-text-muted">{entries.length} araştırma</span>
          <button
            onClick={() => setUploadOpen(true)}
            className="flex items-center gap-1.5 rounded-lg bg-foreground px-4 py-2 text-xs font-medium text-background hover:opacity-90"
          >
            <Plus className="h-4 w-4" />
            Yeni araştırma
          </button>
        </div>
      </div>

      <ResearchUploadModal
        open={uploadOpen}
        onClose={() => setUploadOpen(false)}
        onJobStarted={() => {
          setUploadOpen(false);
          loadLibrary();
        }}
      />

      <div className="flex min-h-0 flex-1">
        <aside className="flex w-80 flex-shrink-0 flex-col border-r soft-border soft-surface">
          <div className="border-b soft-border p-4">
            <div className="relative">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 soft-text-muted" />
              <input
                type="text"
                placeholder="Araştırma ara..."
                value={searchQuery}
                onChange={(event) => setSearchQuery(event.target.value)}
                className="w-full rounded-lg border soft-border bg-transparent py-2.5 pl-9 pr-3 text-sm text-foreground outline-none placeholder:text-muted-foreground focus:border-emerald-500/50"
              />
            </div>
          </div>

          <div className="flex-1 overflow-y-auto p-3">
            {loading ? (
              <div className="flex h-40 items-center justify-center">
                <Loader2 className="h-5 w-5 animate-spin soft-text-muted" />
              </div>
            ) : filteredEntries.length === 0 ? (
              <div className="px-4 py-12 text-center text-sm soft-text-muted">
                {searchQuery ? "Böyle bir araştırma bulunamadı." : "Henüz araştırma yok."}
              </div>
            ) : (
              <div className="space-y-1.5">
                {filteredEntries.map((entry) => (
                  <button
                    key={entry.id}
                    onClick={() => handleSelect(entry.id)}
                    className={`w-full rounded-lg border p-3 text-left transition-colors ${
                      selectedId === entry.id
                        ? "border-emerald-500/30 bg-emerald-500/10"
                        : "border-transparent hover:bg-surface-muted"
                    }`}
                  >
                    <div className="flex gap-3">
                      <div className="mt-0.5 flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-lg soft-muted">
                        <FileText className="h-4 w-4 soft-text-muted" />
                      </div>
                      <div className="min-w-0">
                        <p className="truncate text-sm font-medium text-foreground">{entry.query}</p>
                        <p className="mt-1 flex items-center gap-1.5 text-[11px] soft-text-muted">
                          <Clock className="h-3 w-3" />
                          {formatDate(entry.completedAt)}
                        </p>
                      </div>
                    </div>
                  </button>
                ))}
              </div>
            )}
          </div>
        </aside>

        <main className="flex-1 overflow-y-auto">
          <AnimatePresence mode="wait">
            {!selectedId ? (
              <motion.div
                key="empty"
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                className="flex h-full flex-col items-center justify-center px-10 text-center"
              >
                <div className="mb-4 flex h-14 w-14 items-center justify-center rounded-xl soft-muted">
                  <FlaskConical className="h-7 w-7 soft-text-muted" />
                </div>
                <h2 className="text-base font-semibold text-foreground">Bir rapor seç</h2>
                <p className="mt-2 max-w-sm text-sm leading-relaxed soft-text-muted">
                  Sol listeden bir Korteks araştırmasını açarak raporu okuyabilir veya dışa aktarabilirsin.
                </p>
              </motion.div>
            ) : reportLoading ? (
              <motion.div key="loading" className="flex h-full items-center justify-center">
                <Loader2 className="h-6 w-6 animate-spin soft-text-muted" />
              </motion.div>
            ) : report ? (
              <motion.article
                key={selectedId}
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0 }}
                className="mx-auto max-w-4xl px-10 py-10"
              >
                <header className="mb-8 border-b soft-border pb-6">
                  <p className="mb-3 flex items-center gap-2 text-xs soft-text-muted">
                    <Clock className="h-3.5 w-3.5" />
                    {formatDate(report.completedAt)}
                  </p>
                  <h2 className="text-2xl font-semibold leading-tight text-foreground">{report.query}</h2>
                </header>
                <div className="prose prose-orka max-w-none">
                  <MarkdownRender>{report.report}</MarkdownRender>
                </div>
                <div className="mt-10 border-t soft-border pt-6">
                  <ResearchToolbar content={report.report} topic={report.query} />
                </div>
              </motion.article>
            ) : (
              <motion.div key="error" className="flex h-full items-center justify-center">
                <p className="text-sm text-amber-700 dark:text-amber-300">
                  Rapor verisine ulaşılamadı veya sunucu bağlantısı koptu.
                </p>
              </motion.div>
            )}
          </AnimatePresence>
        </main>
      </div>
    </div>
  );
}
