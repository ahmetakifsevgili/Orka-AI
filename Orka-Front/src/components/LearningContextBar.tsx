import { BookOpen, MessageSquare, PanelRightOpen, X } from "lucide-react";
import type { ActiveLearningContext, ContextRailTab } from "@/lib/types";

interface LearningContextBarProps {
  context: ActiveLearningContext | null;
  railOpen: boolean;
  onOpenRail: (tab?: ContextRailTab) => void;
  onClear: () => void;
}

export default function LearningContextBar({ context, railOpen, onOpenRail, onClear }: LearningContextBarProps) {
  if (!context?.focusTitle) return null;

  return (
    <div className="flex flex-wrap items-center gap-2 rounded-2xl border border-[#526d82]/12 bg-[#eef1f3]/68 px-3 py-2 text-[11px] font-bold text-[#344054]">
      <MessageSquare className="h-3.5 w-3.5 text-[#52768a]" />
      <span className="text-[#667085]">Odak</span>
      <span className="max-w-[20rem] truncate text-[#172033]">
        {context.focusPath || context.focusTitle}
      </span>
      <button
        type="button"
        onClick={() => onOpenRail("wiki")}
        className="ml-1 inline-flex items-center gap-1 rounded-xl bg-[#f7f9fa]/82 px-2.5 py-1 text-[#52768a] transition hover:bg-[#dcecf3]"
      >
        {railOpen ? <BookOpen className="h-3.5 w-3.5" /> : <PanelRightOpen className="h-3.5 w-3.5" />}
        Wiki yanında
      </button>
      <button
        type="button"
        onClick={onClear}
        className="rounded-xl p-1 text-[#98a2b3] transition hover:bg-[#f7f4ec] hover:text-[#172033]"
        title="Odak bağlamını temizle"
      >
        <X className="h-3.5 w-3.5" />
      </button>
    </div>
  );
}
