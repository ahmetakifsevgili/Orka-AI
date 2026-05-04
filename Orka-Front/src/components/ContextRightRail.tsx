import { BookOpen, FileText, Lightbulb, Maximize2, NotebookTabs, PanelRightClose, StickyNote } from "lucide-react";
import type { ContextRailTab, RightRailState } from "@/lib/types";
import WikiMainPanel from "./WikiMainPanel";

interface ContextRightRailProps {
  state: RightRailState;
  onClose: () => void;
  onFullscreenWiki: (topicId: string) => void;
  onTabChange: (tab: ContextRailTab) => void;
  onSendToChat: (message: string, sourceRef?: string) => void;
}

const TABS: Array<{ id: ContextRailTab; label: string; icon: typeof BookOpen; hint: string }> = [
  { id: "wiki", label: "Wiki", icon: BookOpen, hint: "Ders notu" },
  { id: "sources", label: "Kaynak", icon: FileText, hint: "Belge ve citation" },
  { id: "practice", label: "Pekiştir", icon: Lightbulb, hint: "Kart, öneri, mikro quiz" },
  { id: "notes", label: "Not", icon: StickyNote, hint: "Kişisel bağlam" },
];

export default function ContextRightRail({
  state,
  onClose,
  onFullscreenWiki,
  onTabChange,
  onSendToChat,
}: ContextRightRailProps) {
  if (!state.isOpen || !state.topicId) return null;

  const title = state.title || "Bağlam";

  return (
    <aside className="flex h-full min-h-0 w-full flex-col overflow-hidden bg-[#f3f6f7]/92">
      <div className="flex shrink-0 items-start justify-between gap-3 border-b border-[#526d82]/12 bg-[#f7f9fa]/76 px-4 py-3 backdrop-blur-xl">
        <div className="min-w-0">
          <div className="flex items-center gap-2 text-[10px] font-black uppercase tracking-[0.18em] text-[#667085]">
            <NotebookTabs className="h-3.5 w-3.5" />
            Sağ bağlam rayı
          </div>
          <h2 className="mt-1 truncate text-sm font-black text-[#172033]">{title}</h2>
          <p className="mt-0.5 truncate text-[11px] font-semibold text-[#667085]">
            Chat bu konuya odaklı çalışır; kaynaklar burada yanında kalır.
          </p>
        </div>
        <div className="flex items-center gap-1.5">
          <button
            type="button"
            onClick={() => onFullscreenWiki(state.topicId!)}
            className="rounded-xl border border-[#526d82]/12 bg-[#eef1f3]/82 p-2 text-[#667085] transition hover:bg-[#f7f4ec] hover:text-[#172033]"
            title="Wiki'yi tam ekran aç"
          >
            <Maximize2 className="h-4 w-4" />
          </button>
          <button
            type="button"
            onClick={onClose}
            className="rounded-xl border border-[#526d82]/12 bg-[#eef1f3]/82 p-2 text-[#667085] transition hover:bg-[#f7f4ec] hover:text-[#172033]"
            title="Sağ paneli kapat"
          >
            <PanelRightClose className="h-4 w-4" />
          </button>
        </div>
      </div>

      <div className="shrink-0 border-b border-[#526d82]/10 bg-[#eef1f3]/58 px-3 py-2">
        <div className="grid grid-cols-4 gap-1.5">
          {TABS.map((tab) => {
            const Icon = tab.icon;
            const active = state.tab === tab.id;
            return (
              <button
                key={tab.id}
                type="button"
                onClick={() => onTabChange(tab.id)}
                className={`rounded-2xl border px-2 py-2 text-left transition ${
                  active
                    ? "border-[#172033]/10 bg-[#172033] text-white shadow-sm"
                    : "border-[#526d82]/10 bg-[#f7f9fa]/72 text-[#667085] hover:bg-[#fff8ee] hover:text-[#172033]"
                }`}
                title={tab.hint}
              >
                <Icon className="mb-1 h-3.5 w-3.5" />
                <span className="block truncate text-[10px] font-black">{tab.label}</span>
              </button>
            );
          })}
        </div>
      </div>

      <div className="min-h-0 flex-1 overflow-hidden">
        <WikiMainPanel
          topicId={state.topicId}
          onClose={onClose}
          variant="rail"
          activeRailTab={state.tab}
          onSendToChat={(message) => onSendToChat(message, state.sourceRef)}
        />
      </div>
    </aside>
  );
}
