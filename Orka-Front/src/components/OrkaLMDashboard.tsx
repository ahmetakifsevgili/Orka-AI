/*
 * OrkaLMDashboard — NotebookLM-inspired source notebook gallery
 * Dark, minimal, premium. Referans: Google NotebookLM arayüzü.
 *
 * Yapı:
 *   - Sol kolon: notebook grid (oluştur + mevcut defterler)
 *   - Sağ panel: seçili defterin kaynak/audio overview önizlemesi
 *
 * Backend: sadece mevcut TopicsAPI.create + topics listesi.
 * Tüm diğer etkileşimler WikiMainPanel üzerinden yönetiliyor (Home.tsx'te).
 */

import { useState } from "react";
import {
  BookOpen,
  FileText,
  Headphones,
  Loader2,
  Mic,
  Plus,
  Search,
  Sparkles,
  Clock,
  ChevronRight,
} from "lucide-react";
import { TopicsAPI } from "@/services/api";
import type { ApiTopic } from "@/lib/types";
import toast from "react-hot-toast";

interface OrkaLMDashboardProps {
  topics: ApiTopic[];
  onSelectTopic: (topic: ApiTopic) => void;
  onTopicCreated: (topic: ApiTopic) => void;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function relativeDate(raw?: string | null): string {
  if (!raw) return "";
  const diff = Date.now() - new Date(raw).getTime();
  const m = Math.floor(diff / 60_000);
  if (m < 1) return "Az önce";
  if (m < 60) return `${m}d önce`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h}sa önce`;
  const d = Math.floor(h / 24);
  return `${d}g önce`;
}

const NOTEBOOK_COLORS = [
  "from-[#1a2535] to-[#0f1620]",
  "from-[#1e2030] to-[#111525]",
  "from-[#1a2520] to-[#0f1a14]",
  "from-[#241e30] to-[#170f20]",
  "from-[#252018] to-[#18140e]",
];

function notebookColor(id: string) {
  const hash = id.split("").reduce((a, c) => a + c.charCodeAt(0), 0);
  return NOTEBOOK_COLORS[hash % NOTEBOOK_COLORS.length];
}

// ── Sub-components ─────────────────────────────────────────────────────────────

function EmptyState() {
  return (
    <div className="flex flex-col items-center justify-center gap-4 py-20 text-center">
      <div className="flex h-16 w-16 items-center justify-center rounded-2xl border border-white/[0.08] bg-white/[0.04]">
        <BookOpen className="h-7 w-7 text-zinc-500" />
      </div>
      <div>
        <p className="text-[15px] font-medium text-zinc-300">Henüz defter yok</p>
        <p className="mt-1 text-sm text-zinc-600">
          Yeni bir defter oluşturarak kaynaklarını yüklemeye başla.
        </p>
      </div>
    </div>
  );
}

function NotebookCard({
  topic,
  selected,
  onClick,
}: {
  topic: ApiTopic;
  selected: boolean;
  onClick: () => void;
}) {
  const gradient = notebookColor(topic.id);
  const date = relativeDate(topic.updatedAt ?? topic.createdAt);

  return (
    <button
      onClick={onClick}
      className={`group relative flex flex-col gap-0 rounded-2xl border text-left transition-all duration-200 focus:outline-none overflow-hidden ${
        selected
          ? "border-zinc-600 ring-1 ring-zinc-600/50"
          : "border-zinc-800 hover:border-zinc-700"
      }`}
    >
      {/* Top gradient area */}
      <div className={`h-24 w-full bg-gradient-to-br ${gradient} relative`}>
        <div className="absolute inset-0 flex items-end px-4 pb-3">
          <span className="text-2xl">{topic.emoji ?? "📔"}</span>
        </div>
        {selected && (
          <div className="absolute right-3 top-3">
            <span className="flex items-center gap-1 rounded-full border border-zinc-500/40 bg-zinc-800/80 px-2 py-0.5 text-[10px] font-medium text-zinc-300 backdrop-blur-sm">
              Açık
            </span>
          </div>
        )}
      </div>

      {/* Bottom info */}
      <div className="flex flex-col gap-1 bg-zinc-900 px-4 py-3">
        <h3 className="line-clamp-1 text-[13px] font-semibold text-zinc-100">
          {topic.title}
        </h3>
        {date && (
          <p className="flex items-center gap-1 text-[11px] text-zinc-600">
            <Clock className="h-3 w-3" />
            {date}
          </p>
        )}
      </div>
    </button>
  );
}

function CreateCard({ onClick, loading }: { onClick: () => void; loading: boolean }) {
  return (
    <button
      onClick={onClick}
      disabled={loading}
      className="group relative flex h-full min-h-[148px] flex-col items-center justify-center gap-3 rounded-2xl border border-dashed border-zinc-800 bg-transparent transition-all duration-200 hover:border-zinc-700 hover:bg-zinc-900/40 focus:outline-none disabled:cursor-not-allowed disabled:opacity-60"
    >
      {loading ? (
        <Loader2 className="h-7 w-7 animate-spin text-zinc-500" />
      ) : (
        <div className="flex h-10 w-10 items-center justify-center rounded-xl border border-zinc-700 bg-zinc-800/60 text-zinc-400 transition-colors group-hover:border-zinc-600 group-hover:text-zinc-300">
          <Plus className="h-5 w-5" />
        </div>
      )}
      <span className="text-[13px] font-medium text-zinc-500 group-hover:text-zinc-400">
        Yeni defter
      </span>
    </button>
  );
}

function SidePanel({
  topic,
  onOpen,
}: {
  topic: ApiTopic | null;
  onOpen: () => void;
}) {
  if (!topic) {
    return (
      <div className="flex h-full flex-col items-center justify-center gap-3 px-8 text-center">
        <div className="flex h-14 w-14 items-center justify-center rounded-2xl border border-white/[0.06] bg-white/[0.03]">
          <Sparkles className="h-6 w-6 text-zinc-600" />
        </div>
        <p className="text-sm font-medium text-zinc-500">
          Bir defter seç veya oluştur
        </p>
        <p className="text-[12px] text-zinc-700">
          Kaynaklarını yükle, Orka otomatik wiki üretsin ve seninle konuşsun.
        </p>
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col overflow-hidden">
      {/* Header */}
      <div className="flex shrink-0 flex-col gap-1 border-b border-zinc-800 px-6 py-5">
        <div className="flex items-center gap-2">
          <span className="text-xl">{topic.emoji ?? "📔"}</span>
          <h2 className="text-[15px] font-semibold text-zinc-100 leading-snug">
            {topic.title}
          </h2>
        </div>
        <p className="text-[12px] text-zinc-600">
          {relativeDate(topic.updatedAt ?? topic.createdAt)} güncellendi
        </p>
      </div>

      {/* Feature blocks */}
      <div className="flex flex-1 flex-col gap-2 overflow-y-auto px-5 py-5">

        {/* Sources */}
        <div className="rounded-xl border border-zinc-800 bg-zinc-900/60 p-4">
          <div className="mb-3 flex items-center justify-between">
            <div className="flex items-center gap-2">
              <FileText className="h-3.5 w-3.5 text-zinc-500" />
              <span className="text-[12px] font-semibold text-zinc-300">Kaynaklar</span>
            </div>
            <button
              onClick={onOpen}
              className="flex items-center gap-1 rounded-lg border border-zinc-700 bg-zinc-800 px-2.5 py-1 text-[11px] font-medium text-zinc-300 transition-colors hover:border-zinc-600 hover:text-zinc-100"
            >
              <Plus className="h-3 w-3" />
              Ekle
            </button>
          </div>
          <p className="text-[12px] text-zinc-600">
            PDF, web sayfası veya metin ekle. Orka bunları okuyup Wiki ile RAG yanıtları için indeksler.
          </p>
          <div className="mt-3 flex items-center gap-2 rounded-lg border border-zinc-800 bg-zinc-950/60 px-3 py-2">
            <FileText className="h-3.5 w-3.5 shrink-0 text-zinc-600" />
            <span className="text-[11px] text-zinc-600">Kaynak yüklemek için defteri aç →</span>
          </div>
        </div>

        {/* Audio Overview */}
        <div className="rounded-xl border border-zinc-800 bg-zinc-900/60 p-4">
          <div className="mb-3 flex items-center gap-2">
            <Headphones className="h-3.5 w-3.5 text-zinc-500" />
            <span className="text-[12px] font-semibold text-zinc-300">Audio Overview</span>
            <span className="rounded-full border border-[#6ed7ce]/20 bg-[#6ed7ce]/8 px-1.5 py-0.5 text-[9px] font-semibold text-[#6ed7ce]">
              BETA
            </span>
          </div>
          <p className="text-[12px] text-zinc-600">
            İki AI sunucu defterin içeriğini tartışan podcast tarzı bir ses özeti üretir.
          </p>
          <button
            onClick={onOpen}
            className="mt-3 flex w-full items-center justify-center gap-2 rounded-lg border border-zinc-800 bg-zinc-800/60 px-3 py-2.5 text-[12px] font-medium text-zinc-400 transition-colors hover:border-zinc-700 hover:text-zinc-300"
          >
            <Mic className="h-3.5 w-3.5" />
            Ses özeti oluştur
          </button>
        </div>

        {/* Wiki */}
        <div className="rounded-xl border border-zinc-800 bg-zinc-900/60 p-4">
          <div className="mb-3 flex items-center gap-2">
            <BookOpen className="h-3.5 w-3.5 text-zinc-500" />
            <span className="text-[12px] font-semibold text-zinc-300">Otomatik Wiki</span>
          </div>
          <p className="text-[12px] text-zinc-600">
            Kaynak yüklendikçe Orka otomatik kavram sayfaları, ilişki haritaları ve özet kartları üretir.
          </p>
          <button
            onClick={onOpen}
            className="mt-3 flex w-full items-center justify-center gap-1.5 rounded-lg border border-zinc-800 bg-zinc-800/60 px-3 py-2.5 text-[12px] font-medium text-zinc-400 transition-colors hover:border-zinc-700 hover:text-zinc-300"
          >
            Wiki'yi aç
            <ChevronRight className="h-3.5 w-3.5" />
          </button>
        </div>
      </div>

      {/* Open button */}
      <div className="shrink-0 border-t border-zinc-800 px-5 py-4">
        <button
          onClick={onOpen}
          className="flex w-full items-center justify-center gap-2 rounded-xl bg-zinc-100 py-3 text-[13px] font-semibold text-zinc-950 transition-colors hover:bg-white"
        >
          Defteri aç
          <ChevronRight className="h-4 w-4" />
        </button>
      </div>
    </div>
  );
}

// ── Main Component ─────────────────────────────────────────────────────────────

export default function OrkaLMDashboard({
  topics,
  onSelectTopic,
  onTopicCreated,
}: OrkaLMDashboardProps) {
  const [isCreating, setIsCreating] = useState(false);
  const [search, setSearch] = useState("");
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const notebooks = topics
    .filter(
      (t) =>
        !t.parentTopicId &&
        t.category === "Notebook" &&
        t.title.toLowerCase().includes(search.toLowerCase())
    );

  const selectedNotebook = notebooks.find((n) => n.id === selectedId) ?? null;

  const handleCreate = async () => {
    setIsCreating(true);
    try {
      const response = await TopicsAPI.create({
        title: "Yeni Defter",
        emoji: "📔",
        category: "Notebook",
      });
      onTopicCreated(response.data);
      setSelectedId(response.data.id);
      toast.success("Defter oluşturuldu.");
    } catch {
      toast.error("Defter oluşturulamadı.");
    } finally {
      setIsCreating(false);
    }
  };

  const handleOpen = () => {
    if (selectedNotebook) onSelectTopic(selectedNotebook);
  };

  return (
    <div className="flex h-full overflow-hidden bg-zinc-950">
      {/* ── Left: Notebook grid ───────────────────────────────────────── */}
      <div className="flex flex-1 flex-col overflow-hidden border-r border-zinc-800/70">
        {/* Header */}
        <div className="shrink-0 px-8 pb-5 pt-8">
          <h1 className="text-[22px] font-semibold leading-none tracking-tight text-zinc-50">
            OrkaLM Stüdyosu
          </h1>
          <p className="mt-1.5 text-[13px] text-zinc-500">
            Dokümanlarını yükle — Orka wiki üretsin, seninle konuşsun.
          </p>
        </div>

        {/* Search */}
        <div className="shrink-0 px-8 pb-5">
          <div className="relative">
            <Search className="absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-zinc-600" />
            <input
              type="text"
              placeholder="Defterlerde ara…"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="w-full max-w-[320px] rounded-xl border border-zinc-800 bg-zinc-900 py-2 pl-9 pr-4 text-[13px] text-zinc-200 placeholder-zinc-600 outline-none transition-colors focus:border-zinc-600 focus:ring-1 focus:ring-zinc-700/50"
            />
          </div>
        </div>

        {/* Grid */}
        <div className="flex-1 overflow-y-auto px-8 pb-8">
          {notebooks.length === 0 && !isCreating ? (
            <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
              <CreateCard onClick={handleCreate} loading={isCreating} />
              <div className="col-span-full">
                <EmptyState />
              </div>
            </div>
          ) : (
            <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
              <CreateCard onClick={handleCreate} loading={isCreating} />
              {notebooks.map((topic) => (
                <NotebookCard
                  key={topic.id}
                  topic={topic}
                  selected={selectedId === topic.id}
                  onClick={() =>
                    setSelectedId((prev) =>
                      prev === topic.id ? null : topic.id
                    )
                  }
                />
              ))}
            </div>
          )}
        </div>
      </div>

      {/* ── Right: Side panel ────────────────────────────────────────── */}
      <div className="hidden w-[300px] shrink-0 overflow-hidden xl:flex xl:flex-col">
        <SidePanel topic={selectedNotebook} onOpen={handleOpen} />
      </div>
    </div>
  );
}
