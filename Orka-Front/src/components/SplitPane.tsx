import { useRef, useState, useCallback, useEffect, type ReactNode } from "react";

interface SplitPaneProps {
  left: ReactNode;
  right: ReactNode;
  defaultLeftPct?: number; // 0-100
  minLeftPct?: number;
  maxLeftPct?: number;
}

/**
 * Lightweight drag-to-resize split pane — no external library.
 * Uses pointer events for smooth desktop + touch support.
 */
export default function SplitPane({
  left,
  right,
  defaultLeftPct = 35,
  minLeftPct = 20,
  maxLeftPct = 75,
}: SplitPaneProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [leftPct, setLeftPct] = useState(defaultLeftPct);
  const dragging = useRef(false);

  const onPointerMove = useCallback((e: PointerEvent) => {
    if (!dragging.current || !containerRef.current) return;
    const rect = containerRef.current.getBoundingClientRect();
    const newPct = ((e.clientX - rect.left) / rect.width) * 100;
    setLeftPct(Math.min(maxLeftPct, Math.max(minLeftPct, newPct)));
  }, [minLeftPct, maxLeftPct]);

  const stopDrag = useCallback(() => {
    dragging.current = false;
    document.body.style.cursor = "";
    document.body.style.userSelect = "";
  }, []);

  useEffect(() => {
    window.addEventListener("pointermove", onPointerMove);
    window.addEventListener("pointerup", stopDrag);
    return () => {
      window.removeEventListener("pointermove", onPointerMove);
      window.removeEventListener("pointerup", stopDrag);
    };
  }, [onPointerMove, stopDrag]);

  const startDrag = () => {
    dragging.current = true;
    document.body.style.cursor = "col-resize";
    document.body.style.userSelect = "none";
  };

  return (
    <div ref={containerRef} className="flex-1 flex h-full max-h-screen overflow-hidden">
      {/* Left pane */}
      <div
        className="flex flex-col h-full overflow-hidden bg-zinc-950"
        style={{ width: `${leftPct}%` }}
      >
        {left}
      </div>

      {/* Resize handle */}
      <div
        onPointerDown={startDrag}
        className="
          flex-shrink-0 w-1.5 h-full group cursor-col-resize
          bg-zinc-900 border-x border-zinc-800/50
          hover:bg-emerald-500/20 active:bg-emerald-500/40
          transition-colors relative z-10
        "
      >
        {/* Grab indicator dots */}
        <div className="absolute inset-y-0 left-0 right-0 flex flex-col items-center justify-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
          {[0, 1, 2, 3, 4].map(i => (
            <div key={i} className="w-0.5 h-0.5 rounded-full bg-emerald-400" />
          ))}
        </div>
      </div>

      {/* Right pane */}
      <div
        className="flex flex-col h-full overflow-hidden bg-zinc-950"
        style={{ width: `${100 - leftPct}%` }}
      >
        {right}
      </div>
    </div>
  );
}
