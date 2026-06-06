/*
 * Design: "Claude Code Style" — Vertically stacked steps
 * Maintains history of states and renders them like terminal logs
 */

import { motion, AnimatePresence } from "framer-motion";
import { useEffect, useState } from "react";
import { Check, Loader2 } from "lucide-react";

interface ThinkingIndicatorProps {
  state: string;
}

export default function ThinkingIndicator({ state }: ThinkingIndicatorProps) {
  const [history, setHistory] = useState<string[]>([]);

  useEffect(() => {
    if (!state) return;
    setHistory((prev) => {
      // Avoid pushing duplicates back-to-back
      if (prev[prev.length - 1] === state) return prev;
      return [...prev, state];
    });
  }, [state]);

  return (
    <div className="flex flex-col gap-2 py-1">
      <AnimatePresence>
        {history.map((step, idx) => {
          const isLast = idx === history.length - 1;
          return (
            <motion.div
              key={`${step}-${idx}`}
              initial={{ opacity: 0, x: -10 }}
              animate={{ opacity: isLast ? 1 : 0.6, x: 0 }}
              className="flex items-start gap-2.5"
            >
              <div className="mt-[2px] flex-shrink-0 text-[#6f7774]">
                {isLast ? (
                  <Loader2 className="w-3.5 h-3.5 animate-spin" />
                ) : (
                  <Check className="w-3.5 h-3.5 text-[#6ed7ce]" />
                )}
              </div>
              <span className={`text-[13px] font-mono leading-tight ${isLast ? "text-[#c8cfca]" : "text-[#6f7774]"}`}>
                {step}
              </span>
            </motion.div>
          );
        })}
      </AnimatePresence>
    </div>
  );
}
